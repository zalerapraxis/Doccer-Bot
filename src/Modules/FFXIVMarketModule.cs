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
using Doccer_Bot.Modules.Common;
using Doccer_Bot.Services;
using Flurl.Util;

namespace Doccer_Bot.Modules
{
    [Name("Market")]
    public class FFXIVMarketModule : InteractiveBase
    {
        public MarketService MarketService { get; set; }


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

                // various checks to see if inputs are messed up or no results found

                // api failure on our end
                if (itemIdQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemIdQueryResult == MarketAPIRequestFailureStatus.APIFailure)
                {
                    await ReplyAsync($"Request failed, try again. If this keeps happening, let {Context.Guild.GetUser(110866678161645568).Mention} know.");
                    return;
                }

                // no results
                if (itemIdQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemIdQueryResult.Count == MarketAPIRequestFailureStatus.NoResults)
                {
                    await ReplyAsync("No results found. Try to expand your search terms, or check for typos.");
                    return;
                }

                // if more than one result was found, send the results to the selection function to narrow it down to one
                // terminate this function, as the selection function will eventually re-call this method with a single result item
                if (itemIdQueryResult.Count > 1)
                {
                    await InteractiveUserSelectItem(itemIdQueryResult, "market", server);
                    return;
                }

                if (itemIdQueryResult.Count == 1)
                {
                    itemId = itemIdQueryResult[0];
                }
            }

            // get the item name & assign it
            var itemDetailsQueryResult = await MarketService.QueryXivapiWithItemId(itemId);

            // various checks to see if inputs are messed up or no results found

            // api failure on our end
            if (itemDetailsQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemDetailsQueryResult == MarketAPIRequestFailureStatus.APIFailure)
            {
                await ReplyAsync($"Request failed, try again. If this keeps happening, let {Context.Guild.GetUser(110866678161645568).Mention} know.");
                return;
            }

            // no results
            if (itemDetailsQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemDetailsQueryResult.Count == MarketAPIRequestFailureStatus.NoResults)
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
                    "No listings found for that item. Either nobody's selling it, or it's not marketable.");
                return;
            }

            // format market data & display

            var pages = new List<PaginatedMessage.Page>();

            var i = 0;
            var itemsPerPage = 12;

            // iterate through the market results, making a page for every (up to) itemsPerPage listings
            while (i < marketQueryResults.Count)
            {
                // pull up to 20 entries from the list, skipping any from previous iterations
                var currentPageMarketList = marketQueryResults.Skip(i).Take(itemsPerPage);

                StringBuilder sbListing = new StringBuilder();

                // build data for this page
                foreach (var listing in currentPageMarketList)
                {
                    sbListing.Append($"**{listing.Quantity}** ");

                    if (listing.IsHq)
                        sbListing.Append("**HQ** ");

                    if (listing.Quantity > 1)
                        // multiple units
                        sbListing.Append($"for {listing.CurrentPrice * listing.Quantity} (**{listing.CurrentPrice}** per unit) ");
                    else // single units
                        sbListing.Append($"for **{listing.CurrentPrice}** ");
                    sbListing.AppendLine();
                    if (server == null)
                        sbListing.AppendLine($"• Listed on **{listing.Server}**");
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

                // various checks to see if inputs are messed up or no results found

                // api failure on our end
                if (itemIdQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemIdQueryResult == MarketAPIRequestFailureStatus.APIFailure)
                {
                    await ReplyAsync($"Request failed, try again. If this keeps happening, let {Context.Guild.GetUser(110866678161645568).Mention} know.");
                    return;
                }

                // no results
                if (itemIdQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemIdQueryResult.Count == MarketAPIRequestFailureStatus.NoResults)
                {
                    await ReplyAsync("No results found. Try to expand your search terms, or check for typos.");
                    return;
                }

                // if more than one result was found, send the results to the selection function to narrow it down to one
                // terminate this function, as the selection function will eventually re-call this method with a single result item
                if (itemIdQueryResult.Count > 1)
                {
                    await InteractiveUserSelectItem(itemIdQueryResult, "history", server);
                    return;
                }

                if (itemIdQueryResult.Count == 1)
                {
                    itemId = itemIdQueryResult[0];
                }
            }

            // get the item name & assign it
            var itemDetailsQueryResult = await MarketService.QueryXivapiWithItemId(itemId);

            // various checks to see if inputs are messed up or no results found

            // api failure on our end
            if (itemDetailsQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemDetailsQueryResult == MarketAPIRequestFailureStatus.APIFailure)
            {
                await ReplyAsync($"Request failed, try again. If this keeps happening, let {Context.Guild.GetUser(110866678161645568).Mention} know.");
                return;
            }

            // no results
            if (itemDetailsQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemDetailsQueryResult.Count == MarketAPIRequestFailureStatus.NoResults)
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
            var itemsPerPage = 12;

            // iterate through the history results, making a page for every (up to) itemsPerPage listings
            while (i < historyQueryResults.Count)
            {
                // pull up to 20 entries from the list, skipping any from previous iterations
                var currentPageHistoryList = historyQueryResults.Skip(i).Take(itemsPerPage);

                StringBuilder sbListing = new StringBuilder();

                // build data for this page
                foreach (var listing in currentPageHistoryList)
                {
                    sbListing.Append($"**{listing.Quantity}** ");

                    if (listing.IsHq)
                        sbListing.Append("**HQ** ");

                    if (listing.Quantity > 1)
                        // multiple units
                        sbListing.Append($"for {listing.SoldPrice * listing.Quantity} (**{listing.SoldPrice}** per unit) ");
                    else // single units
                        sbListing.Append($"for **{listing.SoldPrice}** ");
                    sbListing.AppendLine();
                    if (server == null)
                        sbListing.AppendLine($"• Sold on **{listing.Server}** at {listing.SaleDate}");
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


        // interactive user selection prompt - each item in the passed collection gets listed out with an emoji
        // user selects an emoji, and the handlecallback function is run with the corresponding item ID as its parameter
        // it's expected that this function will be the last call in a function before that terminates, and that the callback function
        // will re-run the function with the user-selected data
        // optional server parameter to preserve server filter option
        private async Task InteractiveUserSelectItem(OrderedDictionary itemsDictionary, string functionToCall, string server = null)
        {
            string[] numbers = new[] { "0⃣", "1⃣", "2⃣", "3⃣", "4⃣", "5⃣", "6⃣", "7⃣", "8⃣", "9⃣" };
            var numberEmojis = new List<Emoji>();

            EmbedBuilder embedBuilder = new EmbedBuilder();
            StringBuilder stringBuilder = new StringBuilder();

            // add the number of emojis we need to the emojis list, and build our string-list of search results
            for (int i = 0; i < itemsDictionary.Count && i < numbers.Length; i++)
            {
                numberEmojis.Add(new Emoji(numbers[i]));
                // get key for this dictionaryentry at index
                var itemsDictionaryEntry = itemsDictionary.Cast<DictionaryEntry>().ElementAt(i);

                stringBuilder.AppendLine($"{numbers[i]} - {itemsDictionaryEntry.Key}");
            }

            embedBuilder.WithDescription(stringBuilder.ToString());
            embedBuilder.WithColor(Color.Blue);

            // build a message and add reactions to it
            // reactions will be watched, and the one selected will fire the HandleFindTagReactionResult method, passing
            // that reaction's corresponding tagname and the function passed into this parameter
            var messageContents = new ReactionCallbackData("Did you mean... ", embedBuilder.Build());
            for (int i = 0; i < itemsDictionary.Count; i++)
            {
                var counter = i;
                var itemsDictionaryEntry = itemsDictionary.Cast<DictionaryEntry>().ElementAt(i);
                messageContents.AddCallBack(numberEmojis[counter], async (c, r) => await HandleInteractiveUserSelectCallback((int) itemsDictionaryEntry.Value, functionToCall, server));
                
            }

            var message = await InlineReactionReplyAsync(messageContents);

            // add calling user and searchResults embed to a dict as a pair
            // this way we can hold multiple users' reaction messages and operate on them separately
            // _dictFindTagUserEmbedPairs.Add(Context.User, message);
        }

        // this might get modified to accept a 'function' param that will run in a switch:case to
        // select what calling function this callback handler should re-run with the user-selected data
        // optional server parameter to preserve server filter option
        private async Task HandleInteractiveUserSelectCallback(int itemId, string function, string server = null)
        {
            switch (function)
            {
                case "market":
                    await MarketGetItemPriceAsync($"{server} {itemId}");
                    break;
                case "history":
                    await MarketGetItemHistoryAsync($"{server} {itemId}");
                    break;
            }
            
        }
    }
}
