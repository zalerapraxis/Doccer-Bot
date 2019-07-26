using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Doccer_Bot.Modules.Common;
using Doccer_Bot.Services;
using Doccer_Bot.Services.DatabaseServiceComponents;

namespace Doccer_Bot.Modules
{
    [Name("RaidSchedule")]
    [Remarks("Configure & control raid scheduling")]
    public class RaidScheduleModule : InteractiveBase
    {
        // Dependency Injection will fill this value in for us 
        public GoogleCalendarSyncService GoogleCalendarSyncService { get; set; }
        public ScheduleService ScheduleService { get; set; }
        public RaidEventsService RaidEventsService { get; set; }
        public DatabaseServers DatabaseServers { get; set; }


        [Command("adjust")]
        [Summary("Manually adjusts start/end times for the next upcoming event")]
        [Example("adjust {start/end} {value}")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task AdjustEventAsync(string function, int value = 0)
        {
            if (function != "start" && function != "end" && function != "clear" || function != "clear" && value == 0)
            {
                await ReplyAsync("You didn't correctly fill out the command. Syntax is `adjust start/end amount (in minutes)`");
                return;
            }

            var result = GoogleCalendarSyncService.AdjustUpcomingEvent(function, value, Context);

            if (result == true)
            {
                var server = Servers.ServerList.Find(x => x.DiscordServer == Context.Guild);

                StringBuilder responseBuilder = new StringBuilder();
                responseBuilder.Append($"Event {server.Events[0].Name} adjusted - ");

                if (function == "start")
                    responseBuilder.Append($" new start time: {server.Events[0].StartDate}");
                if (function == "end")
                    responseBuilder.Append($" new end time: {server.Events[0].EndDate}");
                if (function == "clear")
                    responseBuilder.Append("adjustment cleared. Use `sync` command to reload original values.");

                await ReplyAsync(responseBuilder.ToString());
            }
            if (result == false)
                await ReplyAsync("Adjust failed - you probably tried to make the event start after it was set to end or something.");
            if (result == null)
                await ReplyAsync("Adjust failed - this server doesn't have any events to adjust.");

        }


        // resync raid schedule timer
        [Command("resync")]
        [Summary("Realigns the timer to nearest time interval")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ScheduleTimerResyncAsync()
        {
            var response = await RaidEventsService.ResyncTimer();
            await ReplyAsync(response);
        }

        // force sync calendar
        [Command("sync", RunMode = RunMode.Async)]
        [Summary("Force a calendar resync")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task CalendarSyncAsync()
        {
            await GoogleCalendarSyncService.ManualSync(null, Context);
        }

        // set calendar id
        [Command("calendarid")]
        [Summary("Sets calendar ID to the input")]
        [Example("calendarid {id}")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task CalendarIdSetAsync([Remainder] string input)
        {
            if (input != "")
            {
                await GoogleCalendarSyncService.SetCalendarId(input, Context);
                await ReplyAndDeleteAsync(
                    ":white_check_mark: Calendar ID set. You can use ```.sync``` to sync up your calendar now.", false, null, TimeSpan.FromMinutes(1));
            }
            else
                await ReplyAsync("You need to provide a calendar ID after the command.");
        }

        [Command("configure")]
        [Alias("config")]
        [Summary("Configures the bot for your server")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ConfigureServerAsync()
        {
            ulong configChannelId;
            ulong reminderChannelId;

            // config channel
            await ReplyAndDeleteAsync($"Tag the channel you want **configuration** messages sent to (for example, {MentionUtils.MentionChannel(Context.Channel.Id)}).", false, null, TimeSpan.FromMinutes(1));
            var response = await NextMessageAsync(true, true, TimeSpan.FromSeconds(30));
            if (response != null)
                if (response.MentionedChannels.FirstOrDefault() != null)
                    configChannelId = MentionUtils.ParseChannel(response.Content);
                else
                {
                    await ReplyAsync("You didn't correctly tag a channel. Follow the instructions, dingus.");
                    return;
                }
            else
            {
                await ReplyAsync("I didn't get a response in time. Try again.");
                return;
            }
                

            // reminder channel
            await ReplyAndDeleteAsync($"Tag the channel you want **reminders & the schedule** sent to (for example, {MentionUtils.MentionChannel(Context.Channel.Id)}).", false, null, TimeSpan.FromMinutes(1));
            response = await NextMessageAsync(true, true, TimeSpan.FromSeconds(30));
            if (response != null)
                if (response.MentionedChannels.FirstOrDefault() != null)
                    reminderChannelId = MentionUtils.ParseChannel(response.Content);
                else
                {
                    await ReplyAsync("You didn't correctly tag a channel. Follow the instructions, dingus.");
                    return;
                }
            else
            {
                await ReplyAsync("I didn't get a response in time. Try again.");
                return;
            }

            // build our new server object
            var newServer = new Server()
            {
                ConfigChannelId = configChannelId.ToString(),
                ReminderChannelId = reminderChannelId.ToString(),
                ServerId = Context.Guild.Id.ToString(),
                ServerName = Context.Guild.Name,
                RemindersEnabled = true
            };

            // add this server's data to the database
            await DatabaseServers.AddServerInfo(newServer);

            // initialize this server
            RaidEventsService.SetServerDiscordObjects(newServer);

            // update the ServerList with the new server
            Servers.ServerList.Add(newServer);

            // set up google api authentication
            await AuthAsync();
        }

        // set up google auth
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Sets up authentication with Google API")]
        [Command("auth", RunMode = RunMode.Async)]
        public async Task AuthAsync()
        {
            await GoogleCalendarSyncService.GetAuthCode(Context);
            var response = await NextMessageAsync(true, true, TimeSpan.FromSeconds(60));
            if (response != null)
                await GoogleCalendarSyncService.GetTokenAndLogin(response.Content, Context);
            else
                await ReplyAsync("I didn't get a response in time. Try again.");
        }

        // manually display upcoming events
        [Command("events")]
        [Summary("Manually display upcoming events")]
        public async Task CalendarEventsAsync()
        {
            var server = Servers.ServerList.Find(x => x.DiscordServer == Context.Guild);
            if (server != null)
                await ScheduleService.GetEvents(Context);
            else
                await ReplyAsync("This server doesn't have the scheduling system set up, so there are no events to display.");
        }

        // enable or disable reminders on this server
        [Command("reminders")]
        [Summary("Toggle reminders for this server")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ToggleRemindersAsync()
        {
            var server = Servers.ServerList.Find(x => x.DiscordServer == Context.Guild);

            if (server.RemindersEnabled)
                server.RemindersEnabled = false;
            else if (server.RemindersEnabled == false)
                server.RemindersEnabled = true;

            await ReplyAsync($"Toggled reminders to: {server.RemindersEnabled}.");

            await DatabaseServers.EditServerInfo(server.ServerId, "reminders_enabled", server.RemindersEnabled);
        }
    }
}
