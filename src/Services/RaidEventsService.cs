using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Doccer_Bot.Services
{
    public class RaidEventsService
    {
        private IServiceProvider _services;

        private GoogleCalendarSyncService _googleCalendarSyncService;
        private ScheduleService _scheduleService;

        private Timer _scheduleTimer; // so garbage collection doesn't eat our timer after a bit
        public int _timerInterval = 5; // how often the timer will run, in minutes

        // DiscordSocketClient, CommandService, and IConfigurationRoot are injected automatically from the IServiceProvider
        public RaidEventsService(IServiceProvider services)
        {
            _services = services;
        }

        public async Task InitializeAsync()
        {
            _googleCalendarSyncService = _services.GetService<GoogleCalendarSyncService>();
            _scheduleService = _services.GetService<ScheduleService>();
        }


        public async Task StartTimer()
        {
            // check if calendar syncing is possible, and if the calendarevents list is populated
            // hold this function until both conditions are met
            while (true)
            {
                var isSyncPossible = await _googleCalendarSyncService.CheckIfSyncPossible();
                if (!isSyncPossible || CalendarEvents.Events.Count > 0)
                    break;
                await Task.Delay(1000);
            }

            Console.WriteLine("Starting schedule/sync timer now.");

            // _timerInterval expressed in milliseconds
            var intervalMs = Convert.ToInt32(TimeSpan.FromMinutes(_timerInterval).TotalMilliseconds);

            // the amount of time between now and the next interval, in this case the next real-world 15 min interval in the hour
            // this value is used only for the first run of the timer, and is not relevant afterwards. it tells the timer to wait
            // until the next 15m interval before it runs, and effectively lines up the timer to run every 15 minutes of each hour.
            var timeUntilNextInterval = GetTimeUntilNextInterval(TimezoneAdjustedDateTime.Now.Invoke(), _timerInterval);

            // run the timer
            _scheduleTimer = new Timer(delegate { RunTimer(); }, null, timeUntilNextInterval, intervalMs);
        }

        // timer executes these functions on each run
        private async void RunTimer()
        {
            Console.WriteLine($"[{DateTime.Now}] Timer ticked");
            await SchedulingTasks();
            await GoogleCalendarResyncTasks();
        }

        // handle all scheduling stuff
        private async Task SchedulingTasks()
        {
            await _scheduleService.HandleReminders();
        }

        // handle all calendar syncing stuff
        private async Task GoogleCalendarResyncTasks()
        {
            // try to sync from calendar
            var success = _googleCalendarSyncService.SyncFromGoogleCaledar();

            // modify events embed in reminders to reflect newly synced values
            if (success)
                await _scheduleService.SendEvents();
        }

        private long GetTimeUntilNextInterval(DateTime input, int interval)
        {
            // returns the time-delta between the input DateTime and the next interval in minutes
            // eg if input is 12:04 and interval is 15, the function will return the time delta between 12:04 and 12:15
            var timeOfNext = new DateTime(input.Year, input.Month, input.Day, input.Hour, input.Minute, 0).AddMinutes(input.Minute % interval == 0 ? 0 : interval - input.Minute % interval);
            var timeUntilNext = Convert.ToInt64(timeOfNext.Subtract(input).TotalMilliseconds);

            // if the timeUntilNext var is negative, it we're probably on one of the minute intervals, and it'll error things out
            // if we return a negative value for the timer, so just return the interval time in milliseconds
            if (timeUntilNext < 0)
                return (interval * 60 * 1000);

            return timeUntilNext;
        }
    }
}
