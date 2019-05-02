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
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Doccer_Bot.Services
{
    public class ScheduleService
    {
        private DiscordSocketClient _discord;
        private IConfiguration _config;
        private IServiceProvider _services;
        private ITextChannel _reminderChannel;

        private RaidEventsService _timeEventsService;
        private TextMemeService _textMemeService;

        public IUserMessage _eventEmbedMessage;

        // DiscordSocketClient, CommandService, and IConfigurationRoot are injected automatically from the IServiceProvider
        public ScheduleService(
            IServiceProvider services,
            DiscordSocketClient discord,
            IConfigurationRoot config)
        {
            _services = services;
            _config = config;
            _discord = discord;
        }

        public async Task InitializeAsync()
        {

            _timeEventsService = _services.GetService<RaidEventsService>();
            _textMemeService = _services.GetService<TextMemeService>();

            // get id of reminders channel from config
            var reminderChannelId = Convert.ToUInt64(_config["reminderChannelId"]);
            _reminderChannel = _discord.GetChannel(reminderChannelId) as ITextChannel;
        }

        public async Task HandleReminders()
        {
            foreach (var calendarEvent in CalendarEvents.Events)
            {
                // get amount of time between the calendarevent start time and the current time
                var timeDelta = calendarEvent.StartDate - TimezoneAdjustedDateTime.Now.Invoke();

                // if it's less than an hour but more than fifteen minutes
                if (timeDelta.TotalHours < 1 && timeDelta.TotalMinutes > 15 && calendarEvent.HourAlertSent == false)
                {
                    calendarEvent.HourAlertSent = true;
                    await _reminderChannel.SendMessageAsync($"{calendarEvent.Name} is starting in {(int)timeDelta.TotalMinutes} minutes.");
                }

                // if it's less than an hour and less or equal to fifteen minutes
                if (timeDelta.TotalHours < 1 && timeDelta.TotalMinutes <= 15 && calendarEvent.EventStartedAlertSent == false)
                {
                    calendarEvent.EventStartedAlertSent = true;
                    await _reminderChannel.SendMessageAsync($"{calendarEvent.Name} is starting shortly. Look for a party finder soon.");
                }
            }
        }

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
            var embed = BuildEventsEmbed();
            await context.Channel.SendMessageAsync(null, false, embed);
        }

        private Embed BuildEventsEmbed()
        {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            // if there are no items in CalendarEvents, build a field stating so
            if (CalendarEvents.Events.Count == 0)
            {
                embedBuilder.AddField("No raids scheduled.", _textMemeService.GetMemeTextForNoEvents().Result);
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
                stringBuilder.Append($"Starts on {calendarEvent.StartDate,0:M/dd} at {calendarEvent.StartDate,0: h:mm tt} {calendarEvent.Timezone} - starts in ");
                // days
                if (timeDelta.Days == 1)
                    stringBuilder.Append($" {timeDelta.Days} day,");
                if (timeDelta.Days > 1)
                    stringBuilder.Append($" {timeDelta.Days} days,");
                // hours
                if (timeDelta.Hours == 1)
                    stringBuilder.Append($" {timeDelta.Hours} hour");
                if (timeDelta.Hours > 1)
                    stringBuilder.Append($" {timeDelta.Hours} hours");
                // and
                if (timeDelta.Days > 0 || timeDelta.Hours > 0 && timeDelta.Minutes > 0)
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
        // if it finds one, set that message as the _eventEmbedMessage
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
        public bool HourAlertSent { get; set; }
        public bool EventStartedAlertSent { get; set; }
    }
}
