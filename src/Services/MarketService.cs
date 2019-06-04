using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Doccer_Bot.Entities;
using Doccer_Bot.Models;
using Flurl.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Doccer_Bot.Services
{
    public class MarketService
    {
        private readonly LoggingService _logger;

        private string _customMarketApiUrl;
        private string _xivapiKey;
        public List<String> ServerList = new List<string>()
        {
            "adamantoise",
            "cactuar",
            "faerie",
            "gilgamesh",
            "jenova",
            "midgardsormr",
            "sargatanas",
            "siren"
        };

        // Set to 1 to (hopefully) force parallel.foreach loops to run one at a time (synchronously)
        // set to -1 for default behavior
        private static ParallelOptions parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = -1 };
        private int exceptionRetryCount = 5; // number of times to retry api requests
        private int exceptionRetryDelay = 1000; // ms

        public MarketService(IConfigurationRoot config, LoggingService logger)
        {
            _logger = logger;

            _customMarketApiUrl = config["custommarketapiurl"];
            _xivapiKey = config["xivapikey"];

            // set this so httpclient can make multiple connections at once
            ServicePointManager.DefaultConnectionLimit = 200;

            // to make the parallel.foreach use more threads
            ThreadPool.SetMinThreads(200, 200);
        }

        // gets list of items, loads them into an ordereddictionary, returns dictionary or null if request failed
        public async Task<dynamic> SearchForItemByName(string searchTerm)
        {
            var apiResponse = await QueryXivapiWithString(searchTerm);

            if (apiResponse.GetType() != typeof(MarketAPIRequestFailureStatus))
            {
                var tempItemList = new OrderedDictionary();

                foreach (var item in apiResponse.Results)
                    tempItemList.Add(item.Name, (int) item.ID);

                return tempItemList;
            }

            return null;
        }

        // take an item id and get the lowest market listings from across all servers, return list of MarketList of MarketItemListings
        public async Task<List<MarketItemListing>> GetMarketListingsFromApi(string itemName, int itemId, string serverFilter = null)
        {   
            List<MarketItemListing> tempMarketList = new List<MarketItemListing>();

            // if calling method provided server via serverOption, use that
            // otherwise, search all servers on Aether
            List<string> tempServerList = new List<string>();
            if (serverFilter != null)
                tempServerList.Add(serverFilter);
            else
                tempServerList.AddRange(ServerList);

            // get all market entries for specified item across all servers, bundle results into tempMarketList
            var tasks = Task.Run(() => Parallel.ForEach(tempServerList, parallelOptions, server =>
            {
                var apiResponse = QueryCustomApiForListings(itemId, server).Result;

                // check if custom API handled error
                if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus))
                {
                    if (apiResponse == MarketAPIRequestFailureStatus.NotLoggedIn)
                        return;
                    if (apiResponse == MarketAPIRequestFailureStatus.UnderMaintenance)
                        return;
                    if (apiResponse == MarketAPIRequestFailureStatus.AccessDenied)
                        return;
                    if (apiResponse == MarketAPIRequestFailureStatus.ServiceUnavailable)
                        return;
                    if (apiResponse == MarketAPIRequestFailureStatus.NoResults)
                        return;
                }

                foreach (var listing in apiResponse.Prices)
                {
                    // build a marketlisting with the info we get from method parameters and the api call
                    var marketListing = new MarketItemListing()
                    {
                        Name = itemName,
                        ItemId = itemId,
                        CurrentPrice = (int)listing.PricePerUnit,
                        Quantity = (int)listing.Quantity,
                        IsHq = (bool)listing.IsHQ,
                        Server = server
                    };

                    tempMarketList.Add(marketListing);
                }
            }));

            await Task.WhenAll(tasks);

            // sort the list by the item price
            tempMarketList = tempMarketList.OrderBy(item => item.CurrentPrice).ToList();

            return tempMarketList;
        }

        // take an item id and get the lowest market listings from across all servers, return list of MarketList of MarketItemListings
        public async Task<List<HistoryItemListing>> GetHistoryListingsFromApi(string itemName, int itemId, string serverFilter = null)
        {
            List<HistoryItemListing> tempHistoryList = new List<HistoryItemListing>();

            // if calling method provided server via serverOption, use that
            // otherwise, search all servers on Aether
            List<string> tempServerList = new List<string>();
            if (serverFilter != null)
                tempServerList.Add(serverFilter);
            else
                tempServerList.AddRange(ServerList);

            // get all market entries for specified item across all servers, bundle results into tempMarketList
            var tasks = Task.Run(() => Parallel.ForEach(tempServerList, parallelOptions, server =>
            {
                var apiResponse = QueryCustomApiForHistory(itemId, server).Result;

                // check if custom API handled error
                if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus))
                {
                    if (apiResponse == MarketAPIRequestFailureStatus.NotLoggedIn)
                        return;
                    if (apiResponse == MarketAPIRequestFailureStatus.UnderMaintenance)
                        return;
                    if (apiResponse == MarketAPIRequestFailureStatus.AccessDenied)
                        return;
                    if (apiResponse == MarketAPIRequestFailureStatus.ServiceUnavailable)
                        return;
                    if (apiResponse == MarketAPIRequestFailureStatus.NoResults)
                        return;
                }

                foreach (var listing in apiResponse.history)
                {
                    // convert companionapi's buyRealData from epoch time (milliseconds) to a normal datetime
                    // set this for converting from epoch time to normal people time
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    var saleDate = epoch.AddMilliseconds(listing.buyRealDate);

                    // build a marketlisting with the info we get from method parameters and the api call
                    var marketListing = new HistoryItemListing()
                    {
                        Name = itemName,
                        ItemId = itemId,
                        SoldPrice = int.Parse(listing.sellPrice),
                        IsHq = (listing.hq == 1),
                        Quantity = (int) listing.stack,
                        SaleDate = saleDate,
                        Server = server
                    };

                    tempHistoryList.Add(marketListing);
                }
            }));

            await Task.WhenAll(tasks);

            // sort the list by the item price
            tempHistoryList = tempHistoryList.OrderByDescending(item => item.SaleDate).ToList();

            return tempHistoryList;
        }

        // get the status of the Companion API
        public async Task<MarketAPIRequestFailureStatus> GetCompanionApiStatus()
        {
            // run test query
            var apiResponse = QueryCustomApiForListings(5, "gilgamesh").Result;

            // if apiresponse does not return a status type, then it should be running fine
            if (apiResponse.GetType() != typeof(MarketAPIRequestFailureStatus))
                return MarketAPIRequestFailureStatus.OK;

            // if it does return a status type, pass that back to calling function
            return apiResponse;
        }

        // get data from API using item ID - this is how we retrieve prices from the Companion API
        private async Task<dynamic> QueryCustomApiForListings(int itemId, string server)
        { 
            // number of retries
            var i = 0;

            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await $"{_customMarketApiUrl}/market/?id={itemId}&server={server}".GetJsonAsync();

                    // check if custom API handled error
                    if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus))
                    {
                        if (apiResponse == "Not logged in")
                            return MarketAPIRequestFailureStatus.NotLoggedIn;
                        if (apiResponse == "Under maintenance")
                            return MarketAPIRequestFailureStatus.UnderMaintenance;
                        if (apiResponse == "Access denied")
                            return MarketAPIRequestFailureStatus.AccessDenied;
                        if (apiResponse == "Service unavailable")
                            return MarketAPIRequestFailureStatus.ServiceUnavailable;
                    }
                    
                    // check if results are null or empty
                    if (apiResponse.Prices == null || apiResponse.Prices.Count == 0)
                        return MarketAPIRequestFailureStatus.NoResults;

                    // otherwise, return what we got
                    return apiResponse;
                }
                catch (FlurlHttpException exception)
                {
                    await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, $"{exception.Message}"));
                    await Task.Delay(exceptionRetryDelay);
                }
                i++;
            }

            // return generic api failure code
            return MarketAPIRequestFailureStatus.APIFailure;
        }

        // get history listings data from API using item id - this is how we retrieve sales history from the Companion API
        private async Task<dynamic> QueryCustomApiForHistory(int itemId, string server)
        {   
            // number of retries
            var i = 0;

            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await $"{_customMarketApiUrl}/market/history.php?id={itemId}&server={server}".GetJsonAsync();

                    // check if custom API handled error
                    if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus))
                    {
                        if (apiResponse == "Not logged in")
                            return MarketAPIRequestFailureStatus.NotLoggedIn;
                        if (apiResponse == "Under maintenance")
                            return MarketAPIRequestFailureStatus.UnderMaintenance;
                        if (apiResponse == "Access denied")
                            return MarketAPIRequestFailureStatus.AccessDenied;
                        if (apiResponse == "Service unavailable")
                            return MarketAPIRequestFailureStatus.ServiceUnavailable;
                    }

                    // check if results are null or empty
                    if (apiResponse.history == null || apiResponse.history.Count == 0)
                        return MarketAPIRequestFailureStatus.NoResults;

                    // otherwise, return what we got
                    return apiResponse;
                }
                catch (FlurlHttpException exception)
                {
                    await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, $"{exception.Message}"));
                    await Task.Delay(exceptionRetryDelay);
                }
                i++;
            }

            // return generic api failure code
            return MarketAPIRequestFailureStatus.APIFailure;
        }

        // get data from XIVAPI using item name - generally used to get an item's ID
        private async Task<dynamic> QueryXivapiWithString(string itemName)
        { 
            // number of retries
            var i = 0;

            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await $"https://xivapi.com/search?string={itemName}&indexes=Item&private_key={_xivapiKey}".GetJsonAsync();

                    // check if no results
                    if (apiResponse.Results == null || apiResponse.Results.Count == 0)
                        return MarketAPIRequestFailureStatus.NoResults;

                    // otherwise, return what we got
                    return apiResponse;
                }
                catch (FlurlHttpException exception)
                {
                    await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, $"{exception.Message}"));
                    await Task.Delay(exceptionRetryDelay);
                }
                i++;
            }

            // return generic api failure code
            return MarketAPIRequestFailureStatus.APIFailure;
        }

        // get data from XIVAPI using item ID - generally used to get an item's name
        public async Task<dynamic> QueryXivapiWithItemId(int itemId)
        { 
            var i = 0;
            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await $"https://xivapi.com/item/{itemId}".GetJsonAsync();

                    // check if no results
                    if (apiResponse.ToString().Contains("Game Data does not exist"))
                        return MarketAPIRequestFailureStatus.NoResults;

                    // otherwise, return what we got
                    return apiResponse;
                }
                catch (FlurlHttpException exception)
                {
                    await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, $"{exception.Message}"));
                    await Task.Delay(exceptionRetryDelay);
                }
                i++;
            }

            // return generic api failure code
            return MarketAPIRequestFailureStatus.APIFailure;
        }
    }
}
