using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Rest;
using Discord.WebSocket;
using Example;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Doccer_Bot.Services
{
    public class ScheduleService
    {
        private readonly DiscordSocketClient _discord;
        private readonly IConfiguration _config;

        private readonly TextMemeService _textMemeService;
        private readonly LoggingService _logger;

        private ITextChannel _reminderChannel;
        public IUserMessage _eventEmbedMessage;

        // DiscordSocketClient, CommandService, and IConfigurationRoot are injected automatically from the IServiceProvider
        public ScheduleService(
            DiscordSocketClient discord,
            IConfigurationRoot config,
            TextMemeService textMemeService,
            LoggingService logger)
        {
            _config = config;
            _discord = discord;

            _textMemeService = textMemeService;
            _logger = logger;
        }

        public async Task Initialize()
        {
            // get id of reminders channel from config
            var reminderChannelId = Convert.ToUInt64(_config["reminderChannelId"]);
            _reminderChannel = _discord.GetChannel(reminderChannelId) as ITextChannel;
        }

        // send or modify messages alerting the user that an event will be starting soon
        public async Task HandleReminders()
        {
            foreach (var calendarEvent in CalendarEvents.Events)
            {
                // look for pre-existing reminder messages containing this event's title
                // if we find one, and we don't already have a alertmessage stored,
                // set it as this event's alert message and use that for modification.
                // this should only come into play if the hour alert message has been sent and the bot is restarted after
                var oldReminderMessage = await GetPreviousReminderMessage(calendarEvent.Name);
                if (oldReminderMessage != null && calendarEvent.AlertMessage == null)
                    calendarEvent.AlertMessage = oldReminderMessage;

                // get amount of time between the calendarevent start time and the current time
                var timeDelta = calendarEvent.StartDate - TimezoneAdjustedDateTime.Now.Invoke();

                // if it's less than an hour but more than fifteen minutes, and we haven't sent an alert message, send an alert message
                if (timeDelta.TotalHours < 1 && timeDelta.TotalMinutes > 15)
                {
                    var messageContents =
                        $"{calendarEvent.Name} is starting in {(int) timeDelta.TotalMinutes} minutes.";

                    // if there's an alert message already, edit it
                    if (calendarEvent.AlertMessage != null)
                    {
                        await calendarEvent.AlertMessage.ModifyAsync(m => m.Content = messageContents);
                    }
                    // if there wasn't an alert message, send a new message
                    else
                    {
                        var msg = await _reminderChannel.SendMessageAsync(messageContents);
                        calendarEvent.AlertMessage = msg;
                    }
                }

                // if it's less than an hour and less or equal to fifteen minutes, try to modify an existing alert message or send a new one
                if (timeDelta.TotalHours < 1 && timeDelta.TotalMinutes <= 15)
                {
                    var messageContents = $"{calendarEvent.Name} is starting shortly. Look for a party finder soon.";

                    // if there's an alert message already, edit it
                    if (calendarEvent.AlertMessage != null)
                    {
                        await calendarEvent.AlertMessage.ModifyAsync(m => m.Content = messageContents);
                    }
                    // if there wasn't an alert message, send a new message
                    else
                    {
                        var msg = await _reminderChannel.SendMessageAsync(messageContents);
                        calendarEvent.AlertMessage = msg;
                    }
                }

                // if the event has past, delete the alert and null the event's alertmessage
                // (nulling the alertmessage is a precautionary thing in case we somehow carry
                // over a previous calendarEvent entry)
                if (calendarEvent.StartDate < TimezoneAdjustedDateTime.Now.Invoke())
                {
                    await calendarEvent.AlertMessage.DeleteAsync();
                    calendarEvent.AlertMessage = null;
                }
            }
        }

        // send or modify embed messages listing upcoming events from the raid calendar
        public async Task SendEvents()
        {
            // build embed
            var embed = BuildEventsEmbed();

            // check if we haven't set an embed message yet
            if (_eventEmbedMessage == null)
            {
                // try to get a pre-existing event embed
                var oldEmbedMessage = await GetPreviousEmbed();

                // if we found a pre-existing event embed, set it as our current event embed message
                // and edit it
                if (oldEmbedMessage != null)
                {
                    _eventEmbedMessage = oldEmbedMessage;
                    await _eventEmbedMessage.ModifyAsync(m => { m.Embed = embed; });
                }
                // otherwise, send a new one and set it as our current event embed message
                else
                {
                    // send embed
                    var message = await _reminderChannel.SendMessageAsync(null, false, embed);

                    // store message id
                    _eventEmbedMessage = message;
                }

            } // if we have set a current event embed message, edit it
            else
            {
                await _eventEmbedMessage.ModifyAsync(m => { m.Embed = embed; });
            }
        }

        // posts the list of future events into the channel that called the command
        public async Task GetEvents(SocketCommandContext context)
        {
            // if command context is the reminders channel, there's already an event embed
            // so just update it instead of sending a new embed
            if (context.Channel.Id == _reminderChannel.Id)
                await SendEvents();
            else // if context is not the reminders channel, send new embed
            {
                var embed = BuildEventsEmbed();
                await context.Channel.SendMessageAsync(null, false, embed);
            }
        }

        // put together the events embed & return it to calling method
        private Embed BuildEventsEmbed()
        {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            // if there are no items in CalendarEvents, build a field stating so
            if (CalendarEvents.Events.Count == 0)
            {
                embedBuilder.AddField("No raids scheduled.", _textMemeService.GetMemeTextForNoEvents());
            }

            // iterate through each calendar event and build strings from them
            // if there are no events, the foreach loop is skipped, so no need to check
            foreach (var calendarEvent in CalendarEvents.Events)
            {
                // don't add items from the past
                if (calendarEvent.StartDate < TimezoneAdjustedDateTime.Now.Invoke())
                    continue;

                // get the time difference between the event and now
                TimeSpan timeDelta = (calendarEvent.StartDate - TimezoneAdjustedDateTime.Now.Invoke());

                // holy fucking formatting batman
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.AppendLine($"Starts on {calendarEvent.StartDate,0:M/dd} at {calendarEvent.StartDate,0: h:mm tt} {calendarEvent.Timezone} and ends at {calendarEvent.EndDate,0: h:mm tt} {calendarEvent.Timezone}");
                stringBuilder.Append(":watch: Starts in ");
                // days
                if (timeDelta.Days == 1)
                    stringBuilder.Append($" {timeDelta.Days} day");
                if (timeDelta.Days > 1)
                    stringBuilder.Append($" {timeDelta.Days} days");
                // comma
                if (timeDelta.Days >= 1 && (timeDelta.Hours > 0 || timeDelta.Minutes > 0))
                    stringBuilder.Append(",");
                // hours
                if (timeDelta.Hours == 1)
                    stringBuilder.Append($" {timeDelta.Hours} hour");
                if (timeDelta.Hours > 1)
                    stringBuilder.Append($" {timeDelta.Hours} hours");
                // and
                if (timeDelta.Hours > 0 && timeDelta.Minutes > 0)
                    stringBuilder.Append(" and");
                // minutes
                if (timeDelta.Minutes == 1)
                    stringBuilder.Append($" {timeDelta.Minutes} minute");
                if (timeDelta.Minutes > 1)
                    stringBuilder.Append($" {timeDelta.Minutes} minutes");

                stringBuilder.Append(".");

                // bundle it all together into a line for the embed
                embedBuilder.AddField($"{calendarEvent.Name}", stringBuilder.ToString());
            }

            // add the extra little embed bits
            embedBuilder.WithTitle("Schedule")
                .WithColor(Color.Blue)
                .WithFooter("Synced: ")
                // set the actual datetime value since discord timestamps
                // are timezone-aware (?)
                .WithTimestamp(DateTime.Now);

            // roll it all up and send it to the channel
            var embed = embedBuilder.Build();
            return embed;
        }

        // searches the _reminderChannel for a message from the bot containing an embed (how else can we filter this - title?)
        // if it finds one, return that message to the calling method to be set as _eventEmbedMessage
        private async Task<IUserMessage> GetPreviousEmbed()
        {
            // get all messages in reminder channel
            var messages = await _reminderChannel.GetMessagesAsync().FlattenAsync();
            // try to get a pre-existing embed message matching our usual event embed parameters
            // return the results
            try
            {
                var embedMsg = messages.Where(msg => msg.Author.Id == _discord.CurrentUser.Id)
                    .Where(msg => msg.Embeds.Count > 0)
                    .Where(msg => msg.Embeds.First().Title == "Schedule").ToList().First();
                return (IUserMessage)embedMsg;
            }
            catch
            {
                return null;
            }
        }

        // searches the _reminderChannel for a message from the bot containing the passed param
        // (this should be the title of an event for which we are looking for a remindermessage to edit)
        // if it finds one, return that message to the calling method to be modified
        private async Task<IUserMessage> GetPreviousReminderMessage(string messageContains)
        {
            // get all messages in reminder channel
            var messages = await _reminderChannel.GetMessagesAsync().FlattenAsync();
            // try to get a pre-existing message matching messageContains (so {eventtitle})
            //return the results
            try
            {
                var reminderMsg = messages.Where(msg => msg.Author.Id == _discord.CurrentUser.Id).First(msg => msg.Content.Contains(messageContains));
                return (IUserMessage) reminderMsg;
            }
            catch
            {
                return null;
            }
        }
    }

    public class CalendarEvents
    {
        public static List<CalendarEvent> Events = new List<CalendarEvent>();
    }

    public class CalendarEvent
    {
        public string Name { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Timezone { get; set; }
        public IUserMessage AlertMessage { get; set; }
    }
}
