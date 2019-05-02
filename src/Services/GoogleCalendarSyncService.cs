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
    public class GoogleCalendarSyncService
    {
        private readonly DiscordSocketClient _discord;
        private readonly IConfiguration _config;
        private readonly ScheduleService _scheduleService;
        private readonly InteractiveService _interactiveService;
        private readonly LoggingService _logger;

        private ITextChannel _configChannel;
        private UserCredential _credential;

        private string _filePath = "client_id.json"; // API Console -> OAuth 2.0 client IDs -> entry -> download button
        private string _credentialPath = "token";

        private string[] _scopes = new string[] { CalendarService.Scope.CalendarReadonly };
        private string _redirectUri = "http://localhost";
        private string _userId = "user";
        private string _calendarId; //"9aqhstjsuqq9s12sf9ij01rvi0@group.calendar.google.com"; - assigned via config file

        // DiscordSocketClient, CommandService, and IConfigurationRoot are injected automatically from the IServiceProvider
        public GoogleCalendarSyncService(
            DiscordSocketClient discord,
            IConfigurationRoot config,
            InteractiveService interactiveService,
            ScheduleService scheduleService,
            LoggingService logger)
        {
            _config = config;
            _discord = discord;

            _interactiveService = interactiveService;
            _scheduleService = scheduleService;
            _logger = logger;
        }

        public async Task Initialize()
        {
            // get calendar id from config
            _calendarId = _config["calendarId"];

            // get id of configuration channel from config
            var configChannelId = Convert.ToUInt64(_config["configChannelId"]);
            _configChannel = _discord.GetChannel(configChannelId) as ITextChannel;

            // authenticate
            await Login();
        }

        public async Task Login(SocketCommandContext context = null)
        {
            using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
            {
                // build code flow manager to authenticate token
                var flowManager = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = GoogleClientSecrets.Load(stream).Secrets,
                    Scopes = _scopes,
                    DataStore = new FileDataStore(_credentialPath, true)
                });

                var fileDataStore = new FileDataStore(_credentialPath, true);
                var token = await fileDataStore.GetAsync<TokenResponse>("token");

                // load token from file 
                // var token = await flowManager.LoadTokenAsync(_userId, CancellationToken.None).ConfigureAwait(false);

                // check if we need to get a new token
                if (flowManager.ShouldForceTokenRetrieval() || token == null ||
                    token.RefreshToken == null && token.IsExpired(flowManager.Clock))
                {
                    await _configChannel.SendMessageAsync("Google Auth credentials missing or expired. Use ```.auth``` to authenticate.");
                    return;
                }

                // set credentials to use for syncing
                _credential = new UserCredential(flowManager, _userId, token);
            }
        }

        // called whenever .sync command is used, and at first program launch
        public async Task InitialSyncEvent(SocketCommandContext context = null)
        {
            // if context = null, we're running at launch
            // if context != null, we're running it via command

            // check if we're authenticated and have a calendar id to sync from
            var isSyncPossible = await CheckIfSyncPossible(context);
            if (!isSyncPossible)
                return; // this function handles informing the user - no need to do it here

            // perform the actual sync
            var success = SyncFromGoogleCaledar(context);

            // handle sync success or failure
            if (success)
            {
                // send message reporting we've synced calendar events
                string resultMessage = $":calendar: Synced {CalendarEvents.Events.Count} calendar events.";
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
            await _scheduleService.SendEvents();
        }

        // logic for pulling data from api and adding it to CalendarEvents list, returns bool representing
        // if calendar had events or not
        public bool SyncFromGoogleCaledar(SocketCommandContext context = null)
        {
            // Set the timespan of events to sync
            var min = TimezoneAdjustedDateTime.Now.Invoke();
            var max = TimezoneAdjustedDateTime.Now.Invoke().AddMonths(1);

            // pull events from the specified google calendar
            // string is the calendar id of the calendar to sync with
            var events = GetCalendarEvents(_credential, _calendarId, min, max);

            // declare events to use for list comparisons
            List<CalendarEvent> oldEventsList = new List<CalendarEvent>();
            List<CalendarEvent> newEventsList = new List<CalendarEvent>();

            oldEventsList.AddRange(CalendarEvents.Events);
            CalendarEvents.Events.Clear();

            // if there are events, iterate through and add them to our calendarevents list
            if ((events?.Count() ?? 0) > 0)
            {

                foreach (var eventItem in events)
                {
                    // api wrapper will always pull times in local time aka eastern
                    // so just subtract 3 hours to get pacific time
                    eventItem.Start.DateTime = eventItem.Start.DateTime - TimeSpan.FromHours(3);
                    eventItem.End.DateTime = eventItem.End.DateTime - TimeSpan.FromHours(3);

                    // don't add items from the past
                    if (eventItem.Start.DateTime < TimezoneAdjustedDateTime.Now.Invoke())
                        continue;

                    // build calendar event to be added to our list
                    var calendarEvent = new CalendarEvent()
                    {
                        Name = eventItem.Summary,
                        StartDate = eventItem.Start.DateTime.Value,
                        EndDate = eventItem.End.DateTime.Value,
                        Timezone = "PST"
                    };

                    newEventsList.Add(calendarEvent);
                }

                // overall purpose of this is to keep the alert flags between resyncs

                if (oldEventsList.Count == 0)
                {
                    // if calendarevents list (and thus oldeventslist) is empty, we're running for the first time
                    // so just add newEventsList to calendarevents and be done
                    CalendarEvents.Events.AddRange(newEventsList);
                }
                else
                {
                    // match events we just pulled from google to events we have stored already, by name
                    // store new name (this doesn't matter), start and endgames from new list into CalendarEvents
                    // keep existing alert flags
                    var oldEventsDict = oldEventsList.ToDictionary(n => n.Name);
                    foreach (var n in newEventsList)
                    {
                        CalendarEvent o;
                        if (oldEventsDict.TryGetValue(n.Name, out o))
                            CalendarEvents.Events.Add(new CalendarEvent
                            {
                                Name = n.Name,
                                StartDate = n.StartDate,
                                EndDate = n.EndDate,
                                Timezone = o.Timezone,
                                HourAlertSent = o.HourAlertSent,
                                EventStartedAlertSent = o.EventStartedAlertSent
                            });
                        else
                            CalendarEvents.Events.Add(n);
                    }
                }
                return true; // calendar had events, and we added them
            }
            return false; // calendar did not have events
        }

        // check if we're authorized and if we have a calendar id, and prompt the user to set up either if needed
        // returns true if we're authorized and have a calendar id, returns false if either checks are false
        public async Task<bool> CheckIfSyncPossible(SocketCommandContext context = null)
        {
            // check if we have credentials for google api
            if (_credential == null)
            {
                string resultMessage = "We're missing Google auth credentials. Use ```.auth``` to set them up.";

                if (context != null)
                    await context.Channel.SendMessageAsync(resultMessage);
                else
                    //await _configChannel.SendMessageAsync(resultMessage);
                    return false;
            }

            // check if we know what calendar we're syncing with
            if (_calendarId == "")
            {
                string resultMessage = "We don't have a calendar set to sync from. Run the command ```.calendar xxxxxxxxxxxxxxxxxxxxxxxxxx@group.calendar.google.com``` to set one." +
                                       $"{Environment.NewLine} You can find the Calendar ID in your raid calendar's settings, near the bottom.";
                if (context != null)
                    await context.Channel.SendMessageAsync(resultMessage);
                return false;
            }

            return true;
        }

        public async Task SetCalendarId(string id, SocketCommandContext context)
        {
            _calendarId = id;
            _config["calendarId"] = id;

            string resultMessage =
                ":white_check_mark: Calendar ID set. You can use ```.sync``` to sync up your calendar now.";
            await _interactiveService.ReplyAndDeleteAsync(context, resultMessage, false, null, TimeSpan.FromSeconds(10));
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

        // called by auth command - receive auth code, exchange it for token, log in
        public async Task GetTokenAndLogin(string userInput, SocketCommandContext context)
        {
            // split auth code from the url that the user passes to this function
            var authCode = userInput.Split('=', '&')[1];

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
                var fileDataStore = new FileDataStore(_credentialPath, true);
                await fileDataStore.StoreAsync("token", token);
            }

            string resultMessage =
                ":white_check_mark: Authorization successful, logging in. You should be able to use ```.sync``` to manually sync your calendar now.";
            await _interactiveService.ReplyAndDeleteAsync(context, resultMessage, false, null,
                TimeSpan.FromSeconds(10));

            // log in using our new token
            await Login();
        }

        private IEnumerable<Google.Apis.Calendar.v3.Data.Event> GetCalendarEvents(ICredential credential, string calendarId, DateTime min, DateTime max, int maxResults = 10)
        {
            var service = new CalendarService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Doccer Bot GCal sync",
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
        // we are working in eastern time and we want pacific time, but we want to have easy access
        // to the current time (in pacific) - so return the current time minus three hours
        public static Func<DateTime> Now = () => DateTime.Now - TimeSpan.FromHours(3);
    }
}
