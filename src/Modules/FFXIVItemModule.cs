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

namespace Doccer_Bot.Modules
{
    [Name("Item")]
    public class FFXIVItemModule : InteractiveBase
    {
        public MarketService MarketService { get; set; }


        [Command("item search", RunMode = RunMode.Async)]
        [Summary("Search for items by name - requires a search term")]
        [Example("item search {name}")]
        public async Task ItemSearchAsync([Remainder] string searchTerm)
        {
            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // response is either a ordereddictionary of keyvaluepairs, or null
            var itemSearchResults = await MarketService.SearchForItemByName(searchTerm);

            // no results
            if (itemSearchResults.Count == 0)
            {
                await ReplyAsync("No results found. Try to expand your search terms, or check for typos.");
                return;
            }

            var pages = new List<PaginatedMessage.Page>();

            var i = 0;
            var itemsPerPage = 12;

            // iterate through the market results, making a page for every (up to) itemsPerPage listings
            while (i < itemSearchResults.Count)
            {
                // pull up to itemsPerPage entries from the list, skipping any from previous iterations
                var currentPageItemSearchResultsList = itemSearchResults.Skip(i).Take(itemsPerPage);

                StringBuilder sbListingName = new StringBuilder();
                StringBuilder sbListingId = new StringBuilder();

                // build data for this page
                foreach (var item in currentPageItemSearchResultsList)
                {
                    sbListingName.AppendLine(item.Name);
                    sbListingId.AppendLine(item.ID.ToString());
                }

                var page = new PaginatedMessage.Page()
                {
                    Fields = new List<EmbedFieldBuilder>()
                    {
                        new EmbedFieldBuilder()
                        {
                            Name = "Name",
                            Value = sbListingName,
                            IsInline = true
                        },
                        new EmbedFieldBuilder()
                        {
                            Name = "ID",
                            Value = sbListingId,
                            IsInline = true
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
                    Name = $"{itemSearchResults.Count} result(s) for \"{searchTerm}\"",
                },
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
    }
}
