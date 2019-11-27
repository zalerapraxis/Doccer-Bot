using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Doccer_Bot.Datasets;
using Doccer_Bot.Entities;
using Doccer_Bot.Models;
using Doccer_Bot.Modules.Common;
using Doccer_Bot.Services;
using Flurl.Util;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using SkiaSharp;

namespace Doccer_Bot.Modules
{
    [Name("Market")]
    [Remarks("Realtime FFXIV market data")]
    public class FFXIVMarketModule : InteractiveBase
    {
        public MarketService MarketService { get; set; }

        private Dictionary<IUser, IUserMessage> _dictFindItemUserEmbedPairs = new Dictionary<IUser, IUserMessage>();

        private Datacenter DefaultDatacenter = Datacenter.aether;
        private World DefaultWorld = World.gilgamesh;

        [Command("market price", RunMode = RunMode.Async)]
        [Alias("mbp")]
        [Summary("Get prices for an item - takes item name or item id")]
        [Example("market price (server) {name/id}")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketGetItemPriceAsync([Remainder] string input)
        {
            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            input = input.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            var worldsToSearch = GetServerOrDatacenterParameter(input, false);
            input = CleanCommandInput(input);


            // declare vars - these will get populated eventually
            int itemId;
            string itemName = "";
            string itemIconUrl = "";

            // try to get an itemid from input - may return null
            var itemIdResponse = await GetItemIdFromInput(input, InteractiveCommandReturn.Price, worldsToSearch);

            // handle getitemidfrominput errors
            if (itemIdResponse == null || itemIdResponse <= 0)
            {
                switch (itemIdResponse)
                {
                    case null: // something wrong with xivapi
                        await ReplyAsync("Something is wrong with XIVAPI. Try using Garlandtools to get the item's ID and use that instead.");
                        return;
                    case 0: // handing off to interactiveusercallback
                        return;
                    case -1: // no results
                        await ReplyAsync("No tradable items found. Try to expand your search terms, or check for typos. ");
                        return;
                    case -2: // too many results
                        await ReplyAsync("Too many results were found via that search term. Try narrowing it down, or use an item ID instead.");
                        return;
                }
            }

            // set itemId as the non-null itemId response
            itemId = itemIdResponse.Value;

            // get the item name & assign it
            var itemDetailsQueryResult = await MarketService.QueryXivapiWithItemId(itemId);

            // no results - should only trigger if user inputs a bad itemID
            if (itemDetailsQueryResult.GetType() == typeof(MarketAPIRequestFailureStatus) && itemDetailsQueryResult == MarketAPIRequestFailureStatus.NoResults)
            {
                await ReplyAsync("The item ID you provided doesn't correspond to any items. Try searching by item name instead.");
                return;
            }

            // assign more vars from results
            itemName = itemDetailsQueryResult.Name;
            itemIconUrl = $"https://xivapi.com/{itemDetailsQueryResult.Icon}";

            // get market data
            var marketQueryResults = await MarketService.GetMarketListings(itemName, itemId, worldsToSearch);

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
        public async Task MarketGetItemHistoryAsync([Remainder] string input)
        {
            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            input = input.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            var worldsToSearch = GetServerOrDatacenterParameter(input, false);
            input = CleanCommandInput(input);

            // declare vars - both of these will get populated eventually
            int itemId;
            string itemName = "";
            string itemIconUrl = "";

            // try to get an itemid from input - may return null
            var itemIdResponse = await GetItemIdFromInput(input, InteractiveCommandReturn.History, worldsToSearch);

            // handle getitemidfrominput errors
            if (itemIdResponse == null || itemIdResponse <= 0)
            {
                switch (itemIdResponse)
                {
                    case null: // something wrong with xivapi
                        await ReplyAsync("Something is wrong with XIVAPI. Try using Garlandtools to get the item's ID and use that instead.");
                        return;
                    case 0: // handing off to interactiveusercallback
                        return;
                    case -1: // no results
                        await ReplyAsync("No tradable items found. Try to expand your search terms, or check for typos. ");
                        return;
                    case -2: // too many results
                        await ReplyAsync("Too many results were found via that search term. Try narrowing it down, or use an item ID instead.");
                        return;
                }
            }

            // set itemId as the non-null itemId response
            itemId = itemIdResponse.Value;

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
            var historyQueryResults = await MarketService.GetHistoryListings(itemName, itemId, worldsToSearch);

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

            // model config
            var plotModel = new PlotModel()
            {
                TextColor = OxyColor.Parse("#FFFFFF"),
                IsLegendVisible = true,
                LegendPlacement = LegendPlacement.Inside,
                LegendPosition = LegendPosition.BottomLeft,
                LegendTextColor = OxyColor.Parse("#FFFFFF"),
                LegendLineSpacing = 5,
            };

            // axes config
            var dateAxis = new DateTimeAxis()
            {
                Title = "Time",
                Position = AxisPosition.Bottom,
                AxislineColor = OxyColor.Parse("#FFFFFF"),
                TicklineColor = OxyColor.Parse("#FFFFFF"),
                StringFormat = "HH:mm",
                AbsoluteMinimum = OxyPlot.Axes.Axis.ToDouble(DateTime.Now.Date.AddDays(-2)),
            };
            var priceAxis = new LinearAxis()
            {
                Title = "Price",
                AxislineColor = OxyColor.Parse("#FFFFFF"),
                TicklineColor = OxyColor.Parse("#FFFFFF"),
                Position = AxisPosition.Left
            };

            // build dictionary of lineseries, where we can pair server+datasets
            var Lines = new Dictionary<string, LineSeries>();
            foreach (var listing in historyQueryResults)
            {
                if (Lines.ContainsKey(listing.Server) == false)
                {
                    Lines.Add(listing.Server, new LineSeries
                    {
                        Title = listing.Server,
                        MarkerFill = OxyColor.Parse("#FFFFFF"),
                        MarkerType = MarkerType.Circle,
                        MarkerSize = 2,
                        StrokeThickness = 0.75
                    });
                }
            }

            // populate lineseries in dictionary with data
            foreach (var listing in historyQueryResults) // iterate through listings
            {
                foreach (var line in Lines) // with each listing, check through the list of lineseries
                {
                    if (line.Key.Equals(listing.Server)) // if this listing's server matches the line's key
                    {
                        line.Value.Points.Add(new DataPoint(DateTimeAxis.ToDouble(listing.SaleDate), listing.SoldPrice));
                    }
                }
            }

            // build an average list line
            var avgSeries = new LineSeries
            {
                Title = "Average",
                LineStyle = LineStyle.LongDash,
                Color = OxyColor.Parse("#FF00FF"),
                StrokeThickness = 2,
            };

            // collect every point, where points represent sold items
            var allPoints = new List<DataPoint>();
            foreach (var line in Lines)
            {
                allPoints.AddRange(line.Value.Points);
            }

            // avg out data
            double xmax = allPoints.Max(x => x.X);
            double xmin = allPoints.Min(x => x.X);
            int bins = 12;
            double groupSize = (xmax - xmin) / (bins - 1);

            var grouped = allPoints
                .OrderBy(x => x.X)
                .GroupBy(x => groupSize * (int)(x.X / groupSize))
                .Select(x => new { xval = x.Key, yavg = x.Average(y => y.Y) })
                .ToList();

            // add to line list
            foreach (var kv in grouped) avgSeries.Points.Add(new DataPoint(DateTimeAxis.ToDouble(kv.xval + groupSize/2f), kv.yavg));


            // put it all together
            plotModel.Axes.Add(dateAxis);
            plotModel.Axes.Add(priceAxis);
            // add each lineseries to the plot
            foreach (var line in Lines)
                plotModel.Series.Add(line.Value);
            plotModel.Series.Add(avgSeries);

            // save plot as svg
            using (var stream = File.Create($"{itemName}.svg"))
            {
                var exporter = new SvgExporter { Width = 800, Height = 600 };
                exporter.Export(plotModel, stream);
            }

            // load plot from file
            var svg = new SKSvg();
            svg.Load($"{itemName}.svg");

            // convert
            var bitmap = new SKBitmap((int)svg.CanvasSize.Width, (int)svg.CanvasSize.Height);
            var canvas = new SKCanvas(bitmap);
            canvas.DrawPicture(svg.Picture);
            canvas.Flush();
            canvas.Save();

            // save
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode())
            {
                // save the data to a stream
                using (var file = File.OpenWrite($"{itemName}.png"))
                {
                    data.SaveTo(file);
                }
            }

            await Context.Channel.SendFileAsync(Path.Combine(Environment.CurrentDirectory, $"{itemName}.png"));

            // delete svg & png temp files
            File.Delete($"{itemName}.svg");
            File.Delete($"{itemName}.png");
        }


        [Command("market analyze", RunMode = RunMode.Async)]
        [Alias("mba")]
        [Summary("Get market analysis for an item")]
        [Example("market analyze {name/id} (server) - defaults to Gilgamesh")]
        // function will attempt to parse server from searchTerm, no need to make a separate param
        public async Task MarketAnalyzeItemAsync([Remainder] string input)
        {
            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            input = input.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            var worldsToSearch = GetServerOrDatacenterParameter(input, true);
            input = CleanCommandInput(input);

            // declare vars - both of these will get populated eventually
            int itemId;
            string itemName = "";
            string itemIconUrl = "";

            // try to get an itemid from input - may return null
            var itemIdResponse = await GetItemIdFromInput(input, InteractiveCommandReturn.Analyze, worldsToSearch);

            // handle getitemidfrominput errors
            if (itemIdResponse == null || itemIdResponse <= 0)
            {
                switch (itemIdResponse)
                {
                    case null: // something wrong with xivapi
                        await ReplyAsync("Something is wrong with XIVAPI. Try using Garlandtools to get the item's ID and use that instead.");
                        return;
                    case 0: // handing off to interactiveusercallback
                        return;
                    case -1: // no results
                        await ReplyAsync("No tradable items found. Try to expand your search terms, or check for typos. ");
                        return;
                    case -2: // too many results
                        await ReplyAsync("Too many results were found via that search term. Try narrowing it down, or use an item ID instead.");
                        return;
                }
            }

            // set itemId as the non-null itemId response
            itemId = itemIdResponse.Value;

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
            var marketAnalysis = await MarketService.CreateMarketAnalysis(itemName, itemId, worldsToSearch);
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
            if (worldsToSearch.Count == 1)
                embedNameBuilder.Append($" on {worldsToSearch[0]}");

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
        public async Task MarketGetBestCurrencyExchangesAsync([Remainder] string input = null)
        {
            if (input == null || !input.Any())
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
            input = input.ToLower();

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            var worldsToSearch = GetServerOrDatacenterParameter(input, true);
            input = CleanCommandInput(input);

            string category = input;

            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            // grab data from api
            var currencyDeals = await MarketService.GetBestCurrencyExchange(category, worldsToSearch);

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
            if (worldsToSearch.Count == 1)
                embedNameBuilder.Append($" on {worldsToSearch[0]}");

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
        [Example("market order {itemname:count, itemname:count, etc...} or marker order {fending, crafting, etc}")]
        public async Task MarketCrossWorldPurchaseOrderAsync([Remainder] string input = null)
        {
            if (input == null || !input.Any())
            {
                // let the user know they fucked up, or don't
                return;
            }

            // convert to lowercase so that if user specified server in capitals,
            // it doesn't break our text matching in serverlist and with api request
            input = input.ToLower();

            var worldsToSearch = GetServerOrDatacenterParameter(input, false);
            input = CleanCommandInput(input);

            // check if user input a gearset request instead of item lists
            if (MarketOrderGearsetDataset.Gearsets.Any(x => input.Contains(x.Key)))
            {
                var gearset = MarketOrderGearsetDataset.Gearsets.FirstOrDefault(x => x.Key.Equals(input)).Value;

                var i = 1;
                var gearsetInputSb = new StringBuilder();
                foreach (var item in gearset)
                {
                    var count = 1;
                    if (item.Contains("Ring"))
                        count = 2;

                    gearsetInputSb.Append($"{item}:{count}");

                    if (i < gearset.Count)
                        gearsetInputSb.Append(", ");

                    i++;
                }

                input = gearsetInputSb.ToString();
            }

            // split each item:count pairing
            var inputList = input.Split(", ");

            
            var itemsList = new List<MarketItemXWOrderModel>();
            foreach (var item in inputList)
            {
                var itemSplit = item.Split(":");

                var itemName = itemSplit[0];
                var NeededQuantity = int.Parse(itemSplit[1]);
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

            var results = await MarketService.GetMarketCrossworldPurchaseOrder(itemsList, worldsToSearch);

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
            await ReplyAsync("If any items are missing from the list, it's likely they'd take too many purchases to process. Consider using the market price (mbp) command for missing items.", false, purchaseOrderEmbed.Build());
        }


        [Command("market status", RunMode = RunMode.Async)]
        [Alias("mbs")]
        [Summary("")]
        public async Task MarketServerStatusAsync()
        {
            // show that the bot's processing
            await Context.Channel.TriggerTypingAsync();

            StringBuilder serverStatusStringBuilder = new StringBuilder();
            serverStatusStringBuilder.AppendLine("**Server status**");

            var allServersList = new List<String>();
            allServersList.AddRange(MarketService.ServerList_Aether);
            allServersList.AddRange(MarketService.ServerList_Primal);

            var tasks = Task.Run(() => Parallel.ForEach(allServersList, server =>
            {
                var serverStatusResult = MarketService.GetCompanionApiStatus(server).Result;
                var serverStatus = "";

                if (serverStatusResult == MarketAPIRequestFailureStatus.OK)
                    serverStatus = "✅";
                else
                    serverStatus = "❌";

                serverStatusStringBuilder.AppendLine($"{server}: {serverStatus}");
            }));

            await Task.WhenAll(tasks);

            await ReplyAsync(serverStatusStringBuilder.ToString());
        }


        [Command("market login", RunMode = RunMode.Async)]
        [Alias("mbl")]
        [Summary("Check status of servers & attempt to log in to any that aren't logged in")]
        public async Task MarketServerLoginAsync()
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
        private async Task InteractiveUserSelectItem(List<ItemSearchResultModel> itemsList, InteractiveCommandReturn function, List<string> worldsToSearch)
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
                messageContents.AddCallBack(numberEmojis[counter], async (c, r) => HandleInteractiveUserSelectCallback(itemId, function, worldsToSearch));
            }

            var message = await InlineReactionReplyAsync(messageContents);

            // add calling user and searchResults embed to a dict as a pair
            // this way we can hold multiple users' reaction messages and operate on them separately
            _dictFindItemUserEmbedPairs.Add(Context.User, message);
        }


        // this might get modified to accept a 'function' param that will run in a switch:case to
        // select what calling function this callback handler should re-run with the user-selected data
        // optional server parameter to preserve server filter option
        private async Task HandleInteractiveUserSelectCallback(int itemId, InteractiveCommandReturn function, List<string> worldsToSearch)
        {
            string searchLocation = "";
            
            // figure out what world or datacenter we should pass to the command
            if (worldsToSearch.Intersect(Enum.GetNames(typeof(World))).Count() == 1)
            {
                searchLocation = worldsToSearch.FirstOrDefault(x => Enum.GetNames(typeof(World)).Contains(x));
            }
            else if (worldsToSearch.Intersect(Enum.GetNames(typeof(World))).Count() > 1)
            {
                if (worldsToSearch.Intersect(Enum.GetNames(typeof(WorldsAetherEnum))).Count() > 1)
                    searchLocation = "aether";
                else if (worldsToSearch.Intersect(Enum.GetNames(typeof(WorldsPrimalEnum))).Count() > 1)
                    searchLocation = "primal";
            }
            
            // grab the calling user's pair of calling user & searchResults embed
            var dictEntry = _dictFindItemUserEmbedPairs.FirstOrDefault(x => x.Key == Context.User);

            // delete the calling user's searchResults embed, if it exists
            if (dictEntry.Key != null)
                await dictEntry.Value.DeleteAsync();

            switch (function)
            {
                case InteractiveCommandReturn.Price:
                    await MarketGetItemPriceAsync($"{searchLocation} {itemId}");
                    break;
                case InteractiveCommandReturn.History:
                    await MarketGetItemHistoryAsync($"{searchLocation} {itemId}");
                    break;
                case InteractiveCommandReturn.Analyze:
                    await MarketAnalyzeItemAsync($"{searchLocation} {itemId}");
                    break;
            }
        }

        // returns a list of strings representing the servers that should be parsed
        // can either be one server, in the case of the user requesting a specific server, or all servers in a datacenter
        private List<string> GetServerOrDatacenterParameter(string input, bool useDefaultWorld)
        {
            var resultsList = new List<string>();

            // server handling

            // try to get server name from the given text
            var pattern = new Regex(@"\W");
            var server = pattern.Split(input).FirstOrDefault(x => MarketService.ServerList.Contains(x));

            // if we found a server, user wants that specific server, so add it to resultsList and return that
            if (server != null)
            {
                resultsList.Add(server);
                return resultsList;
            }

            // if we didn't find a server, but calling function requested we use the default world instead of the default datacenter,
            // return the default world instead of the default datacenter
            if (useDefaultWorld && server == null)
            {
                resultsList.Add(DefaultWorld.ToString());
                return resultsList;
            }

            // datacenter handling, if server not found
            if (Regex.Match(input, @"\baether\b", RegexOptions.IgnoreCase).Success)
            {
                foreach (var world in Enum.GetValues(typeof(WorldsAetherEnum)))
                    resultsList.Add(world.ToString());
            }
            if (Regex.Match(input, @"\bprimal\b", RegexOptions.IgnoreCase).Success)
            {
                foreach (var world in Enum.GetValues(typeof(WorldsPrimalEnum)))
                    resultsList.Add(world.ToString());
            }


            // if nothing matched world/datacenter checks, use the default datacenter
            if (!resultsList.Any())
            {
                if (DefaultDatacenter == Datacenter.aether)
                    foreach (var world in Enum.GetValues(typeof(WorldsAetherEnum)))
                        resultsList.Add(world.ToString());
                if (DefaultDatacenter == Datacenter.primal)
                    foreach (var world in Enum.GetValues(typeof(WorldsPrimalEnum)))
                        resultsList.Add(world.ToString());
            }

            return resultsList;
        }

        private string CleanCommandInput(string input)
        {
            var wordsToRemove = new List<string>();
            string result = input;

            // add each possible input into a list of words to look for
            foreach (var world in Enum.GetValues(typeof(World)))
                wordsToRemove.Add(world.ToString());
            foreach (var datacenter in Enum.GetValues(typeof(Datacenter)))
                wordsToRemove.Add(datacenter.ToString());

            foreach (var word in wordsToRemove)
            {
                if (Regex.Match(input, $@"\b{word}\b", RegexOptions.IgnoreCase).Success)
                {
                    result = ReplaceWholeWord(input, word, "");
                }
            }

            return result;
        }

        private async Task<int?> GetItemIdFromInput(string input, InteractiveCommandReturn function, List<string> worldsToSearch)
        {
            int itemId;

            // try to see if the given text is an item ID
            var searchTermIsItemId = int.TryParse(input, out itemId);

            // if user passed a itemname, get corresponding itemid.
            if (!searchTermIsItemId)
            {
                // response is either a ordereddictionary of keyvaluepairs, or null
                var itemIdQueryResult = await MarketService.SearchForItemByName(input);

                // something is wrong with xivapi
                if (itemIdQueryResult == null)
                    return null;

                // no results
                if (itemIdQueryResult.Count == -1)
                    return -1;

                // too many results
                if (itemIdQueryResult.Count > 15)
                    return -2;

                // if more than one result was found, send the results to the selection function to narrow it down to one
                // terminate this function, as the selection function will eventually re-call this method with a single result item
                // 10 is the max number of items we can use interactiveuserselectitem with
                if (itemIdQueryResult.Count > 1 && itemIdQueryResult.Count < 15)
                {
                    await InteractiveUserSelectItem(itemIdQueryResult, function, worldsToSearch);
                }

                // if only one result was found, select it and continue without any prompts
                if (itemIdQueryResult.Count == 1)
                {
                    itemId = itemIdQueryResult[0].ID;
                }
            }

            return itemId;
        }

        private string GetCustomAPIStatusHumanResponse(MarketAPIRequestFailureStatus status)
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