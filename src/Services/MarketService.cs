using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
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
            "gilgamesh", // first server on list is that datacenter's home server
            "adamantoise",
            "cactuar",
            "faerie",
            "jenova",
            "midgardsormr",
            "sargatanas",
            "siren",
            "leviathan", // first server on list is that datacenter's home server
            "famfrit",
            "hyperion",
            "ultros",
            "behemoth",
            "excalibur",
            "exodus",
            "lamia"
        };
        public List<String> ServerList_Aether = new List<string>()
        {
            "gilgamesh", // first server on list is that datacenter's home server
            "adamantoise",
            "cactuar",
            "faerie",
            "jenova",
            "midgardsormr",
            "sargatanas",
            "siren"
        };
        public List<String> ServerList_Primal = new List<string>()
        {
            "leviathan", // first server on list is that datacenter's home server
            "famfrit",
            "hyperion",
            "ultros",
            "behemoth",
            "excalibur",
            "exodus",
            "lamia"
        };

        // Set to 1 to (hopefully) force parallel.foreach loops to run one at a time (synchronously)
        // set to -1 for default behavior
        private static ParallelOptions parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = -1 };
        private int exceptionRetryCount = 5; // number of times to retry api requests
        private int exceptionRetryDelay = 1000; // ms delay between retries

        public MarketService(IConfigurationRoot config, LoggingService logger)
        {
            _logger = logger;

            _customMarketApiUrl = config["custommarketapiurl"];
            _xivapiKey = config["xivapikey"];

            // set this so httpclient can make multiple connections at once
            ServicePointManager.DefaultConnectionLimit = 200;

            // to make parallel.foreach use more threads
            ThreadPool.SetMinThreads(200, 200);
        }

        // gets list of items, loads them into an list, returns list of items or empty list if request failed
        public async Task<List<ItemSearchResultModel>> SearchForItemByName(string searchTerm)
        {
            var apiResponse = await QueryXivapiWithString(searchTerm);

            var tempItemList = new List<ItemSearchResultModel>();

            // if api failure, return empty list
            if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus) &&
                apiResponse == MarketAPIRequestFailureStatus.APIFailure)
                return null; // empty list

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

        // gets list of items, loads them into an list, returns list of items or empty list if request failed
        public async Task<List<ItemSearchResultModel>> SearchForItemByNameExact(string searchTerm)
        {
            var apiResponse = await QueryXivapiWithStringExact(searchTerm);

            var tempItemList = new List<ItemSearchResultModel>();

            // if api failure, return empty list
            if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus) &&
                apiResponse == MarketAPIRequestFailureStatus.APIFailure)
                return null; // empty list

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
        public async Task<List<MarketItemListingModel>> GetMarketListings(string itemName, int itemId, Datacenter datacenter, string serverFilter = null)
        {   
            List<MarketItemListingModel> tempMarketList = new List<MarketItemListingModel>();

            // if calling method provided server via serverOption, use that
            // otherwise, search all servers on Aether
            List<string> tempServerList = new List<string>();
            if (serverFilter != null)
                tempServerList.Add(serverFilter);
            else
            {
                if (datacenter == Datacenter.Aether)
                    tempServerList.AddRange(ServerList_Aether);
                if (datacenter == Datacenter.Primal)
                    tempServerList.AddRange(ServerList_Primal);
            }

            // get all market entries for specified item across all servers, bundle results into tempMarketList
            var tasks = Task.Run(() => Parallel.ForEach(tempServerList, parallelOptions, server =>
            {
                var apiResponse = QueryCustomApiForListings(itemId, server).Result;

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
            tempMarketList = tempMarketList.OrderBy(x => x.CurrentPrice).ToList();

            return tempMarketList;
        }

        // take an item id and get the lowest market listings from across all servers, return list of MarketList of MarketItemListings
        public async Task<List<HistoryItemListingModel>> GetHistoryListings(string itemName, int itemId, Datacenter datacenter, string serverFilter = null)
        {
            List<HistoryItemListingModel> tempHistoryList = new List<HistoryItemListingModel>();

            // if calling method provided server via serverOption, use that
            // otherwise, search all servers on Aether
            List<string> tempServerList = new List<string>();
            if (serverFilter != null)
                tempServerList.Add(serverFilter);
            else
            {
                if (datacenter == Datacenter.Aether)
                    tempServerList.AddRange(ServerList_Aether);
                if (datacenter == Datacenter.Primal)
                    tempServerList.AddRange(ServerList_Primal);
            }

            // get all market entries for specified item across all servers, bundle results into tempMarketList
            var tasks = Task.Run(() => Parallel.ForEach(tempServerList, parallelOptions, server =>
            {
                var apiResponse = QueryCustomApiForHistory(itemId, server).Result;

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


        // returns three analysis objects: hq, nq, and overall
        public async Task<List<MarketItemAnalysisModel>> CreateMarketAnalysis(string itemName, int itemID, Datacenter datacenter, string server = null)
        {
            if (server == null)
            {
                if (datacenter == Datacenter.Aether)
                    server = ServerList_Aether[0];
                if (datacenter == Datacenter.Primal)
                    server = ServerList_Primal[0];
            }

            var analysisHQ = new MarketItemAnalysisModel();
            var analysisNQ = new MarketItemAnalysisModel();
            var analysisOverall = new MarketItemAnalysisModel(); // items regardless of quality - used for exchange command

            analysisHQ.Name = itemName;
            analysisNQ.Name = itemName;
            analysisOverall.Name = itemName;
            analysisHQ.ID = itemID;
            analysisNQ.ID = itemID;
            analysisOverall.ID = itemID;
            analysisHQ.IsHQ = true;
            analysisNQ.IsHQ = false;
            analysisOverall.IsHQ = false;

            // make API requests for data
            var apiHistoryResponse = await GetHistoryListings(itemName, itemID, datacenter, server);
            var apiMarketResponse = await GetHistoryListings(itemName, itemID, datacenter, server);

            // split history results by quality
            var salesHQ = apiHistoryResponse.Where(x => x.IsHq == true).ToList();
            var salesNQ = apiHistoryResponse.Where(x => x.IsHq == false).ToList();
            var salesOverall = apiHistoryResponse.ToList();

            // split market results by quality
            var marketHQ = apiMarketResponse.Where(x => x.IsHq == true).ToList();
            var marketNQ = apiMarketResponse.Where(x => x.IsHq == false).ToList();
            var marketOverall = apiHistoryResponse.ToList();

            // handle HQ items if they exist
            if (salesHQ.Any() && marketHQ.Any())
            {
                // assign recent sale count
                analysisHQ.NumRecentSales = CountRecentSales(salesHQ);

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
            analysisNQ.NumRecentSales = CountRecentSales(salesNQ);

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


            // handle overall items list
            // assign recent sale count
            analysisOverall.NumRecentSales = CountRecentSales(salesOverall);

            // assign average price of last 5 sales
            analysisOverall.AvgSalePrice = GetAveragePricePerUnit(salesOverall.Take(5).ToList());

            // assign average price of lowest ten listings
            analysisOverall.AvgMarketPrice = GetAveragePricePerUnit(marketOverall.Take(10).ToList());

            // set checks for if item's sold or has listings
            if (analysisOverall.AvgMarketPrice == 0)
                analysisOverall.ItemHasListings = false;
            else
                analysisOverall.ItemHasListings = true;

            if (analysisOverall.AvgSalePrice == 0)
                analysisOverall.ItemHasHistory = false;
            else
                analysisOverall.ItemHasHistory = true;

            // assign differential of sale price to market price
            if (analysisOverall.ItemHasHistory == false || analysisOverall.ItemHasListings == false)
                analysisOverall.Differential = 0;
            else
                analysisOverall.Differential = Math.Round((((decimal)analysisOverall.AvgSalePrice / analysisOverall.AvgMarketPrice) * 100) - 100, 2);


            List<MarketItemAnalysisModel> response = new List<MarketItemAnalysisModel>();
            response.Add(analysisHQ);
            response.Add(analysisNQ);
            response.Add(analysisOverall);

            return response;
        }


        public async Task<List<CurrencyTradeableItem>> GetBestCurrencyExchange(string category, Datacenter datacenter, string server = null)
        {
            List<CurrencyTradeableItem> itemsList = new List<CurrencyTradeableItem>(); // gets overwritten shortly

            switch (category)
            {
                case "gc":
                    itemsList = CurrencyTradeableItemsModel.GrandCompanySealItemsList;
                    break;
                case "poetics":
                    itemsList = CurrencyTradeableItemsModel.PoeticsItemsList;
                    break;
                case "gemstones":
                    itemsList = CurrencyTradeableItemsModel.GemstonesItemsList;
                    break;
                case "nuts":
                    itemsList = CurrencyTradeableItemsModel.NutsItemsList;
                    break;
                case "wgs":
                    itemsList = CurrencyTradeableItemsModel.WhiteGathererScripsItemsList;
                    break;
                case "wcs":
                    itemsList = CurrencyTradeableItemsModel.WhiteCrafterScripsItemsList;
                    break;
                case "ygs":
                    itemsList = CurrencyTradeableItemsModel.YellowGathererScripsItemsList;
                    break;
                case "ycs":
                    itemsList = CurrencyTradeableItemsModel.YellowCrafterScripsItemsList;
                    break;
                case "goetia":
                    itemsList = CurrencyTradeableItemsModel.GoetiaItemsList;
                    break;
                default:
                    return itemsList;
            }
            
            var tasks = Task.Run(() => Parallel.ForEach(itemsList, parallelOptions, item =>
            {
                var analysisResponse = CreateMarketAnalysis(item.Name, item.ItemID, datacenter, server).Result;
                // index 2 is the 'overall' analysis that includes both NQ and HQ items
                var analysis = analysisResponse[2];

                item.AvgMarketPrice = analysis.AvgMarketPrice;
                item.AvgSalePrice = analysis.AvgSalePrice;
                item.ValueRatio = item.AvgMarketPrice / item.CurrencyCost;
                item.NumRecentSales = analysis.NumRecentSales;
            }));

            await Task.WhenAll(tasks);

            return itemsList;
        }


        public async Task<List<MarketItemXWOrderModel>> GetMarketCrossworldPurchaseOrder(List<MarketItemXWOrderModel> inputs, Datacenter datacenter)
        {
            List<MarketItemXWOrderModel> PurchaseOrderList = new List<MarketItemXWOrderModel>();

            // iterate through each item
            var tasks = Task.Run(() => Parallel.ForEach(inputs, parallelOptions, input =>
            {
                var response = SearchForItemByNameExact(input.Name).Result;

                // getting first response for now, but we should find a way to make this more flexible later
                var itemName = response[0].Name;
                var itemId = response[0].ID;
                // paramer values only for use in this function
                var neededQuantity = input.NeededQuantity;
                var shouldBeHq = input.ShouldBeHQ;
                // for large requests, take a RAM hit to grab more listings
                var numOfListingsToTake = 15;
                if (neededQuantity > 99)
                    numOfListingsToTake = 20;
                if (neededQuantity > 200)
                    numOfListingsToTake = 25;

                var listings = GetMarketListings(itemName, itemId, datacenter).Result;

                // if we need this item to be hq, we should filter out NQ listings now
                if (shouldBeHq)
                    listings = listings.Where(x => x.IsHq).ToList();

                // put together a list of each market listing
                var multiPartOrderList = new List<MarketItemXWOrderModel>();
                // only look at listings that are less than double the price of the lowest cost item
                // we'd need to change this to look at averages since the lowest price could be a major outlier

                foreach (var listing in listings.Take(numOfListingsToTake))
                {
                    // make a new item each iteration
                    var item = new MarketItemXWOrderModel()
                    {
                        Name = itemName,
                        ItemID = itemId,
                        NeededQuantity = neededQuantity,
                        Price = listing.CurrentPrice,
                        Server = listing.Server,
                        Quantity = listing.Quantity,
                        IsHQ = listing.IsHq
                    };

                    multiPartOrderList.Add(item);
                }

                // send our listings off to find what the most efficient set of listings to buy are
                var efficientListings = GetMostEfficientPurchases(multiPartOrderList, input.NeededQuantity);


                if (efficientListings.Any())
                    PurchaseOrderList.AddRange(efficientListings.FirstOrDefault());

                multiPartOrderList.Clear();
            }));

            await Task.WhenAll(tasks);

            PurchaseOrderList = PurchaseOrderList.OrderBy(x => x.Server).ToList();

            return PurchaseOrderList;
        }


        // get the status of the Companion API
        public async Task<MarketAPIRequestFailureStatus> GetCompanionApiStatus(string server = null)
        {
            if (server == null)
                server = "gilgamesh";

            // run test query
            var apiResponse = await QueryCustomApiForHistory(5, server);

            // if apiresponse does not return a status type, then it should be running fine
            if (apiResponse.GetType() != typeof(MarketAPIRequestFailureStatus))
                return MarketAPIRequestFailureStatus.OK;

            // if it does return a status type, pass that back to calling function
            return apiResponse;
        }


        // log in to the companion api
        public async Task<bool> LoginToCompanionAPI(string server)
        {
            //dynamic apiResponse = await "https://xivapi.com/market/categories".GetJsonListAsync();
            var response =
                await
                    $"{_customMarketApiUrl}/market/scripts/logincmd.php?server={server}"
                        .GetStringAsync();

            if (response.Contains("1"))
                return true;
            else
                return false;
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

                        if (((IDictionary<String, object>)apiResponse).ContainsKey("Prices") == false)
                            return MarketAPIRequestFailureStatus.APIFailure;

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

                        if (((IDictionary<String, object>)apiResponse).ContainsKey("history") == false)
                            return MarketAPIRequestFailureStatus.APIFailure;

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

        // search XIVAPI using item name - generally used to get an item's ID
        private async Task<dynamic> QueryXivapiWithStringExact(string itemName)
        {
            // number of retries
            var i = 0;

            while (i < exceptionRetryCount)
            {
                try
                {
                    dynamic apiResponse = await $"https://xivapi.com/search?string={itemName}&indexes=Item&string_algo=match&filters=IsUntradable=0&private_key={_xivapiKey}".GetJsonAsync();

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

        // this is used to determine the most efficient order of buying items cross-world
        // results are pre-sorted by:
        // 1. how close to the needed value that group of listings is (greater than or equal to)

        // this function returns the 20 values of best 'fit' for these requirements given the parameters
        private static List<IEnumerable<MarketItemXWOrderModel>> GetMostEfficientPurchases(List<MarketItemXWOrderModel> listings, int needed)
        {
            var target = Enumerable.Range(1, listings.Count)
                .SelectMany(p => listings.Combinations(p))
                .OrderBy(p => Math.Abs((int)p.Select(x => x.Quantity).Sum() - needed)) // sort by number of listings - fewest orders possible
                .ThenBy(x => x.Sum(y => y.Price * y.Quantity)) // sort by total price of listing - typically leans towards more orders but cheaper overall
                .Where(x => x.Sum(y => y.Quantity) >= needed) // where the total quantity is what we need, or more
                .Take(20);

            return target.ToList();
        }
    }

    // for use with the GetMostEfficientPurchases command, to build order lists
    public static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> Combinations<T>(this IEnumerable<T> elements, int k)
        {
            return k == 0 ? new[] { new T[0] } :
                elements.SelectMany((e, i) =>
                    elements.Skip(i + 1).Combinations(k - 1).Select(c => (new[] { e }).Concat(c)));
        }
    }
}
