using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Doccer_Bot.Services.DatabaseServiceComponents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Doccer_Bot.Services
{
    //
    // This service contains the timer that periodically calls various scheduling functions in GoogleCalendarSyncService & ScheduleService
    //
    public class RaidEventsService
    {
        private readonly DiscordSocketClient _discord;

        private readonly GoogleCalendarSyncService _googleCalendarSyncService;
        private readonly ScheduleService _scheduleService;
        private readonly DatabaseServers _databaseServers;
        private readonly LoggingService _logger;

        private Timer _scheduleTimer; // so garbage collection doesn't eat our timer after a bit
        public TimeSpan _timerInterval = TimeSpan.FromMinutes(5); // how often the timer will run, in minutes

        public RaidEventsService(
            DiscordSocketClient discord,
            GoogleCalendarSyncService googleCalendarSyncService,
            ScheduleService scheduleService,
            DatabaseServers databaseServers,
            LoggingService logger)
        {
            _discord = discord;

            _googleCalendarSyncService = googleCalendarSyncService;
            _scheduleService = scheduleService;
            _databaseServers = databaseServers;
            _logger = logger;
        }

        public async Task Initialize()
        {
            // sets global ServersList variable
            await GetServersInfoFromDatabase();

            foreach (var server in Servers.ServerList)
            {
                //authenticate
                await _googleCalendarSyncService.Login(server);
                // perform initial sync for each server
                await _googleCalendarSyncService.ManualSync(server);
            }
        }

        // perform checks to ensure we can sync, set up intervals and begin the timer that fires resync/scheduling events
        public async Task StartTimer()
        {
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
            _scheduleTimer = new Timer(delegate { Timer_Tick(); }, null, timeUntilNextInterval, intervalMs);
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
        private async void Timer_Tick()
        {
            await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, "Timer ticked."));

            foreach (var server in Servers.ServerList)
            {
                // check if it's possible for us to sync
                var syncStatus = _googleCalendarSyncService.CheckIfSyncPossible(server);

                if (syncStatus == CalendarSyncStatus.OK)
                {
                    // try to sync from calendar
                    _googleCalendarSyncService.SyncFromGoogleCalendar(server);

                    if (server.RemindersEnabled && server.Events.Any())
                        await _scheduleService.HandleReminders(server);

                    // modify events embed in reminders to reflect newly synced values
                    // don't care if syncfromgooglecalendar succeeded or not, because we have placeholder
                    // values for the embed
                    await _scheduleService.SendEvents(server);
                }
                else
                {
                    if (syncStatus == CalendarSyncStatus.ServerUnavailable)
                    {
                        // if the bot detects that a connection error has caused objects to become outdated, we should update them here
                        await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, $"DEBUG: Server {server.ServerName} was detected as disconnected, and we are reassigning its object now."));
                        SetServerDiscordObjects(server);
                    }
                }
            }
        }

        // returns the time-delta between the input DateTime and the next interval in minutes
        // eg if input is 12:04 and interval is 15, the function will return the time delta between 12:04 and 12:15
        private long GetTimeUntilNextInterval(DateTime input, TimeSpan interval)
        {
            var timeOfNext = new DateTime((input.Ticks + interval.Ticks - 1) / interval.Ticks * interval.Ticks);
            var timeUntilNext = Convert.ToInt64(timeOfNext.Subtract(input).TotalMilliseconds);

            return timeUntilNext;
        }
        
        // Updates the ServerList if needed
        public async Task GetServersInfoFromDatabase()
        {
            // get server info from database
            var servers = await _databaseServers.GetServersInfo();

            foreach (var server in servers)
            {
                // if a channel's config or reminder channels are null, we need to set them
                if (server.DiscordServerObject == null || server.ConfigChannel == null || server.ReminderChannel == null)
                {
                    // set this server's discord channel & server refs
                    SetServerDiscordObjects(server);
                    // add this server to the ServerList
                    Servers.ServerList.Add(server);
                }
            }
        }


        // converts stored IDs from database into ulongs (mongo can't store ulong ha ha) and use them to
        // assign our discord objects
        public void SetServerDiscordObjects(DiscordServer server)
        {
            server.DiscordServerObject = _discord.GetGuild(Convert.ToUInt64(server.ServerId));
            server.ConfigChannel = _discord.GetChannel(Convert.ToUInt64(server.ConfigChannelId)) as ITextChannel;
            server.ReminderChannel = _discord.GetChannel(Convert.ToUInt64(server.ReminderChannelId)) as ITextChannel;
        }
    }
}
