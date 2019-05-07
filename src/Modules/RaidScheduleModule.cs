using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Doccer_Bot.Services;

namespace Doccer_Bot.Modules
{
    public class RaidScheduleModule : InteractiveBase
    {
        // Dependency Injection will fill this value in for us 
        public GoogleCalendarSyncService GoogleCalendarSyncService { get; set; }
        public ScheduleService ScheduleService { get; set; }
        public RaidEventsService RaidEventsService { get; set; }

        // resync raid schedule timer
        [Command("resync")]
        [Summary("Realigns the timer to 5m intervals")]
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
            await GoogleCalendarSyncService.InitialSyncEvent(Context);
        }

        // set calendar id
        [Command("calendarid")]
        [Summary("Sets calendar ID to the input")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task CalendarIdSetAsync([Remainder] string input)
        {
            if (input != "")
                await GoogleCalendarSyncService.SetCalendarId(input, Context);
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
            await ScheduleService.HandleReminders();
        }
    }
}
