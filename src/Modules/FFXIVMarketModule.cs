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
                searchTerm = searchTerm.Replace($"{server} ", "");

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
                    await ReplyAsync($"Too many results found ({itemIdQueryResult.Count}). Try to narrow down your search terms, or use `.item search` to get your item's ID and use that instead.");
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
                searchTerm = searchTerm.Replace($"{server} ", "");

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
                    await ReplyAsync($"Too many results found ({itemIdQueryResult.Count}). Try to narrow down your search terms, or use `.item search` to get your item's ID and use that instead.");
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
        [Example("market analyze {name/id} (server)")]
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
                searchTerm = searchTerm.Replace($"{server}", "");

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
                    await ReplyAsync(
                        $"Too many results found ({itemIdQueryResult.Count}). Try to narrow down your search terms, or use `.item search` to get your item's ID and use that instead.");
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
            if (hqMarketAnalysis.numRecentSales != 0)
            {
                StringBuilder hqFieldBuilder = new StringBuilder();
                hqFieldBuilder.AppendLine($"Avg Market Price: {hqMarketAnalysis.AvgMarketPrice}");
                hqFieldBuilder.AppendLine($"Avg Sale Price: {hqMarketAnalysis.AvgSalePrice}");
                hqFieldBuilder.AppendLine($"Differential: {hqMarketAnalysis.Differential}");

                analysisEmbedBuilder.AddField("HQ", hqFieldBuilder.ToString());
            }
            // nq stuff - first line inline=true in case we had hq values
            StringBuilder nqFieldBuilder = new StringBuilder();
            nqFieldBuilder.AppendLine($"Avg Market Price: {nqMarketAnalysis.AvgMarketPrice}");
            nqFieldBuilder.AppendLine($"Avg Sale Price: {nqMarketAnalysis.AvgSalePrice}");
            nqFieldBuilder.AppendLine($"Differential: {nqMarketAnalysis.Differential}");

            analysisEmbedBuilder.AddField("NQ", nqFieldBuilder.ToString());

            analysisEmbedBuilder.Author = new EmbedAuthorBuilder()
            {
                Name = $"Market analysis for {itemName}",
            };
            analysisEmbedBuilder.ThumbnailUrl = itemIconUrl;
            analysisEmbedBuilder.Color = Color.Blue;

            await ReplyAsync(null, false, analysisEmbedBuilder.Build());
        }


        [Command("market deals", RunMode = RunMode.Async)]
        [Summary("Get market analyses for item categories")]
        [Example("market deals {categoryid} (minilvl) (maxilvl) (searchterms) (server)")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketGetDealsAsync(params string[] inputs)
        {
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
                int i = 0;
                foreach (var category in categories)
                {
                    // odd
                    if (i % 2 != 0)
                        categoryIDsField1Builder.AppendLine($"{category.Name} - {category.ID}");
                    // even
                    if (i % 2 == 0)
                        categoryIDsField2Builder.AppendLine($"{category.Name} - {category.ID}");
                    i++;
                }
                
                categoryIDsEmbedBuilder.AddField("Categories", categoryIDsField1Builder.ToString(), true);
                categoryIDsEmbedBuilder.AddField("Categories", categoryIDsField2Builder.ToString(), true);
                categoryIDsEmbedBuilder.Color = Color.Blue;

                await ReplyAsync(null, false, categoryIDsEmbedBuilder.Build());
                return;
            }

            // category must be a int
            if (inputs[0] != null && int.TryParse(inputs[0], out var tmp) == false)
            {
                await ReplyAsync("Invalid category input. Input must be a number.");
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

            // if list count is 1, we only got category ID, so add default lowerIlvl and upperIlvl
            if (integerInputsList.Count == 1)
            {
                integerInputsList.Add(defaultLowerIlevel);
                integerInputsList.Add(defaultUpperIlevel);
            }
            // if list count is 2, we only got lower ilvl, so add upper ilvl 
            if (integerInputsList.Count == 2)
            {
                integerInputsList.Add(defaultUpperIlevel);
            }

            int cid = integerInputsList[0];
            int lowerIlevel = integerInputsList[1];
            int upperIlevel = integerInputsList[2];


            string searchTerms = null;
            string server = defaultServer;
            
            // get server from combined inputs if it exists
            server = MarketService.ServerList.Where(combinedStringInputs.Contains).FirstOrDefault();
            // remove server text from combined inputs if server text exists
            if (server != null)
                combinedStringInputs = combinedStringInputs.Replace($"{server}", "");

            // assign remaining text to search terms
            searchTerms = combinedStringInputs;

            // remove any trailing spaces
            if (searchTerms.EndsWith(" "))
                searchTerms = searchTerms.Remove(searchTerms.Length - 1);

            // QoL thing - if user inputs 0 for upper ilvl, treat it as if there is no upper bound
            if (upperIlevel == 0)
                upperIlevel = defaultUpperIlevel;

            // get count of items & send list as embed
            var apiResponse = await MarketService.QueryXivapiForItemIdsGivenCategoryId(cid, lowerIlevel, upperIlevel, searchTerms);
            if (apiResponse.GetType() == typeof(MarketAPIRequestFailureStatus) && apiResponse == MarketAPIRequestFailureStatus.APIFailure)
            {
                await ReplyAsync("API failure");
                return;
            }

            EmbedBuilder searchResultsEmbedBuilder = new EmbedBuilder();
            StringBuilder itemFieldBuilder = new StringBuilder();

            foreach (var item in apiResponse.Results)
            {
                itemFieldBuilder.AppendLine(item.Name);
            }

            searchResultsEmbedBuilder.AddField("Names", itemFieldBuilder);
            searchResultsEmbedBuilder.Color = Color.Blue;

            var waitMsg = await ReplyAsync($"We found {apiResponse.Results.Count} items, and we're going to start processing them now. This may take a while...", false, searchResultsEmbedBuilder.Build());

            // show user that the bot is processing
            await Context.Channel.TriggerTypingAsync();

            // get deals & send them as embed
            var deals = await MarketService.GetBestDealsFromCategory(cid, lowerIlevel, upperIlevel, server, searchTerms);

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
                dealFieldContentsBuilder.AppendLine($"Avg Market Price: {item.AvgMarketPrice}");
                dealFieldContentsBuilder.AppendLine($"Avg Sale Price: {item.AvgSalePrice}");
                dealFieldContentsBuilder.AppendLine($"Differential: {item.Differential}");

                dealsEmbedBuilder.AddField(dealFieldNameBuilder.ToString(), dealFieldContentsBuilder.ToString(), true);
            }

            dealsEmbedBuilder.Author = new EmbedAuthorBuilder()
            {
                Name = "Potential deals",
            };
            dealsEmbedBuilder.Color = Color.Blue;

            await ReplyAsync(null, false, dealsEmbedBuilder.Build());
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
                messageContents.AddCallBack(numberEmojis[counter], async (c, r) => await HandleInteractiveUserSelectCallback(itemsDictionaryID, functionToCall, server));

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
