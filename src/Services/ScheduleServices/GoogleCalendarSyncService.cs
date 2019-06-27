using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;
using Doccer_Bot.Models;
using Doccer_Bot.Services.DatabaseServiceComponents;
using Example;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Auth.OAuth2.Web;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Doccer_Bot.Services
{
    //
    // This service handles communicating with Google Calendar API, getting events, and building lists from them
    //
    public class GoogleCalendarSyncService
    {
        private readonly IConfiguration _config;
        private readonly ScheduleService _scheduleService;
        private readonly DatabaseServers _databaseServers;
        private readonly InteractiveService _interactiveService;
        private readonly LoggingService _logger;

        private string _filePath = "client_id.json"; // API Console -> OAuth 2.0 client IDs -> entry -> download button
        private string _credentialPathPrefix = "tokens";

        private string[] _scopes = { CalendarService.Scope.CalendarReadonly };
        private string _redirectUri = "http://localhost";
        private string _userId = "user"; // dummy username for google stuff

        public GoogleCalendarSyncService(            
            IConfigurationRoot config,
            InteractiveService interactiveService,
            ScheduleService scheduleService,
            DatabaseServers datbaseServers,
            LoggingService logger)
        {
            _config = config;

            _interactiveService = interactiveService;
            _scheduleService = scheduleService;
            _databaseServers = datbaseServers;
            _logger = logger;
        }

        // log in to all servers
        public async Task Login(Server server)
        {
            var credentialPath = $@"{_credentialPathPrefix}/{server.ServerId}";

            using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
            {
                // build code flow manager to authenticate token
                var flowManager = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = GoogleClientSecrets.Load(stream).Secrets,
                    Scopes = _scopes,
                    DataStore = new FileDataStore(credentialPath, true)
                });

                var fileDataStore = new FileDataStore(credentialPath, true);
                var token = await fileDataStore.GetAsync<TokenResponse>("token");

                // load token from file 
                // var token = await flowManager.LoadTokenAsync(_userId, CancellationToken.None).ConfigureAwait(false);

                // check if we need to get a new token
                if (flowManager.ShouldForceTokenRetrieval() || token == null ||
                    token.RefreshToken == null && token.IsExpired(flowManager.Clock))
                {
                    return;
                }

                // set credentials to use for syncing
                server.GoogleUserCredential = new UserCredential(flowManager, _userId, token);
            }
        }

        // called whenever .sync command is used, and at first program launch
        public async Task<bool> ManualSync(Server server = null, SocketCommandContext context = null)
        {
            // if server is null, context is not null - we're calling via command, so get the right server via context
            if (server == null && context != null)
                server = Servers.ServerList.Find(x => x.DiscordServer == context.Guild);

            // check if we're authenticated and have a calendar id to sync from
            var syncStatus = CheckIfSyncPossible(server);
            if (syncStatus != CalendarSyncStatus.OK)
            {
                if (server == null && context != null)
                    await context.Channel.SendMessageAsync($"Sync failed: {SyncFailedReason(syncStatus)}");
                else
                    await server.ConfigChannel.SendMessageAsync($"Sync failed: {SyncFailedReason(syncStatus)}");

                return false;
            }
                
            // perform the actual sync
            var success = SyncFromGoogleCalendar(server);

            // handle sync success or failure
            if (success)
            {
                // send message reporting we've synced calendar events
                string resultMessage = $":calendar: Synced {server.Events.Count} calendar events.";
                if (context != null) // we only want to send a message announcing sync success if the user sent the command
                {
                    await _interactiveService.ReplyAndDeleteAsync(context, resultMessage);
                }
            }
            else
            {
                // send message reporting there were no calendar events to sync
                string resultMessage = ":calendar: No events found in calendar.";
                if (context != null)
                {
                    await _interactiveService.ReplyAndDeleteAsync(context, resultMessage);
                }

            }

            // send/modify events embed in reminders to reflect newly synced values
            await _scheduleService.SendEvents(server);

            return true;
        }

        // logic for pulling data from api and adding it to CalendarEvents list, returns bool representing
        // if calendar had events or not
        public bool SyncFromGoogleCalendar(Server server)
        {
            // Set the timespan of events to sync
            var min = TimezoneAdjustedDateTime.Now.Invoke();
            var max = TimezoneAdjustedDateTime.Now.Invoke().AddMonths(1);

            // pull events from the specified google calendar
            // string is the calendar id of the calendar to sync with
            var events = GetCalendarEvents(server.GoogleUserCredential, server.CalendarId, min, max);

            // declare events to use for list comparisons
            List<CalendarEvent> oldEventsList = new List<CalendarEvent>();
            List<CalendarEvent> newEventsList = new List<CalendarEvent>();

            oldEventsList.AddRange(server.Events);
            server.Events.Clear();

            // if there are events, iterate through and add them to our calendarevents list
            if (events.Any())
            {
                // build a list of the events we pulled from gcal
                foreach (var eventItem in events)
                {
                    // api wrapper will always pull times in local time aka eastern because it sucks
                    // so just subtract 3 hours to get pacific time
                    eventItem.Start.DateTime = eventItem.Start.DateTime - TimeSpan.FromHours(3);
                    eventItem.End.DateTime = eventItem.End.DateTime - TimeSpan.FromHours(3);

                    // don't add items from the past
                    if (eventItem.End.DateTime < TimezoneAdjustedDateTime.Now.Invoke())
                        continue;

                    // build calendar event to be added to our list
                    var calendarEvent = new CalendarEvent()
                    {
                        Name = eventItem.Summary,
                        StartDate = eventItem.Start.DateTime.Value,
                        EndDate = eventItem.End.DateTime.Value,
                        Timezone = "PST",
                        UniqueId = eventItem.CreatedRaw
                    };

                    newEventsList.Add(calendarEvent);
                }

                // build our working list of calendarevents, mixing old event items (if any) and new ones
                if (oldEventsList.Count == 0)
                {
                    // if calendarevents list (and thus oldeventslist) is empty, we're running for the first time
                    // so just add newEventsList to calendarevents and be done
                    server.Events.AddRange(newEventsList);
                }
                else
                {
                    // match events we just pulled from google to events we have stored already, by start date
                    // store new name (this doesn't matter), start and endgames from new list into CalendarEvents
                    // keep existing alert flags
                    var oldEventsDict = oldEventsList.ToDictionary(n => n.UniqueId);
                    foreach (var n in newEventsList)
                    {
                        CalendarEvent o;
                        if (oldEventsDict.TryGetValue(n.UniqueId, out o))
                        {
                            var calendarEvent = new CalendarEvent();
                            calendarEvent.Name = n.Name;
                            calendarEvent.Timezone = o.Timezone;
                            calendarEvent.AlertMessage = o.AlertMessage;
                            calendarEvent.UniqueId = o.UniqueId;

                            // if this event's been manually adjusted, keep the old values
                            if (o.ManuallyAdjusted)
                            {
                                calendarEvent.StartDate = o.StartDate;
                                calendarEvent.EndDate = o.EndDate;
                            }
                            else // else accept the new values
                            {
                                calendarEvent.StartDate = n.StartDate;
                                calendarEvent.EndDate = n.EndDate;
                            }

                            server.Events.Add(calendarEvent);
                        }
                            
                        else
                            server.Events.Add(n);
                    }
                }
                return true; // calendar had events, and we added them
            }
            return false; // calendar did not have events
        }

        public bool? AdjustUpcomingEvent(string function, int value, SocketCommandContext context)
        {
            var server = Servers.ServerList.Find(x => x.DiscordServer == context.Guild);

            // if no events, return null
            if (!server.Events.Any())
                return null;

            if (function == "clear")
            {
                server.Events[0].ManuallyAdjusted = false;
                return true;
            }

            if (function == "start")
            {
                // if adjusted start date would be later than the end date, return false
                if (server.Events[0].StartDate.AddMinutes(value) >= server.Events[0].EndDate)
                    return false;

                server.Events[0].StartDate = server.Events[0].StartDate.AddMinutes(value);
            }

            if (function == "end")
            {
                // if adjusted end date would be sooner than the start date, return false
                if (server.Events[0].EndDate.AddMinutes(value) <= server.Events[0].StartDate)
                    return false;

                server.Events[0].EndDate = server.Events[0].EndDate.AddMinutes(value);
            }

            server.Events[0].ManuallyAdjusted = true;
            return true;
        }

        // check if we're authorized and if we have a calendar id, and prompt the user to set up either if needed
        // returns true if we're authorized and have a calendar id, returns false if either checks are false
        public CalendarSyncStatus CheckIfSyncPossible(Server server)
        {
            // check if we have credentials for google apiitem
            if (server.GoogleUserCredential == null)
                return CalendarSyncStatus.NullCredentials;

            if (server.CalendarId == "")
                return CalendarSyncStatus.NullCalendarId;

            if (server.CalendarId == null)
                return CalendarSyncStatus.EmptyCalendarId;

            // if server object is assigned, the bot is connected, but the bot is not connected to this server, we're probably kicked
            if (server.DiscordServer != null && server.DiscordServer.Available &&
                ((SocketGuild) server.DiscordServer).IsConnected == false)
            {
                // DEBUG
                Task.Run((async () =>
                {
                    await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name,
                        $"DEBUG - Name: {server.DiscordServer.Name} - Available: {server.DiscordServer.Available} " +
                        $"Connected: {((SocketGuild) server.DiscordServer).IsConnected}"));
                }));
                return CalendarSyncStatus.ServerUnavailable;
            }
                

            return CalendarSyncStatus.OK;
        }

        public string SyncFailedReason(CalendarSyncStatus status)
        {
            switch (status)
            {
                case CalendarSyncStatus.NullCredentials:
                    return "Google Auth credentials are missing. Use ```.auth``` to authenticate.";
                case CalendarSyncStatus.NullCalendarId:
                    return "Calendar ID is missing. Use ```.calendarid {calendarid} to set it.";
                case CalendarSyncStatus.EmptyCalendarId:
                    return "Calendar ID is missing. Use ```.calendarid {calendarid} to set it.";
                case CalendarSyncStatus.ServerUnavailable:
                    return "Bot is not a member of this server.";
            }

            return "Uncaught sync failure reason.";
        }

        public async Task SetCalendarId(string calendarId, SocketCommandContext context)
        {
            // grab server by id of current guild via context
            var server = Servers.ServerList.Find(x => x.DiscordServer == context.Guild);

            // and its index in the ServerList so we can assign to the ServerList directly
            var serverIndex = Servers.ServerList.IndexOf(server);
            Servers.ServerList[serverIndex].CalendarId = calendarId;

            // and update the database as well
            await _databaseServers.EditServerInfo(server.ServerId, "calendar_id", calendarId);
        }

        // called by .auth command - build auth code & send to user
        public async Task GetAuthCode(SocketCommandContext context)
        {
            // build authentication url and send it to user
            using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
            {
                // build code flow manager to get auth url
                var flowManager = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = GoogleClientSecrets.Load(stream).Secrets,
                    Scopes = _scopes,
                });

                // build auth url
                var request = flowManager.CreateAuthorizationCodeRequest(_redirectUri);
                var url = request.Build();

                // put together a response message to give user instructions on what to do
                StringBuilder sb = new StringBuilder();
                sb.Append("Authorize your Google account using the following link. When you've finished following the instructions " +
                          "and you're given a connection error, copy the URL from your browser and paste it here.");
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine($"{url.AbsoluteUri}");

                await _interactiveService.ReplyAndDeleteAsync(context, sb.ToString(), false, null,
                    TimeSpan.FromMinutes(1));
            }
        }

        // called by .auth command - receive auth code, exchange it for token, log in
        public async Task GetTokenAndLogin(string userInput, SocketCommandContext context)
        {
            // split auth code from the url that the user passes to this function
            var authCode = userInput.Split('=', '&')[1];

            // grab server by id of current guild via context
            var server = Servers.ServerList.Find(x => x.DiscordServer == context.Guild);

            var credentialPath = $@"{_credentialPathPrefix}/{server.ServerId}";

            // build code flow manager to get token
            using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
            {
                var flowManager = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = GoogleClientSecrets.Load(stream).Secrets,
                    Scopes = _scopes,
                });

                // retrieve token using authentication url
                var token = flowManager.ExchangeCodeForTokenAsync(_userId, authCode, _redirectUri, CancellationToken.None).Result;

                // save user credentials
                var fileDataStore = new FileDataStore(credentialPath, true);
                await fileDataStore.StoreAsync("token", token);
            }

            string resultMessage =
                ":white_check_mark: Authorization successful. Get your calendar ID and run ```.calendarid {id}``` to finish setup." +
                "Your calendar ID can be found by going to your raid calendar's settings, and scrolling to the \"Integrate Calendar\" section.";
            await _interactiveService.ReplyAndDeleteAsync(context, resultMessage, false, null,
                TimeSpan.FromMinutes(1));

            // log in using our new token
            await Login(server);
        }

        private IEnumerable<Google.Apis.Calendar.v3.Data.Event> GetCalendarEvents(ICredential credential, string calendarId, DateTime min, DateTime max, int maxResults = 10)
        {
            var service = new CalendarService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Doccer Bot calendar sync",
            });

            EventsResource.ListRequest request = service.Events.List(calendarId);

            request.TimeMin = min;
            request.TimeMax = max;
            request.ShowDeleted = false;
            request.SingleEvents = true;
            request.MaxResults = maxResults;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            var events = request.Execute()?.Items;

            return events;
        }
    }

    public static class TimezoneAdjustedDateTime
    {
        // the bot runs in eastern time and we want pacific time, and we want to have easy access
        // to the current time (in pacific) - so return the current time minus three hours
        public static Func<DateTime> Now = () => DateTime.Now - TimeSpan.FromHours(3);
    }
}
