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
using MongoDB.Bson.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonConvert = Newtonsoft.Json.JsonConvert;

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
        public async Task<List<ItemSearchResultModel>> SearchForItemByName(string searchTerm)
        {
            var apiResponse = await QueryXivapiWithString(searchTerm);

            var tempItemList = new List<ItemSearchResultModel>();

            // if api failure, return empty list
            if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus) &&
                apiResponse == MarketAPIRequestFailureStatus.APIFailure)
                return tempItemList;

            // if no results, return empty list
            if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus) &&
                apiResponse == MarketAPIRequestFailureStatus.NoResults)
                return tempItemList;

            foreach (var item in apiResponse.Results)
                tempItemList.Add(new ItemSearchResultModel()
                {
                    Name = item.Name,
                    ID = (int)item.ID
                });

            return tempItemList;
        }

        // take an item id and get the lowest market listings from across all servers, return list of MarketList of MarketItemListings
        public async Task<List<MarketItemListingModel>> GetMarketListingsFromApi(string itemName, int itemId, string serverFilter = null)
        {   
            List<MarketItemListingModel> tempMarketList = new List<MarketItemListingModel>();

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
                    if (apiResponse == MarketAPIRequestFailureStatus.APIFailure)
                        return;
                }

                foreach (var listing in apiResponse.Prices)
                {
                    // build a marketlisting with the info we get from method parameters and the api call
                    var marketListing = new MarketItemListingModel()
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
        public async Task<List<HistoryItemListingModel>> GetHistoryListingsFromApi(string itemName, int itemId, string serverFilter = null)
        {
            List<HistoryItemListingModel> tempHistoryList = new List<HistoryItemListingModel>();

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
                    if (apiResponse == MarketAPIRequestFailureStatus.APIFailure)
                        return;
                }

                foreach (var listing in apiResponse.history)
                {
                    // convert companionapi's buyRealData from epoch time (milliseconds) to a normal datetime
                    // set this for converting from epoch time to normal people time
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    var saleDate = epoch.AddMilliseconds(listing.buyRealDate);

                    // build a marketlisting with the info we get from method parameters and the api call
                    var marketListing = new HistoryItemListingModel()
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
            var apiResponse = await QueryCustomApiForHistory(5, "gilgamesh");

            // if apiresponse does not return a status type, then it should be running fine
            if (apiResponse.GetType() != typeof(MarketAPIRequestFailureStatus))
                return MarketAPIRequestFailureStatus.OK;

            // if it does return a status type, pass that back to calling function
            return apiResponse;
        }

        // get item data from XIVAPI using item ID - generally used to get an item's name
        public async Task<dynamic> QueryXivapiWithItemId(int itemId)
        { 
            var i = 0;
            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await $"https://xivapi.com/item/{itemId}".GetJsonAsync();

                    return apiResponse;
                }
                catch (FlurlHttpException exception)
                {
                    // check if no results - flurl sees XIVAPI respond with a 'code 404' if it can't find an item
                    // so it'll throw an exception - we can't check the response body for its contents like we can the custom API
                    if (exception.Call.HttpStatus == HttpStatusCode.NotFound)
                        return MarketAPIRequestFailureStatus.NoResults;

                    await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, $"{exception.Message}"));
                    await Task.Delay(exceptionRetryDelay);
                }
                i++;
            }

            // return generic api failure code
            return MarketAPIRequestFailureStatus.APIFailure;
        }


        public async Task<List<MarketItemAnalysisModel>> CreateMarketAnalysis(string itemName, int itemID, string server = null)
        {
            var analysisHQ = new MarketItemAnalysisModel();
            var analysisNQ = new MarketItemAnalysisModel();

            analysisHQ.Name = itemName;
            analysisNQ.Name = itemName;
            analysisHQ.ID = itemID;
            analysisNQ.ID = itemID;
            analysisHQ.IsHQ = true;
            analysisNQ.IsHQ = false;

            // make API requests for data
            var apiHistoryResponse = await GetHistoryListingsFromApi(itemName, itemID, server);
            var apiMarketResponse = await GetMarketListingsFromApi(itemName, itemID, server);

            // split history results by quality
            var salesHQ = apiHistoryResponse.Where(x => x.IsHq == true).ToList();
            var salesNQ = apiHistoryResponse.Where(x => x.IsHq == false).ToList();

            // split market results by quality
            var marketHQ = apiMarketResponse.Where(x => x.IsHq == true);
            var marketNQ = apiMarketResponse.Where(x => x.IsHq == false);

            // handle HQ items if they exist
            if (salesHQ.Any() && marketHQ.Any())
            {
                // assign recent sale count
                analysisHQ.numRecentSales = CountRecentSales(salesHQ);

                // assign average price of last 5 sales
                analysisHQ.AvgSalePrice = GetAveragePricePerUnit(salesHQ.Take(5).ToList());

                // assign average price of lowest ten listings
                analysisHQ.AvgMarketPrice = GetAveragePricePerUnit(marketHQ.Take(10).ToList());

                // set checks for if item's sold or has listings
                if (analysisHQ.AvgMarketPrice == 0)
                    analysisHQ.ItemHasListings = false;
                else
                    analysisHQ.ItemHasListings = true;

                if (analysisHQ.AvgSalePrice == 0)
                    analysisHQ.ItemHasHistory = false;
                else
                    analysisHQ.ItemHasHistory = true;

                // assign differential of sale price to market price
                if (analysisHQ.ItemHasHistory == false || analysisHQ.ItemHasListings == false)
                    analysisHQ.Differential = 0;
                else
                    analysisHQ.Differential = Math.Round((((decimal)analysisHQ.AvgSalePrice / analysisHQ.AvgMarketPrice) * 100) - 100, 2);
            }

            // handle NQ items
            // assign recent sale count
            analysisNQ.numRecentSales = CountRecentSales(salesNQ);

            // assign average price of last 5 sales
            analysisNQ.AvgSalePrice = GetAveragePricePerUnit(salesNQ.Take(5).ToList());

            // assign average price of lowest ten listings
            analysisNQ.AvgMarketPrice = GetAveragePricePerUnit(marketNQ.Take(10).ToList());

            // set checks for if item's sold or has listings
            if (analysisNQ.AvgMarketPrice == 0)
                analysisNQ.ItemHasListings = false;
            else
                analysisNQ.ItemHasListings = true;

            if (analysisNQ.AvgSalePrice == 0)
                analysisNQ.ItemHasHistory = false;
            else
                analysisNQ.ItemHasHistory = true;

            // assign differential of sale price to market price
            if (analysisNQ.ItemHasHistory == false || analysisNQ.ItemHasListings == false)
                analysisNQ.Differential = 0;
            else
                analysisNQ.Differential = Math.Round((((decimal)analysisNQ.AvgSalePrice / analysisNQ.AvgMarketPrice) * 100) - 100, 2);


            List<MarketItemAnalysisModel> response = new List<MarketItemAnalysisModel>();
            response.Add(analysisHQ);
            response.Add(analysisNQ);

            return response;
        }


        public async Task<List<MarketItemAnalysisModel>> GetBestDealsForSearchTerms(string searchTerms, int lowerIlevel, int upperIlevel, string server = null)
        {
            var apiResponse = await QueryXivapiWithStringAndILevels(searchTerms, lowerIlevel, upperIlevel);

            // take the response data and add it to a list so we can go over it in parallel
            var responseList = new List<MarketItemAnalysisModel>();
            foreach (var item in apiResponse.Results)
            {
                responseList.Add(new MarketItemAnalysisModel()
                {
                    Name = item.Name,
                    ID = (int)item.ID
                });
            }

            List<MarketItemAnalysisModel> results = new List<MarketItemAnalysisModel>();

            var tasks = Task.Run(() => Parallel.ForEach(responseList, parallelOptions, item =>
            {
                var analysis = CreateMarketAnalysis(item.Name, item.ID, server).Result;
                results.AddRange(analysis);
            }));

            await Task.WhenAll(tasks);

            var yeet = results;

            return results.Where(x => x.Differential > 150).ToList();
        }


        public async Task<dynamic> QueryXivapiWithStringAndILevels(string searchTerms, int lowerIlevel, int upperIlevel)
        {
            // number of retries
            var i = 0;

            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await $"https://xivapi.com/search?string={searchTerms}&indexes=item&filters=LevelItem>={lowerIlevel},LevelItem<={upperIlevel},IsUntradable=0&private_key={_xivapiKey}".GetJsonAsync();


                    if (apiResponse.Results.Count == 0)
                        return MarketAPIRequestFailureStatus.NoResults;

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


        public async Task<dynamic> QueryXivapiForCategoryIds()
        {
            // number of retries
            var i = 0;

            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await "https://xivapi.com/market/categories".GetJsonListAsync();

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


                    if ((object)apiResponse != null)
                    {
                        // check if custom API handled error - get apiResponse as dict of keyvalue pairs
                        // if the dict contains 'Error' key, it's a handled error
                        if (((IDictionary<String, object>)apiResponse).ContainsKey("Error"))
                        {
                            if (apiResponse.Error == null)
                                return MarketAPIRequestFailureStatus.APIFailure;
                            if (apiResponse.Error == "Not logged in")
                                return MarketAPIRequestFailureStatus.NotLoggedIn;
                            if (apiResponse.Error == "Under maintenance")
                                return MarketAPIRequestFailureStatus.UnderMaintenance;
                            if (apiResponse.Error == "Access denied")
                                return MarketAPIRequestFailureStatus.AccessDenied;
                            if (apiResponse.Error == "Service unavailable")
                                return MarketAPIRequestFailureStatus.ServiceUnavailable;
                        }

                        if (apiResponse.Prices == null || apiResponse.Prices.Count == 0)
                            return MarketAPIRequestFailureStatus.NoResults;

                        // otherwise, return what we got
                        return apiResponse;
                    }
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
            string wot = "";

            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await $"{_customMarketApiUrl}/market/history.php?id={itemId}&server={server}".GetJsonAsync();

                    if ((object) apiResponse != null)
                    {
                        // check if custom API handled error - get apiResponse as dict of keyvalue pairs
                        // if the dict contains 'Error' key, it's a handled error
                        if (((IDictionary<String, object>)apiResponse).ContainsKey("Error"))
                        {
                            wot = JsonConvert.SerializeObject(apiResponse);

                            if (apiResponse.Error == null)
                                return MarketAPIRequestFailureStatus.APIFailure;
                            if (apiResponse.Error == "Not logged in")
                                return MarketAPIRequestFailureStatus.NotLoggedIn;
                            if (apiResponse.Error == "Under maintenance")
                                return MarketAPIRequestFailureStatus.UnderMaintenance;
                            if (apiResponse.Error == "Access denied")
                                return MarketAPIRequestFailureStatus.AccessDenied;
                            if (apiResponse.Error == "Service unavailable")
                                return MarketAPIRequestFailureStatus.ServiceUnavailable;
                        }

                        if (apiResponse.history == null || apiResponse.history.Count == 0)
                            return MarketAPIRequestFailureStatus.NoResults;

                        // otherwise, return what we got
                        return apiResponse;
                    }
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

        // search XIVAPI using item name - generally used to get an item's ID
        private async Task<dynamic> QueryXivapiWithString(string itemName)
        {
            // number of retries
            var i = 0;

            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await $"https://xivapi.com/search?string={itemName}&indexes=Item&filters=IsUntradable=0&private_key={_xivapiKey}".GetJsonAsync();

                    if (apiResponse.Results.Count == 0)
                        return MarketAPIRequestFailureStatus.NoResults;

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

        private int GetAveragePricePerUnit(List<MarketItemListingModel> listings)
        {
            var sumOfPrices = 0;
            foreach (var item in listings)
            {
                sumOfPrices += item.CurrentPrice;
            }

            if (sumOfPrices == 0 || listings.Count == 0)
                return 0;
            return sumOfPrices / listings.Count;
        }

        private int GetAveragePricePerUnit(List<HistoryItemListingModel> listings)
        {
            var sumOfPrices = 0;
            foreach (var item in listings)
            {
                sumOfPrices += item.SoldPrice;
            }

            if (sumOfPrices == 0 || listings.Count == 0)
                return 0;
            return sumOfPrices / listings.Count;
        }

        private int CountRecentSales(List<HistoryItemListingModel> listings)
        {
            var twoDaysAgo = DateTime.Now.Subtract(TimeSpan.FromDays(2));

            var sales = 0;
            foreach (var item in listings)
            {
                if (item.SaleDate > twoDaysAgo)
                    sales += 1;
            }
            if (sales == 0 || listings.Count == 0)
                return 0;
            return sales;
        }
    }
}
