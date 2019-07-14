using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Doccer_Bot.Entities;
using Doccer_Bot.Models;
using Doccer_Bot.Modules.Common;
using Doccer_Bot.Services;
using Flurl.Util;

namespace Doccer_Bot.Modules
{
    [Name("Market")]
    [Remarks("Realtime FFXIV market data")]
    public class FFXIVMarketModule : InteractiveBase
    {
        public MarketService MarketService { get; set; }

        private Dictionary<IUser, IUserMessage> _dictFindItemUserEmbedPairs = new Dictionary<IUser, IUserMessage>();


        [Command("market price", RunMode = RunMode.Async)]
        [Summary("Get prices for an item - takes item name or item id")]
        [Example("market price (server) {name/id}")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketGetItemPriceAsync([Remainder] string searchTerm)
        {
            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            searchTerm = searchTerm.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // check if the API is operational, handle it if it's not
            var apiStatus = await MarketService.GetCompanionApiStatus();
            if (apiStatus != MarketAPIRequestFailureStatus.OK)
            {
                string apiStatusHumanResponse = "";

                if (apiStatus == MarketAPIRequestFailureStatus.NotLoggedIn)
                    apiStatusHumanResponse = $"Not logged in to Companion API. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
                if (apiStatus == MarketAPIRequestFailureStatus.UnderMaintenance)
                    apiStatusHumanResponse = "SE's API is down for maintenance.";
                if (apiStatus == MarketAPIRequestFailureStatus.AccessDenied)
                    apiStatusHumanResponse = $"Access denied. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
                if (apiStatus == MarketAPIRequestFailureStatus.ServiceUnavailable || apiStatus == MarketAPIRequestFailureStatus.APIFailure)
                    apiStatusHumanResponse = $"Something went wrong. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";

                await ReplyAsync(apiStatusHumanResponse);
                return;
            }

            // try to get server name from the given text
            var server = MarketService.ServerList.Where(searchTerm.Contains).FirstOrDefault();
            // if server's not null, user provided a specific server
            // remove server name from the text
            if (server != null)
                searchTerm = searchTerm.Replace($"{server}", "").Trim();

            // declare vars - both of these will get populated eventually
            int itemId;
            string itemName = "";
            string itemIconUrl = "";

            // try to see if the given text is an item ID
            var searchTermIsItemId = int.TryParse(searchTerm, out itemId);


            // if user passed a itemname, get corresponding itemid. Does any of the following:

            // * call the interactive user select function and terminates, if there are multiple search results
            //      in this case, the interactive user select function will re-run the function and pass a single item ID

            // * assigns itemid to search result, if there was only one search result, and then continue the function
            if (!searchTermIsItemId)
            {
                // response is either a ordereddictionary of keyvaluepairs, or null
                var itemIdQueryResult = await MarketService.SearchForItemByName(searchTerm);

                // no results
                if (itemIdQueryResult.Count == 0)
                {
                    await ReplyAsync("No tradable items found. Try to expand your search terms, or check for typos. ");
                    return;
                }

                // too many results
                if (itemIdQueryResult.Count > 10)
                {
                    var resultcount = $"{itemIdQueryResult.Count}";
                    if (itemIdQueryResult.Count == 100)
                        resultcount = "100+";

                    await ReplyAsync($"Too many results found ({resultcount}). Try to narrow down your search terms, or use `.item search` to get your item's ID and use that instead.");
                    return;
                }

                // if more than one result was found, send the results to the selection function to narrow it down to one
                // terminate this function, as the selection function will eventually re-call this method with a single result item
                // 10 is the max number of items we can use interactiveuserselectitem with
                if (itemIdQueryResult.Count > 1 && itemIdQueryResult.Count < 10) 
                {
                    await InteractiveUserSelectItem(itemIdQueryResult, "market", server);
                    return;
                }

                // if only one result was found, select it and continue without any prompts
                if (itemIdQueryResult.Count == 1)
                {
                    itemId = itemIdQueryResult[0].ID;
                }
            }

            // get the item name & assign it
            var itemDetailsQueryResult = await MarketService.QueryXivapiWithItemId(itemId);

            // no results - should only trigger if user inputs a bad itemID
            if (itemDetailsQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemDetailsQueryResult == MarketAPIRequestFailureStatus.NoResults)
            {
                await ReplyAsync("The item ID you provided doesn't correspond to any items. Try searching by item name instead.");
                return;
            }

            itemName = itemDetailsQueryResult.Name;
            itemIconUrl = $"https://xivapi.com/{itemDetailsQueryResult.Icon}";

            // get market data - server param can be null, since this function sees server as optional null
            // if user specified a server, it'll send one, but otherwise the function will check on all servers
            var marketQueryResults = await MarketService.GetMarketListingsFromApi(itemName, itemId, server);

            if (marketQueryResults.Count == 0)
            {
                await ReplyAsync(
                    "No listings found for that item. Either nobody's selling it, or it's not tradable.");
                return;
            }

            // format market data & display

            var pages = new List<PaginatedMessage.Page>();

            var i = 0;
            var itemsPerPage = 12;

            // iterate through the market results, making a page for every (up to) itemsPerPage listings
            while (i < marketQueryResults.Count)
            {
                // pull up to itemsPerPage entries from the list, skipping any from previous iterations
                var currentPageMarketList = marketQueryResults.Skip(i).Take(itemsPerPage);

                StringBuilder sbListing = new StringBuilder();

                // build data for this page
                foreach (var listing in currentPageMarketList)
                {
                    sbListing.Append($"• **{listing.Quantity}** ");

                    if (listing.IsHq)
                        sbListing.Append("**HQ** ");

                    if (listing.Quantity > 1)
                        // multiple units
                        sbListing.Append($"for {listing.CurrentPrice * listing.Quantity} (**{listing.CurrentPrice}** per unit) ");
                    else // single units
                        sbListing.Append($"for **{listing.CurrentPrice}** ");
                    if (server == null)
                        sbListing.Append($"on **{listing.Server}**");
                    sbListing.AppendLine();
                }

                var page = new PaginatedMessage.Page()
                {
                    Fields = new List<EmbedFieldBuilder>()
                    {
                        new EmbedFieldBuilder()
                        {
                            Name = $"{itemName}",
                            Value = sbListing
                        }
                    }
                };

                pages.Add(page);

                i = i + itemsPerPage;
            }

            var pager = new PaginatedMessage()
            {
                Pages = pages,
                Author = new EmbedAuthorBuilder()
                {
                    Name = $"{marketQueryResults.Count} Market listing(s) for {itemName}",
                },
                ThumbnailUrl = itemIconUrl,
                Color = Color.Blue,
                Options = new PaginatedAppearanceOptions()
                {
                    InformationText = "This is an interactive message. Use the reaction emotes to change pages. Use the :1234: emote and then type a number in chat to go to that page.",
                }
            };

            await PagedReplyAsync(pager, new ReactionList()
            {
                Forward = true,
                Backward = true,
                First = true,
                Last = true,
                Info = true,
                Jump = true,
            });
        }


        [Command("market history", RunMode = RunMode.Async)]
        [Summary("Get history for an item - takes item name or item id")]
        [Example("market price (server) {name/id}")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketGetItemHistoryAsync([Remainder] string searchTerm)
        {
            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            searchTerm = searchTerm.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // check if the API is operational, handle it if it's not
            var apiStatus = await MarketService.GetCompanionApiStatus();
            if (apiStatus != MarketAPIRequestFailureStatus.OK)
            {
                string apiStatusHumanResponse = "";

                if (apiStatus == MarketAPIRequestFailureStatus.NotLoggedIn)
                    apiStatusHumanResponse = $"Not logged in to Companion API. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
                if (apiStatus == MarketAPIRequestFailureStatus.UnderMaintenance)
                    apiStatusHumanResponse = "SE's API is down for maintenance.";
                if (apiStatus == MarketAPIRequestFailureStatus.AccessDenied)
                    apiStatusHumanResponse = $"Access denied. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
                if (apiStatus == MarketAPIRequestFailureStatus.ServiceUnavailable || apiStatus == MarketAPIRequestFailureStatus.APIFailure)
                    apiStatusHumanResponse = $"Something went wrong. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";

                await ReplyAsync(apiStatusHumanResponse);
                return;
            }

            // try to get server name from the given text
            var server = MarketService.ServerList.Where(searchTerm.Contains).FirstOrDefault();
            // if server's not null, remove server name from the text
            if (server != null)
                searchTerm = searchTerm.Replace($"{server}", "").Trim();

            // declare vars - both of these will get populated eventually
            int itemId;
            string itemName = "";
            string itemIconUrl = "";

            // try to see if the given text is an item ID
            var searchTermIsItemId = int.TryParse(searchTerm, out itemId);


            // if user passed a itemname, get corresponding itemid. Does any of the following:

            // * call the interactive user select function and terminates, if there are multiple search results
            //      in this case, the interactive user select function will re-run the function and pass a single item ID

            // * assigns itemid to search result, if there was only one search result, and then continue the function
            if (!searchTermIsItemId)
            {
                // response is either a ordereddictionary of keyvaluepairs, or null
                var itemIdQueryResult = await MarketService.SearchForItemByName(searchTerm);

                // no results
                if (itemIdQueryResult.Count == 0)
                {
                    await ReplyAsync("No tradable items found. Try to expand your search terms, or check for typos.");
                    return;
                }

                // too many results
                if (itemIdQueryResult.Count > 10)
                {
                    var resultcount = $"{itemIdQueryResult.Count}";
                    if (itemIdQueryResult.Count == 100)
                        resultcount = "100+";

                    await ReplyAsync($"Too many results found ({resultcount}). Try to narrow down your search terms, or use `.item search` to get your item's ID and use that instead.");
                    return;
                }

                // if more than one result was found, send the results to the selection function to narrow it down to one
                // terminate this function, as the selection function will eventually re-call this method with a single result item
                if (itemIdQueryResult.Count > 1)
                {
                    await InteractiveUserSelectItem(itemIdQueryResult, "history", server);
                    return;
                }

                // if only one result was found, select it and continue without any prompts
                if (itemIdQueryResult.Count == 1)
                {
                    itemId = itemIdQueryResult[0].ID;
                }
            }

            // get the item name & assign it
            var itemDetailsQueryResult = await MarketService.QueryXivapiWithItemId(itemId);

            // no results
            if (itemDetailsQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemDetailsQueryResult == MarketAPIRequestFailureStatus.NoResults)
            {
                await ReplyAsync("The item ID you provided doesn't correspond to any items. Try searching by item name instead.");
                return;
            }

            itemName = itemDetailsQueryResult.Name;
            itemIconUrl = $"https://xivapi.com/{itemDetailsQueryResult.Icon}";

            // get market data - server param can be null, since this function sees server as optional null
            // if user specified a server, it'll send one, but otherwise the function will check on all servers
            var historyQueryResults = await MarketService.GetHistoryListingsFromApi(itemName, itemId, server);

            if (historyQueryResults.Count == 0)
            {
                await ReplyAsync(
                    "No listings found for that item. Either nobody's selling it, or it's not marketable.");
                return;
            }


            // format history data & display

            var pages = new List<PaginatedMessage.Page>();

            var i = 0;
            var itemsPerPage = 10;

            // iterate through the history results, making a page for every (up to) itemsPerPage listings
            while (i < historyQueryResults.Count)
            {
                // pull up to itemsPerPage entries from the list, skipping any from previous iterations
                var currentPageHistoryList = historyQueryResults.Skip(i).Take(itemsPerPage);

                StringBuilder sbListing = new StringBuilder();

                // build data for this page
                foreach (var listing in currentPageHistoryList)
                {
                    sbListing.Append($"• **{listing.Quantity}** ");

                    if (listing.IsHq)
                        sbListing.Append("**HQ** ");

                    if (listing.Quantity > 1)
                        // multiple units
                        sbListing.Append($"for {listing.SoldPrice * listing.Quantity} (**{listing.SoldPrice}** per unit) ");
                    else // single units
                        sbListing.Append($"for **{listing.SoldPrice}** ");
                    sbListing.AppendLine();
                    sbListing.Append("››› Sold ");
                    if (server == null)
                        sbListing.Append($"on **{listing.Server}** ");
                    sbListing.Append($"at {listing.SaleDate}");
                    sbListing.AppendLine();
                }

                var page = new PaginatedMessage.Page()
                {
                    Fields = new List<EmbedFieldBuilder>()
                    {
                        new EmbedFieldBuilder()
                        {
                            Name = $"{itemName}",
                            Value = sbListing
                        }
                    }
                };

                pages.Add(page);

                i = i + itemsPerPage;
            }

            var pager = new PaginatedMessage()
            {
                Pages = pages,
                Author = new EmbedAuthorBuilder()
                {
                    Name = $"{historyQueryResults.Count} History listing(s) for {itemName}",
                },
                ThumbnailUrl = itemIconUrl,
                Color = Color.Blue
            };

            await PagedReplyAsync(pager, new ReactionList()
            {
                Forward = true,
                Backward = true,
                First = true,
                Last = true,
                Info = true,
                Jump = true
            });
        }


        [Command("market analyze", RunMode = RunMode.Async)]
        [Summary("Get market analysis for an item")]
        [Example("market analyze {name/id} (server) - defaults to Gilgamesh")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketAnalyzeItemAsync([Remainder] string searchTerm)
        {
            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            searchTerm = searchTerm.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // check if the API is operational, handle it if it's not
            var apiStatus = await MarketService.GetCompanionApiStatus();
            if (apiStatus != MarketAPIRequestFailureStatus.OK)
            {
                string apiStatusHumanResponse = "";

                if (apiStatus == MarketAPIRequestFailureStatus.NotLoggedIn)
                    apiStatusHumanResponse =
                        $"Not logged in to Companion API. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
                if (apiStatus == MarketAPIRequestFailureStatus.UnderMaintenance)
                    apiStatusHumanResponse = "SE's API is down for maintenance.";
                if (apiStatus == MarketAPIRequestFailureStatus.AccessDenied)
                    apiStatusHumanResponse =
                        $"Access denied. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
                if (apiStatus == MarketAPIRequestFailureStatus.ServiceUnavailable ||
                    apiStatus == MarketAPIRequestFailureStatus.APIFailure)
                    apiStatusHumanResponse =
                        $"Something went wrong. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";

                await ReplyAsync(apiStatusHumanResponse);
                return;
            }

            // try to get server name from the given text
            var server = MarketService.ServerList.Where(searchTerm.Contains).FirstOrDefault();
            // if server's not null, remove server name from the text
            if (server != null)
                searchTerm = searchTerm.Replace($"{server}", "").Trim();

            // declare vars - both of these will get populated eventually
            int itemId;
            string itemName = "";
            string itemIconUrl = "";

            // try to see if the given text is an item ID
            var searchTermIsItemId = int.TryParse(searchTerm, out itemId);


            // if user passed a itemname, get corresponding itemid. Does any of the following:

            // * call the interactive user select function and terminates, if there are multiple search results
            //      in this case, the interactive user select function will re-run the function and pass a single item ID

            // * assigns itemid to search result, if there was only one search result, and then continue the function
            if (!searchTermIsItemId)
            {
                // remove any trailing spaces
                if (searchTerm.EndsWith(" "))
                    searchTerm = searchTerm.Remove(searchTerm.Length - 1);

                // response is either a ordereddictionary of keyvaluepairs, or null
                var itemIdQueryResult = await MarketService.SearchForItemByName(searchTerm);

                // no results
                if (itemIdQueryResult.Count == 0)
                {
                    await ReplyAsync("No tradable items found. Try to expand your search terms, or check for typos.");
                    return;
                }

                // too many results
                if (itemIdQueryResult.Count > 10)
                {
                    var resultcount = $"{itemIdQueryResult.Count}";
                    if (itemIdQueryResult.Count == 100)
                        resultcount = "100+";

                    await ReplyAsync(
                        $"Too many results found ({resultcount}). Try to narrow down your search terms, or use `.item search` to get your item's ID and use that instead.");
                    return;
                }

                // if more than one result was found, send the results to the selection function to narrow it down to one
                // terminate this function, as the selection function will eventually re-call this method with a single result item
                if (itemIdQueryResult.Count > 1)
                {
                    await InteractiveUserSelectItem(itemIdQueryResult, "analyze", server);
                    return;
                }

                // if only one result was found, select it and continue without any prompts
                if (itemIdQueryResult.Count == 1)
                {
                    itemId = itemIdQueryResult[0].ID;
                }
            }

            // get the item name & assign it
            var itemDetailsQueryResult = await MarketService.QueryXivapiWithItemId(itemId);

            // no results
            if (itemDetailsQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) &&
                itemDetailsQueryResult == MarketAPIRequestFailureStatus.NoResults)
            {
                await ReplyAsync(
                    "The item ID you provided doesn't correspond to any items. Try searching by item name instead.");
                return;
            }

            itemName = itemDetailsQueryResult.Name;
            itemIconUrl = $"https://xivapi.com/{itemDetailsQueryResult.Icon}";

            // get market data - server param can be null, since this function sees server as optional null
            // if user specified a server, it'll send one, but otherwise the function will check on all servers
            var marketAnalysis = await MarketService.CreateMarketAnalysis(itemName, itemId, server);
            var hqMarketAnalysis = marketAnalysis[0];
            var nqMarketAnalysis = marketAnalysis[1];

            // format history data & display

            EmbedBuilder analysisEmbedBuilder = new EmbedBuilder();

            // hq stuff if values are filled in
            if (hqMarketAnalysis.NumRecentSales != 0)
            {
                StringBuilder hqFieldBuilder = new StringBuilder();
                hqFieldBuilder.AppendLine($"Avg Listed Price: {hqMarketAnalysis.AvgMarketPrice}");
                hqFieldBuilder.AppendLine($"Avg Sale Price: {hqMarketAnalysis.AvgSalePrice}");
                hqFieldBuilder.AppendLine($"Differential: {hqMarketAnalysis.Differential}%");
                hqFieldBuilder.Append("Active:");
                if (hqMarketAnalysis.NumRecentSales >= 5)
                    hqFieldBuilder.AppendLine(" Yes");
                else
                    hqFieldBuilder.AppendLine("No");
                hqFieldBuilder.AppendLine($"Number of sales: {hqMarketAnalysis.NumRecentSales}");
                if (hqMarketAnalysis.NumRecentSales >= 20)
                    hqFieldBuilder.AppendLine("+");

                analysisEmbedBuilder.AddField("HQ", hqFieldBuilder.ToString());
            }
            // nq stuff - first line inline=true in case we had hq values
            StringBuilder nqFieldBuilder = new StringBuilder();
            nqFieldBuilder.AppendLine($"Avg Listed Price: {nqMarketAnalysis.AvgMarketPrice}");
            nqFieldBuilder.AppendLine($"Avg Sale Price: {nqMarketAnalysis.AvgSalePrice}");
            nqFieldBuilder.AppendLine($"Differential: {nqMarketAnalysis.Differential}%");
            nqFieldBuilder.Append("Active:");
            if (nqMarketAnalysis.NumRecentSales >= 5)
                nqFieldBuilder.AppendLine(" Yes");
            else
                nqFieldBuilder.AppendLine("No");
            nqFieldBuilder.Append($"Number of sales: {nqMarketAnalysis.NumRecentSales}");
            if (nqMarketAnalysis.NumRecentSales >= 20)
                nqFieldBuilder.AppendLine("+");
            else
                nqFieldBuilder.AppendLine("");

            analysisEmbedBuilder.AddField("NQ", nqFieldBuilder.ToString());

            StringBuilder embedNameBuilder = new StringBuilder();
            embedNameBuilder.Append($"Market analysis for {itemName}");
            if (server != null)
                embedNameBuilder.Append($" on {server}");

            analysisEmbedBuilder.Author = new EmbedAuthorBuilder()
            {
                Name = embedNameBuilder.ToString()
            };
            analysisEmbedBuilder.ThumbnailUrl = itemIconUrl;
            analysisEmbedBuilder.Color = Color.Blue;

            await ReplyAsync(null, false, analysisEmbedBuilder.Build());
        }


        // should be able to accept inputs in any order - if two values are provided, they will be treated as minilvl and maxilvl respectively
        [Command("market deals", RunMode = RunMode.Async)]
        [Summary("Get market analyses for item categories")]
        [Example("market deals {searchterms} (minilvl) (maxilvl) (server) - defaults to Gilgamesh")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketGetDealsAsync(params string[] inputs)
        {
            int index = 0;

            // set defaults
            int defaultLowerIlevel = 0;
            int defaultUpperIlevel = 1000;
            string defaultServer = "gilgamesh";

            // if no inputs, display categories
            if (!inputs.Any())
            {
                // get categories, list them out
                var categories = await MarketService.QueryXivapiForCategoryIds();

                EmbedBuilder categoryIDsEmbedBuilder = new EmbedBuilder();
                StringBuilder categoryIDsField1Builder = new StringBuilder();
                StringBuilder categoryIDsField2Builder = new StringBuilder();

                // sort categories into alternating columns for two embed fields
                index = 0;
                foreach (var category in categories)
                {
                    // odd
                    if (index % 2 != 0)
                        categoryIDsField1Builder.AppendLine($"{category.Name} - {category.ID}");
                    // even
                    if (index % 2 == 0)
                        categoryIDsField2Builder.AppendLine($"{category.Name} - {category.ID}");
                    index++;
                }
                
                categoryIDsEmbedBuilder.AddField("Categories", categoryIDsField1Builder.ToString(), true);
                categoryIDsEmbedBuilder.AddField("Categories", categoryIDsField2Builder.ToString(), true);
                categoryIDsEmbedBuilder.Color = Color.Blue;

                await ReplyAsync(null, false, categoryIDsEmbedBuilder.Build());
                return;
            }

            // init working collections
            string combinedStringInputs = "";
            var integerInputsList = new List<int>();

            // loop through inputs, determine if each is an integer or a string, and assign each 
            foreach (var input in inputs)
            {
                int integerInput;
                var inputWasInt = int.TryParse(input, out integerInput);

                if (inputWasInt)
                    integerInputsList.Add(integerInput);
                else
                {
                    combinedStringInputs += $"{input} ";
                }
            }

            // if list count is 0, we didn't get any ilv inputs, so set them
            if (integerInputsList.Count == 0)
            {
                integerInputsList.Add(defaultLowerIlevel);
                integerInputsList.Add(defaultUpperIlevel);
            }
            // if list count is 2, we only got lower ilvl, so add upper ilvl 
            if (integerInputsList.Count == 1)
            {
                integerInputsList.Add(defaultUpperIlevel);
            }

            int lowerIlevel = integerInputsList[0];
            int upperIlevel = integerInputsList[1];


            string searchTerms = null;
            string server = defaultServer;
            
            // get server from combined inputs if it exists
            server = MarketService.ServerList.Where(combinedStringInputs.Contains).FirstOrDefault();
            // remove server text from combined inputs if server text exists
            if (server != null)
                combinedStringInputs = combinedStringInputs.Replace($"{server}", "").Trim();

            // assign remaining text to search terms
            searchTerms = combinedStringInputs;

            // remove any trailing spaces
            // .trim should take care of this instead?
            //if (searchTerms.EndsWith(" "))
                //searchTerms = searchTerms.Remove(searchTerms.Length - 1);

            // QoL thing - if user inputs 0 for upper ilvl, treat it as if there is no upper bound
            if (upperIlevel == 0)
                upperIlevel = defaultUpperIlevel;

            // get count of items & send list as embed
            var apiResponse = await MarketService.QueryXivapiWithStringAndILevels(searchTerms, lowerIlevel, upperIlevel);
            if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus) && apiResponse == MarketAPIRequestFailureStatus.APIFailure)
            {
                await ReplyAsync("API failure");
                return;
            }

            EmbedBuilder searchResultsEmbedBuilder = new EmbedBuilder();
            StringBuilder itemField1Builder = new StringBuilder();
            StringBuilder itemField2Builder = new StringBuilder();

            index = 0;
            foreach (var item in apiResponse.Results)
            {
                if (index % 2 == 0 && itemField1Builder.Length < 1000)
                    itemField1Builder.AppendLine(item.Name);
                if (index % 2 != 0 && itemField2Builder.Length < 1000)
                    itemField2Builder.AppendLine(item.Name);

                index++;
            }

            searchResultsEmbedBuilder.AddField("Names", itemField1Builder, true);
            if (itemField2Builder.Length > 0) // only add second field if more than 1 item was found
                searchResultsEmbedBuilder.AddField("Names", itemField2Builder, true);
            searchResultsEmbedBuilder.Color = Color.Blue;


            double estimatedTime = (apiResponse.Results.Count * 0.75) + 8; // 0.75 seconds per item approximately, with ~8s being initial processing time

            var resultcount = $"{apiResponse.Results.Count}";
            if (apiResponse.Results.Count == 100)
                resultcount = "100+";

            var waitMsg = await ReplyAsync($"We found {resultcount} items - processing them now. This should take about {estimatedTime} seconds.", false, searchResultsEmbedBuilder.Build());

            // show user that the bot is processing
            await Context.Channel.TriggerTypingAsync();

            // get deals & send them as embed
            var deals = await MarketService.GetBestDealsForSearchTerms(searchTerms, lowerIlevel, upperIlevel, server);

            // delete wait msg
            await waitMsg.DeleteAsync();

            if (deals.Count == 0)
            {
                await ReplyAsync("No deals were found under those conditions.");
                return;
            }

            EmbedBuilder dealsEmbedBuilder = new EmbedBuilder();

            foreach (var item in deals.Take(25))
            {
                StringBuilder dealFieldNameBuilder = new StringBuilder();
                dealFieldNameBuilder.Append($"{item.Name}");
                if (item.IsHQ)
                    dealFieldNameBuilder.Append(" - HQ");
                else
                    dealFieldNameBuilder.Append(" - NQ");

                StringBuilder dealFieldContentsBuilder = new StringBuilder();
                dealFieldContentsBuilder.AppendLine($"Avg Listed Price: {item.AvgMarketPrice}");
                dealFieldContentsBuilder.AppendLine($"Avg Sale Price: {item.AvgSalePrice}");
                dealFieldContentsBuilder.AppendLine($"Differential: {item.Differential}%");
                dealFieldContentsBuilder.AppendLine($"Number of sales: {item.NumRecentSales}");
                if (item.NumRecentSales >= 20)
                    dealFieldContentsBuilder.AppendLine("+");

                dealsEmbedBuilder.AddField(dealFieldNameBuilder.ToString(), dealFieldContentsBuilder.ToString(), true);
            }

            StringBuilder embedNameBuilder = new StringBuilder();
            embedNameBuilder.Append($"Potential deals");
            if (server != null)
                embedNameBuilder.Append($" on {server}");

            dealsEmbedBuilder.Author = new EmbedAuthorBuilder()
            {
                Name = embedNameBuilder.ToString()
            };
            dealsEmbedBuilder.Color = Color.Blue;

            await ReplyAsync(null, false, dealsEmbedBuilder.Build());
        }


        // should be able to accept inputs in any order - if two values are provided, they will be treated as minilvl and maxilvl respectively
        [Command("market exchange", RunMode = RunMode.Async)]
        [Summary("Get best items to spend your tomes/seals on")]
        [Example("market exchange {currency} (server) - defaults to Gilgamesh")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketGetBestCurrencyExchangesAsync([Remainder] string inputs = null)
        {
            if (inputs == null || !inputs.Any())
            {
                StringBuilder categoryListBuilder = new StringBuilder();
                categoryListBuilder.AppendLine("These are the categories you can check:");

                categoryListBuilder.AppendLine("gc - grand company seals");
                categoryListBuilder.AppendLine("poetics - i380 crafter mats, more later maybe");
                categoryListBuilder.AppendLine("gemstones - bicolor gemstones from fates");
                categoryListBuilder.AppendLine("nuts - sacks of nuts from hunts :peanut:");
                categoryListBuilder.AppendLine("wgs - White Gatherer Scrip items");
                categoryListBuilder.AppendLine("wcs - White Crafter Scrip items");

                await ReplyAsync(categoryListBuilder.ToString());
                return;
            }

            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            inputs = inputs.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // check if the API is operational, handle it if it's not
            var apiStatus = await MarketService.GetCompanionApiStatus();
            if (apiStatus != MarketAPIRequestFailureStatus.OK)
            {
                string apiStatusHumanResponse = "";

                if (apiStatus == MarketAPIRequestFailureStatus.NotLoggedIn)
                    apiStatusHumanResponse =
                        $"Not logged in to Companion API. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
                if (apiStatus == MarketAPIRequestFailureStatus.UnderMaintenance)
                    apiStatusHumanResponse = "SE's API is down for maintenance.";
                if (apiStatus == MarketAPIRequestFailureStatus.AccessDenied)
                    apiStatusHumanResponse =
                        $"Access denied. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
                if (apiStatus == MarketAPIRequestFailureStatus.ServiceUnavailable ||
                    apiStatus == MarketAPIRequestFailureStatus.APIFailure)
                    apiStatusHumanResponse =
                        $"Something went wrong. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";

                await ReplyAsync(apiStatusHumanResponse);
                return;
            }

            // try to get server name from the given text
            var server = MarketService.ServerList.Where(inputs.Contains).FirstOrDefault();
            // if server's not null, remove server name from the text
            if (server != null)
                inputs = inputs.Replace($"{server}", "").Trim();

            string category = inputs;

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // grab data from api
            var currencyDeals = await MarketService.GetBestCurrencyExchange(category, server);

            // keep items that are actively selling, and order by value ratio to put the best stuff to sell on top
            currencyDeals = currencyDeals.Where(x => x.NumRecentSales > 5).OrderByDescending(x => x.ValueRatio).ToList();

            // catch if the user didn't send a good category
            if (currencyDeals.Count == 0 || !currencyDeals.Any())
            {
                await ReplyAsync("You didn't input an existing category. Run the command by itself to get the categories this command can take.");
                return;
            }

            EmbedBuilder dealsEmbedBuilder = new EmbedBuilder();

            foreach (var item in currencyDeals.Take(8))
            {
                StringBuilder dealFieldNameBuilder = new StringBuilder();
                dealFieldNameBuilder.Append($"{item.Name}");

                StringBuilder dealFieldContentsBuilder = new StringBuilder();
                dealFieldContentsBuilder.AppendLine($"Avg Listed Price: {item.AvgMarketPrice}");
                dealFieldContentsBuilder.AppendLine($"Avg Sale Price: {item.AvgSalePrice}");
                dealFieldContentsBuilder.AppendLine($"Currency cost: {item.CurrencyCost}");
                dealFieldContentsBuilder.AppendLine($"Value ratio: {item.ValueRatio:0.000} gil/c");

                dealFieldContentsBuilder.Append($"Recent sales: {item.NumRecentSales}");
                if (item.NumRecentSales >= 20)
                    dealFieldContentsBuilder.AppendLine("+");
                else
                    dealFieldContentsBuilder.AppendLine("");

                if (item.VendorLocation != null)
                    dealFieldContentsBuilder.AppendLine($"Location: {item.VendorLocation}");

                dealsEmbedBuilder.AddField(dealFieldNameBuilder.ToString(), dealFieldContentsBuilder.ToString(), true);
            }


            // build author stuff 

            StringBuilder embedNameBuilder = new StringBuilder();
            embedNameBuilder.Append($"{category}");
            if (server != null)
                embedNameBuilder.Append($" on {server}");

            var authorurl = "";

            switch (category)
            {
                case "gc":
                    authorurl = "https://xivapi.com/i/065000/065004.png";
                    break;
                case "poetics":
                    authorurl = "https://xivapi.com/i/065000/065023.png";
                    break;
                case "gemstones":
                    authorurl = "https://xivapi.com/i/065000/065071.png";
                    break;
                case "nuts":
                    authorurl = "https://xivapi.com/i/065000/065068.png";
                    break;
                case "wgs":
                    authorurl = "https://xivapi.com/i/065000/065069.png";
                    break;
                case "wcs":
                    authorurl = "https://xivapi.com/i/065000/065070.png";
                    break;
            }

            dealsEmbedBuilder.Author = new EmbedAuthorBuilder()
            {
                Name = embedNameBuilder.ToString(),
                IconUrl = authorurl
            };
            dealsEmbedBuilder.Color = Color.Blue;

            await ReplyAsync("Items are sorted in descending order by their value ratio - items that are better to sell are at the top.", false, dealsEmbedBuilder.Build());
        }



        // interactive user selection prompt - each item in the passed collection gets listed out with an emoji
            // user selects an emoji, and the handlecallback function is run with the corresponding item ID as its parameter
            // it's expected that this function will be the last call in a function before that terminates, and that the callback function
            // will re-run the function with the user-selected data
            // optional server parameter to preserve server filter option
            private async Task InteractiveUserSelectItem(List<ItemSearchResultModel> itemsList, string functionToCall, string server = null)
        {
            string[] numbers = new[] { "0⃣", "1⃣", "2⃣", "3⃣", "4⃣", "5⃣", "6⃣", "7⃣", "8⃣", "9⃣" };
            var numberEmojis = new List<Emoji>();

            EmbedBuilder embedBuilder = new EmbedBuilder();
            StringBuilder stringBuilder = new StringBuilder();

            // add the number of emojis we need to the emojis list, and build our string-list of search results
            for (int i = 0; i < itemsList.Count && i < numbers.Length; i++)
            {
                numberEmojis.Add(new Emoji(numbers[i]));
                // get key for this dictionaryentry at index
                var itemsDictionaryName = itemsList[i].Name;

                stringBuilder.AppendLine($"{numbers[i]} - {itemsDictionaryName}");
            }

            embedBuilder.WithDescription(stringBuilder.ToString());
            embedBuilder.WithColor(Color.Blue);

            // build a message and add reactions to it
            // reactions will be watched, and the one selected will fire the HandleFindTagReactionResult method, passing
            // that reaction's corresponding tagname and the function passed into this parameter
            var messageContents = new ReactionCallbackData("Did you mean... ", embedBuilder.Build());
            for (int i = 0; i < itemsList.Count; i++)
            {
                var counter = i;
                var itemsDictionaryID = itemsList[i].ID;
                messageContents.AddCallBack(numberEmojis[counter], async (c, r) => HandleInteractiveUserSelectCallback(itemsDictionaryID, functionToCall, server));

            }

            var message = await InlineReactionReplyAsync(messageContents);

            // add calling user and searchResults embed to a dict as a pair
            // this way we can hold multiple users' reaction messages and operate on them separately
            _dictFindItemUserEmbedPairs.Add(Context.User, message);
        }


        // this might get modified to accept a 'function' param that will run in a switch:case to
        // select what calling function this callback handler should re-run with the user-selected data
        // optional server parameter to preserve server filter option
        private async Task HandleInteractiveUserSelectCallback(int itemId, string function, string server = null)
        {
            // grab the calling user's pair of calling user & searchResults embed
            var dictEntry = _dictFindItemUserEmbedPairs.FirstOrDefault(x => x.Key == Context.User);

            // delete the calling user's searchResults embed, if it exists
            if (dictEntry.Key != null)
                await dictEntry.Value.DeleteAsync();

            switch (function)
            {
                case "market":
                    await MarketGetItemPriceAsync($"{server} {itemId}");
                    break;
                case "history":
                    await MarketGetItemHistoryAsync($"{server} {itemId}");
                    break;
                case "analyze":
                    await MarketAnalyzeItemAsync($"{server} {itemId}");
                    break;
            }
        }
    }
}