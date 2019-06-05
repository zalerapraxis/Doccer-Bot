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

            // api failure on our end
            if (itemSearchResults.GetType() == typeof(MarketAPIRequestFailureStatus) && itemSearchResults == MarketAPIRequestFailureStatus.APIFailure)
            {
                await ReplyAsync($"Request failed, try again. If this keeps happening, let {Context.Guild.GetUser(110866678161645568).Mention} know.");
                return;
            }

            // no results
            if (itemSearchResults.GetType() == typeof(MarketAPIRequestFailureStatus) && itemSearchResults.Count == MarketAPIRequestFailureStatus.NoResults)
            {
                await ReplyAsync("No results found. Try to expand your search terms, or check for typos.");
                return;
            }

            if (itemSearchResults.Count > 20)
            {
                await ReplyAsync("Too many results returned - try narrowing down your search terms.");
                return;
            }

            StringBuilder sbNameColumn = new StringBuilder();
            StringBuilder sbItemIdColumn = new StringBuilder();

            EmbedBuilder ebSearchResults = new EmbedBuilder();

            foreach (var item in itemSearchResults)
            {
                sbNameColumn.AppendLine(item.Key.ToString());
                sbItemIdColumn.AppendLine(item.Value.ToString());
            }

            ebSearchResults.AddField("Name", sbNameColumn.ToString(), true);
            ebSearchResults.AddField("ID", sbItemIdColumn.ToString(), true);
            ebSearchResults.WithColor(Color.Blue);
            ebSearchResults.WithCurrentTimestamp();

            await ReplyAsync(null, false, ebSearchResults.Build());
        }
    }
}
