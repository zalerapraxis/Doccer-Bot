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

namespace Doccer_Bot.Modules
{
    [Name("RaidSchedule")]
    public class RaidScheduleModule : InteractiveBase
    {
        // Dependency Injection will fill this value in for us 
        public GoogleCalendarSyncService GoogleCalendarSyncService { get; set; }
        public ScheduleService ScheduleService { get; set; }
        public RaidEventsService RaidEventsService { get; set; }
        public DatabaseService DatabaseService { get; set; }

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
            var response = await NextMessageAsync(true, true, TimeSpan.FromSeconds(10));
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
            response = await NextMessageAsync(true, true, TimeSpan.FromSeconds(10));
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
                ServerName = Context.Guild.Name
            };

            // add this server's data to the database
            await DatabaseService.AddServerInfo(newServer);

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
            var response = await NextMessageAsync(true, true, TimeSpan.FromSeconds(30));
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
            await ScheduleService.GetEvents(Context);
        }

        // manually send event reminders
        [Command("remind")]
        [Summary("Manually display reminders")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SendEventRemindersAsync()
        {
            var server = Servers.ServerList.Find(x => x.DiscordServer == Context.Guild);
            await ScheduleService.HandleReminders(server);
        }
    }
}
