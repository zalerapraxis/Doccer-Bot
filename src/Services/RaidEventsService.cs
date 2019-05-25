using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Example;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Doccer_Bot.Services
{
    public class RaidEventsService
    {
        private readonly GoogleCalendarSyncService _googleCalendarSyncService;
        private readonly ScheduleService _scheduleService;
        private readonly LoggingService _logger;

        private Timer _scheduleTimer; // so garbage collection doesn't eat our timer after a bit
        public TimeSpan _timerInterval = TimeSpan.FromMinutes(5); // how often the timer will run, in minutes

        // DiscordSocketClient, CommandService, and IConfigurationRoot are injected automatically from the IServiceProvider
        public RaidEventsService(IServiceProvider services,
            GoogleCalendarSyncService googleCalendarSyncService,
            ScheduleService scheduleService,
            LoggingService logger)
        {
            _googleCalendarSyncService = googleCalendarSyncService;
            _scheduleService = scheduleService;
            _logger = logger;
        }

        // perform checks to ensure we can sync, set up intervals and begin the timer that fires resync/scheduling events
        public async Task StartTimer()
        {
            // check if calendar syncing is possible, and if the calendarevents list is populated
            // hold this function until both conditions are met
            while (true)
            {
                var isSyncPossible = await _googleCalendarSyncService.CheckIfSyncPossible();
                if (isSyncPossible)
                    break;
                await Task.Delay(1000);
            }

            // the amount of time between now and the next interval, in this case the next real-world 15 min interval in the hour
            // this value is used only for the first run of the timer, and is not relevant afterwards. it tells the timer to wait
            // until the next 15m interval before it runs, and effectively lines up the timer to run every 15 minutes of each hour.
            var timeUntilNextInterval = GetTimeUntilNextInterval(TimezoneAdjustedDateTime.Now.Invoke(), _timerInterval);

            // _timerInterval expressed in milliseconds
            var intervalMs = Convert.ToInt32(_timerInterval.TotalMilliseconds);

            // the time of the next scheduled timer execution
            var resultTime = DateTime.Now.AddMilliseconds(timeUntilNextInterval).ToString("HH:mm:ss");

            await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, 
                $"Starting schedule/sync timer now - Waiting {TimeSpan.FromMilliseconds(timeUntilNextInterval).TotalSeconds} seconds - next tick at {resultTime}."));

            // run the timer
            _scheduleTimer = new Timer(delegate { RunTimer(); }, null, timeUntilNextInterval, intervalMs);
        }

        // resync timer to the nearest interval, return string to command method to reply to user
        public async Task<string> ResyncTimer()
        {
            // documentation for these lines is in the StartTimer method
            var timeUntilNextInterval = GetTimeUntilNextInterval(TimezoneAdjustedDateTime.Now.Invoke(), _timerInterval);
            var intervalMs = Convert.ToInt32(_timerInterval.TotalMilliseconds);
            var resultTime = DateTime.Now.AddMilliseconds(timeUntilNextInterval).ToString("HH:mm:ss");

            var message =
                $"Resyncing timer now - waiting {TimeSpan.FromMilliseconds(timeUntilNextInterval).TotalSeconds} seconds - next tick at {resultTime}.";

            await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, message));

            _scheduleTimer.Change(timeUntilNextInterval, intervalMs);

            // pass the message back to the calling method, so that it can inform the user that called the command
            // that we've sent the resync command
            return message;
        }

        // timer executes these functions on each run
        private async void RunTimer()
        {
            await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, "Timer ticked."));
            await _scheduleService.HandleReminders();
            await GoogleCalendarResyncTasks();
        }

        // handle all calendar syncing stuff
        private async Task GoogleCalendarResyncTasks()
        {
            // try to sync from calendar
            _googleCalendarSyncService.SyncFromGoogleCaledar();

            // modify events embed in reminders to reflect newly synced values
            // don't care if syncfromgooglecalendar succeeded or not, because we have placeholder
            // values for the embed
            await _scheduleService.SendEvents();
        }

        private long GetTimeUntilNextInterval(DateTime input, TimeSpan interval)
        {
            // returns the time-delta between the input DateTime and the next interval in minutes
            // eg if input is 12:04 and interval is 15, the function will return the time delta between 12:04 and 12:15
            var timeOfNext = new DateTime((input.Ticks + interval.Ticks - 1) / interval.Ticks * interval.Ticks);
            var timeUntilNext = Convert.ToInt64(timeOfNext.Subtract(input).TotalMilliseconds);

            return timeUntilNext;
        }
    }
}
