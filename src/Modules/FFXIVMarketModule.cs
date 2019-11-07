using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        [Alias("mbp")]
        [Summary("Get prices for an item - takes item name or item id")]
        [Example("market price (server) {name/id}")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketGetItemPriceAsync([Remainder] string inputs)
        {
            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            inputs = inputs.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // try to get server name from the given text
            var pattern = new Regex(@"\W");
            var server = pattern.Split(inputs).FirstOrDefault(x => MarketService.ServerList.Contains(x));

            // if server's not null, user provided a specific server
            // remove server name from the text
            if (server != null)
                inputs = ReplaceWholeWord(inputs, $"{server}", "").Trim();

            // set datacenter - if server param was passed and that server's in primal, we can use that, too
            var datacenter = Datacenter.Aether;
            // aether is default, but aether dc could be passed by user or by user-interact function, so handle it just in case
            // using regex to match whole words, so we don't trigger this check with things like 'aethersand'
            if (Regex.Match(inputs, @"\baether\b", RegexOptions.IgnoreCase).Success || server != null && MarketService.ServerList_Aether.Contains(server))
            {
                datacenter = Datacenter.Aether;
                if (inputs.Contains("aether"))
                    inputs = ReplaceWholeWord(inputs, "aether", "").Trim();
            }
            if (Regex.Match(inputs, @"\bprimal\b", RegexOptions.IgnoreCase).Success || server != null && MarketService.ServerList_Primal.Contains(server))
            {
                datacenter = Datacenter.Primal;
                if (inputs.Contains("primal")) // second check here since getting datacenter by world means inputs wouldn't contain a dc
                    inputs = ReplaceWholeWord(inputs, "primal", "");
            }

            // check if the API is operational, handle it if it's not
            var apiStatus = await MarketService.GetCompanionApiStatus(server);
            if (apiStatus != MarketAPIRequestFailureStatus.OK)
            {
                string apiStatusHumanResponse = await GetCustomAPIStatusHumanResponse(apiStatus);

                await ReplyAsync(apiStatusHumanResponse);
                return;
            }

            // declare vars - these will get populated eventually
            int itemId;
            string itemName = "";
            string itemIconUrl = "";

            // try to see if the given text is an item ID
            var searchTermIsItemId = int.TryParse(inputs, out itemId);


            // if user passed a itemname, get corresponding itemid. Does any of the following:

            // * call the interactive user select function and terminates, if there are multiple search results
            //      in this case, the interactive user select function will re-run the function and pass a single item ID

            // * assigns itemid to search result, if there was only one search result, and then continue the function
            if (!searchTermIsItemId)
            {
                // response is either a ordereddictionary of keyvaluepairs, or null
                var itemIdQueryResult = await MarketService.SearchForItemByName(inputs);

                if (itemIdQueryResult == null)
                {
                    await ReplyAsync("Something is wrong with XIVAPI. Try using Garlandtools to get the item's ID and use that instead.");
                    return;
                }

                // no results
                if (itemIdQueryResult.Count == 0)
                {
                    await ReplyAsync("No tradable items found. Try to expand your search terms, or check for typos. ");
                    return;
                }

                // too many results
                if (itemIdQueryResult.Count > 15)
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
                if (itemIdQueryResult.Count > 1 && itemIdQueryResult.Count < 15) 
                {
                    await InteractiveUserSelectItem(itemIdQueryResult, "market", datacenter, server);
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
            var marketQueryResults = await MarketService.GetMarketListings(itemName, itemId, datacenter, server);

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
        [Alias("mbh")]
        [Summary("Get history for an item - takes item name or item id")]
        [Example("market price (server) {name/id}")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketGetItemHistoryAsync([Remainder] string inputs)
        {
            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            inputs = inputs.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // try to get server name from the given text
            var pattern = new Regex(@"\W");
            var server = pattern.Split(inputs).FirstOrDefault(x => MarketService.ServerList.Contains(x));

            // if server's not null, user provided a specific server
            // remove server name from the text
            if (server != null)
                inputs = ReplaceWholeWord(inputs, $"{server}", "").Trim();

            // set datacenter - if server param was passed and that server's in primal, we can use that, too
            var datacenter = Datacenter.Aether;
            // aether is default, but aether dc could be passed by user or by user-interact function, so handle it just in case
            // using regex to match whole words, so we don't trigger this check with things like 'aethersand'
            if (Regex.Match(inputs, @"\baether\b", RegexOptions.IgnoreCase).Success || server != null && MarketService.ServerList_Aether.Contains(server))
            {
                datacenter = Datacenter.Aether;
                if (inputs.Contains("aether"))
                    inputs = ReplaceWholeWord(inputs, "aether", "").Trim();
            }
            if (Regex.Match(inputs, @"\bprimal\b", RegexOptions.IgnoreCase).Success || server != null && MarketService.ServerList_Primal.Contains(server))
            {
                datacenter = Datacenter.Primal;
                if (inputs.Contains("primal")) // second check here since getting datacenter by world means inputs wouldn't contain a dc
                    inputs = ReplaceWholeWord(inputs, "primal", "");
            }

            // check if the API is operational, handle it if it's not
            var apiStatus = await MarketService.GetCompanionApiStatus(server);
            if (apiStatus != MarketAPIRequestFailureStatus.OK)
            {
                string apiStatusHumanResponse = await GetCustomAPIStatusHumanResponse(apiStatus);

                await ReplyAsync(apiStatusHumanResponse);
                return;
            }

            // declare vars - both of these will get populated eventually
            int itemId;
            string itemName = "";
            string itemIconUrl = "";

            // try to see if the given text is an item ID
            var searchTermIsItemId = int.TryParse(inputs, out itemId);


            // if user passed a itemname, get corresponding itemid. Does any of the following:

            // * call the interactive user select function and terminates, if there are multiple search results
            //      in this case, the interactive user select function will re-run the function and pass a single item ID

            // * assigns itemid to search result, if there was only one search result, and then continue the function
            if (!searchTermIsItemId)
            {
                // response is either a ordereddictionary of keyvaluepairs, or null
                var itemIdQueryResult = await MarketService.SearchForItemByName(inputs);

                if (itemIdQueryResult == null)
                {
                    await ReplyAsync("Something is wrong with XIVAPI. Try using Garlandtools to get the item's ID and use that instead.");
                    return;
                }

                // no results
                if (itemIdQueryResult.Count == 0)
                {
                    await ReplyAsync("No tradable items found. Try to expand your search terms, or check for typos.");
                    return;
                }

                // too many results
                if (itemIdQueryResult.Count > 15)
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
                    await InteractiveUserSelectItem(itemIdQueryResult, "history", datacenter, server);
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
            var historyQueryResults = await MarketService.GetHistoryListings(itemName, itemId, datacenter, server);

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
        [Alias("mba")]
        [Summary("Get market analysis for an item")]
        [Example("market analyze {name/id} (server) - defaults to Gilgamesh")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketAnalyzeItemAsync([Remainder] string inputs)
        {
            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            inputs = inputs.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // try to get server name from the given text
            var pattern = new Regex(@"\W");
            var server = pattern.Split(inputs).FirstOrDefault(x => MarketService.ServerList.Contains(x));

            // if server's not null, user provided a specific server
            // remove server name from the text
            if (server != null)
                inputs = ReplaceWholeWord(inputs, $"{server}", "").Trim();

            // FIX

            // set datacenter - if server param was passed and that server's in primal, we can use that, too
            var datacenter = Datacenter.Aether;
            // aether is default, but aether dc could be passed by user or by user-interact function, so handle it just in case
            // using regex to match whole words, so we don't trigger this check with things like 'aethersand'
            if (Regex.Match(inputs, @"\baether\b", RegexOptions.IgnoreCase).Success || server != null && MarketService.ServerList_Aether.Contains(server))
            {
                datacenter = Datacenter.Aether;
                if (inputs.Contains("aether"))
                    inputs = ReplaceWholeWord(inputs, "aether", "").Trim();
            }
            if (Regex.Match(inputs, @"\bprimal\b", RegexOptions.IgnoreCase).Success || server != null && MarketService.ServerList_Primal.Contains(server))
            {
                datacenter = Datacenter.Primal;
                if (inputs.Contains("primal")) // second check here since getting datacenter by world means inputs wouldn't contain a dc
                    inputs = ReplaceWholeWord(inputs, "primal", "");
            }

            // check if the API is operational, handle it if it's not
            var apiStatus = await MarketService.GetCompanionApiStatus(server);
            if (apiStatus != MarketAPIRequestFailureStatus.OK)
            {
                string apiStatusHumanResponse = await GetCustomAPIStatusHumanResponse(apiStatus);

                await ReplyAsync(apiStatusHumanResponse);
                return;
            }

            // declare vars - both of these will get populated eventually
            int itemId;
            string itemName = "";
            string itemIconUrl = "";

            // try to see if the given text is an item ID
            var searchTermIsItemId = int.TryParse(inputs, out itemId);


            // if user passed a itemname, get corresponding itemid. Does any of the following:

            // * call the interactive user select function and terminates, if there are multiple search results
            //      in this case, the interactive user select function will re-run the function and pass a single item ID

            // * assigns itemid to search result, if there was only one search result, and then continue the function
            if (!searchTermIsItemId)
            {
                // remove any trailing spaces
                if (inputs.EndsWith(" "))
                    inputs = inputs.Remove(inputs.Length - 1);

                // response is either a ordereddictionary of keyvaluepairs, or null
                var itemIdQueryResult = await MarketService.SearchForItemByName(inputs);

                if (itemIdQueryResult == null)
                {
                    await ReplyAsync("Something is wrong with XIVAPI. Try using Garlandtools to get the item's ID and use that instead.");
                    return;
                }

                // no results
                if (itemIdQueryResult.Count == 0)
                {
                    await ReplyAsync("No tradable items found. Try to expand your search terms, or check for typos.");
                    return;
                }

                // too many results
                if (itemIdQueryResult.Count > 15)
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
                    await InteractiveUserSelectItem(itemIdQueryResult, "analyze", datacenter, server);
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
            var marketAnalysis = await MarketService.CreateMarketAnalysis(itemName, itemId, datacenter, server);
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
                hqFieldBuilder.Append($"Number of sales: {hqMarketAnalysis.NumRecentSales}");
                if (hqMarketAnalysis.NumRecentSales >= 20)
                    hqFieldBuilder.AppendLine("+");
                else
                    hqFieldBuilder.AppendLine("");

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
        [Command("market exchange", RunMode = RunMode.Async)]
        [Alias("mbe")]
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
                categoryListBuilder.AppendLine("ygs - Yellow Gatherer Scrip items");
                categoryListBuilder.AppendLine("ycs - Yellow Crafter Scrip items");
                categoryListBuilder.AppendLine("goetia - goetia mats");

                await ReplyAsync(categoryListBuilder.ToString());
                return;
            }

            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            inputs = inputs.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // try to get server name from the given text
            var pattern = new Regex(@"\W");
            var server = pattern.Split(inputs).FirstOrDefault(x => MarketService.ServerList.Contains(x));

            // if server's not null, user provided a specific server
            // remove server name from the text
            if (server != null)
                inputs = ReplaceWholeWord(inputs, $"{server}", "").Trim();

            // set datacenter - if server param was passed and that server's in primal, we can use that, too
            var datacenter = Datacenter.Aether;
            // aether is default, but aether dc could be passed by user or by user-interact function, so handle it just in case
            // using regex to match whole words, so we don't trigger this check with things like 'aethersand'
            if (Regex.Match(inputs, @"\baether\b", RegexOptions.IgnoreCase).Success || server != null && MarketService.ServerList_Aether.Contains(server))
            {
                datacenter = Datacenter.Aether;
                if (inputs.Contains("aether"))
                    inputs = ReplaceWholeWord(inputs, "aether", "").Trim();
            }
            if (Regex.Match(inputs, @"\bprimal\b", RegexOptions.IgnoreCase).Success || server != null && MarketService.ServerList_Primal.Contains(server))
            {
                datacenter = Datacenter.Primal;
                if (inputs.Contains("primal")) // second check here since getting datacenter by world means inputs wouldn't contain a dc
                    inputs = ReplaceWholeWord(inputs, "primal", "");
            }

            // check if the API is operational, handle it if it's not
            var apiStatus = await MarketService.GetCompanionApiStatus(server);
            if (apiStatus != MarketAPIRequestFailureStatus.OK)
            {
                string apiStatusHumanResponse = await GetCustomAPIStatusHumanResponse(apiStatus);

                await ReplyAsync(apiStatusHumanResponse);
                return;
            }

            string category = inputs;

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // grab data from api
            var currencyDeals = await MarketService.GetBestCurrencyExchange(category, datacenter, server);

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
                case "ygs":
                    authorurl = "https://xivapi.com/i/065000/065043.png";
                    break;
                case "ycs":
                    authorurl = "https://xivapi.com/i/065000/065044.png";
                    break;
                case "goetia":
                    authorurl = "https://xivapi.com/i/065000/065066.png";
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


        [Command("market order", RunMode = RunMode.Async)]
        [Alias("mbo")]
        [Summary("Build a list of the lowest market prices for items, ordered by server")]
        [Example("market order {itemname:count, itemname:count, etc...}")]
        public async Task MarketCrossWorldPurchaseOrderAsync([Remainder] string inputs = null)
        {
            if (inputs == null || !inputs.Any())
            {
                // let the user know they fucked up, or don't
                return;
            }

            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            inputs = inputs.ToLower();

            // set datacenter - if server param was passed and that server's in primal, we can use that, too
            var datacenter = Datacenter.Aether;
            // aether is default, but aether dc could be passed by user or by user-interact function, so handle it just in case
            // using regex to match whole words, so we don't trigger this check with things like 'aethersand'
            if (Regex.Match(inputs, @"\baether\b", RegexOptions.IgnoreCase).Success)
            {
                datacenter = Datacenter.Aether;
                if (inputs.Contains("aether"))
                    inputs = ReplaceWholeWord(inputs, "aether", "").Trim();
            }
            if (Regex.Match(inputs, @"\bprimal\b", RegexOptions.IgnoreCase).Success)
            {
                datacenter = Datacenter.Primal;
                if (inputs.Contains("primal")) // second check here since getting datacenter by world means inputs wouldn't contain a dc
                    inputs = ReplaceWholeWord(inputs, "primal", "");
            }

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // check if the API is operational, handle it if it's not
            var apiStatus = await MarketService.GetCompanionApiStatus("gilgamesh"); // order doesn't use a specific server, so just picking one for now
            if (apiStatus != MarketAPIRequestFailureStatus.OK)
            {
                string apiStatusHumanResponse = await GetCustomAPIStatusHumanResponse(apiStatus);

                await ReplyAsync(apiStatusHumanResponse);
                return;
            }

            // split each item:count pairing
            var inputList = inputs.Split(", ");

            
            var itemsList = new List<MarketItemXWOrderModel>();
            foreach (var input in inputList)
            {
                var inputSplit = input.Split(":");

                var itemName = inputSplit[0];
                var NeededQuantity = int.Parse(inputSplit[1]);
                var itemShouldBeHQ = false;

                // replace hq text in itemname var if it exists, and set shouldbehq flag to true
                if (itemName.Contains("hq"))
                {
                    itemShouldBeHQ = true;
                    itemName = itemName.Replace("hq", "").Trim();
                }
                
                itemsList.Add(new MarketItemXWOrderModel(){Name = itemName, NeededQuantity = NeededQuantity, ShouldBeHQ = itemShouldBeHQ });
            }

            var plsWaitMsg = await ReplyAsync("This could take quite a while. Please hang tight.");

            var timer = Stopwatch.StartNew();

            var results = await MarketService.GetMarketCrossworldPurchaseOrder(itemsList, datacenter);

            timer.Stop();

            // sort the results into different lists, grouped by server
            var purchaseOrder = results.GroupBy(x => x.Server).ToList();

            // to get the overall cost of the order
            var totalCost = 0;
            var purchaseOrderEmbed = new EmbedBuilder();

            foreach (var server in purchaseOrder)
            {
                StringBuilder purchaseOrderServerStringBuilder = new StringBuilder();
                StringBuilder purchaseOrderServerOverflowStringBuilder = new StringBuilder(); // in case the first field goes over 1024 chars
                // convert this server IGrouping to a list so we can access its values easily
                var serverList = server.ToList();

                // build this server's item list 
                foreach (var item in serverList)
                {
                    var quality = item.IsHQ ? "(HQ)" : "";
                    var entry = $"{item.Name} {quality} - {item.Quantity} for {item.Price} (total: {item.Quantity * item.Price})";

                    if (purchaseOrderServerStringBuilder.Length + entry.Length < 1024)
                        purchaseOrderServerStringBuilder.AppendLine(entry);
                    else
                        purchaseOrderServerOverflowStringBuilder.AppendLine(entry);
                    
                    totalCost += item.Price * item.Quantity;
                }

                var field = new EmbedFieldBuilder();
                var fieldOverflow = new EmbedFieldBuilder(); // in case the first field goes over 1024 chars

                // regular field
                field.Name = $"{serverList[0].Server}";
                field.Value = purchaseOrderServerStringBuilder.ToString();
                purchaseOrderEmbed.AddField(field);

                // overflow field, only if applicable
                if (purchaseOrderServerOverflowStringBuilder.Length > 0)
                {
                    fieldOverflow.Name = $"{serverList[0].Server} (2)";
                    fieldOverflow.Value = purchaseOrderServerOverflowStringBuilder.ToString();
                    purchaseOrderEmbed.AddField(fieldOverflow);
                }

                // embed title
                purchaseOrderEmbed.Title = $"Total cost: {totalCost}";

            }

            purchaseOrderEmbed.WithFooter($"Took {timer.ElapsedMilliseconds} ms");

            await plsWaitMsg.DeleteAsync();

            await ReplyAsync("If any items are missing from the list, it's likely they'd take too many purchases to fulfill easily.", false, purchaseOrderEmbed.Build());
        }


        [Command("market login", RunMode = RunMode.Async)]
        [Alias("mbl")]
        [Summary("Check status of servers & attempt to log in to any that aren't logged in")]
        public async Task MarketServerStatusAsync()
        {
            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            var allServersList = new List<string>();
            allServersList.AddRange(MarketService.ServerList_Aether);
            allServersList.AddRange(MarketService.ServerList_Primal);

            var serverStatusList = new List<CompanionAPILoginStatusModel>();

            var tasks = Task.Run(() => Parallel.ForEach(allServersList, server =>
            {
                var serverStatus = new CompanionAPILoginStatusModel();
                serverStatus.ServerName = server;

                var serverStatusQueryResult = MarketService.GetCompanionApiStatus(server).Result;

                if (serverStatusQueryResult == MarketAPIRequestFailureStatus.OK)
                    serverStatus.LoginStatus = true;
                else
                    serverStatus.LoginStatus = false;

                serverStatusList.Add(serverStatus);
            }));

            await Task.WhenAll(tasks);

            var serverStatusFailedList = serverStatusList.Where(x => x.LoginStatus == false).ToList();

            // function ends here if all servers are logged in
            if (serverStatusFailedList.Any() == false)
            {
                await ReplyAsync("All servers are logged in.");
                return;
            }

            // continue and handle if any servers failed

            await ReplyAsync($"{serverStatusFailedList.Count} server(s) aren't logged in at the moment. I will try to log into them now.");
            var statusMsg = await ReplyAsync("Status: Waiting...");


            var currentCount = 0;
            foreach (var server in serverStatusFailedList)
            {
                currentCount++;

                await Context.Channel.TriggerTypingAsync();

                await statusMsg.ModifyAsync(m => m.Content = $"Status: Logging into {server.ServerName} ({currentCount} / {serverStatusFailedList.Count})...");
                var loggedIn = await MarketService.LoginToCompanionAPI(server.ServerName);

                if (loggedIn)
                    await statusMsg.ModifyAsync(m => m.Content = $"Successfully logged in to {server.ServerName} ({currentCount} / {serverStatusFailedList.Count}).");
                else
                    await statusMsg.ModifyAsync(m => m.Content = $"Could not log in to {server.ServerName} ({currentCount} / {serverStatusFailedList.Count}).");

                await Task.Delay(1000);
            }

            await statusMsg.ModifyAsync(m => m.Content = $"Logins complete.");
        }


        // interactive user selection prompt - each item in the passed collection gets listed out with an emoji
            // user selects an emoji, and the handlecallback function is run with the corresponding item ID as its parameter
            // it's expected that this function will be the last call in a function before that terminates, and that the callback function
            // will re-run the function with the user-selected data
            // optional server parameter to preserve server filter option
            private async Task InteractiveUserSelectItem(List<ItemSearchResultModel> itemsList, string functionToCall, Datacenter datacenter, string server = null)
        {
            string[] numbers = new[] { "0⃣", "1⃣", "2⃣", "3⃣", "4⃣", "5⃣", "6⃣", "7⃣", "8⃣", "9⃣", "🇦", "🇧", "🇨", "🇩", "🇪" };
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
                var itemId = itemsList[i].ID;
                messageContents.AddCallBack(numberEmojis[counter], async (c, r) => HandleInteractiveUserSelectCallback(itemId, functionToCall, datacenter, server));

            }

            var message = await InlineReactionReplyAsync(messageContents);

            // add calling user and searchResults embed to a dict as a pair
            // this way we can hold multiple users' reaction messages and operate on them separately
            _dictFindItemUserEmbedPairs.Add(Context.User, message);
        }


        // this might get modified to accept a 'function' param that will run in a switch:case to
        // select what calling function this callback handler should re-run with the user-selected data
        // optional server parameter to preserve server filter option
        private async Task HandleInteractiveUserSelectCallback(int itemId, string function, Datacenter datacenter, string server = null)
        {
            // grab the calling user's pair of calling user & searchResults embed
            var dictEntry = _dictFindItemUserEmbedPairs.FirstOrDefault(x => x.Key == Context.User);

            // delete the calling user's searchResults embed, if it exists
            if (dictEntry.Key != null)
                await dictEntry.Value.DeleteAsync();

            switch (function)
            {
                case "market":
                    await MarketGetItemPriceAsync($"{server} {datacenter} {itemId}");
                    break;
                case "history":
                    await MarketGetItemHistoryAsync($"{server} {datacenter} {itemId}");
                    break;
                case "analyze":
                    await MarketAnalyzeItemAsync($"{server} {datacenter} {itemId}");
                    break;
            }
        }

        private async Task<string> GetCustomAPIStatusHumanResponse(MarketAPIRequestFailureStatus status)
        {
            string apiStatusHumanResponse = "";

            if (status == MarketAPIRequestFailureStatus.NotLoggedIn)
                apiStatusHumanResponse = $"Not logged in to Companion API. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
            if (status == MarketAPIRequestFailureStatus.UnderMaintenance)
                apiStatusHumanResponse = "SE's API is down for maintenance.";
            if (status == MarketAPIRequestFailureStatus.AccessDenied)
                apiStatusHumanResponse = $"Access denied. Contact {Context.Guild.GetUser(110866678161645568).Mention}.";
            if (status == MarketAPIRequestFailureStatus.ServiceUnavailable || status == MarketAPIRequestFailureStatus.APIFailure)
                apiStatusHumanResponse = $"Something went wrong (API failure). Contact {Context.Guild.GetUser(110866678161645568).Mention}.";

            return apiStatusHumanResponse;
        }

        private string ReplaceWholeWord(string original, string wordToFind, string replacement, RegexOptions regexOptions = RegexOptions.None)
        {
            string pattern = String.Format(@"\b{0}\b", wordToFind);
            string replaced = Regex.Replace(original, pattern, replacement, regexOptions).Trim(); // remove the unwanted word
            string ret = Regex.Replace(replaced, @"\s+", " "); // clear out any excess whitespace
            return ret;
        }
    }
}