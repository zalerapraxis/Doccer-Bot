using System;
using System.Collections.Generic;
using System.Text;

namespace Doccer_Bot.Models
{
    public class MarketItemAnalysisModel
    {
        public string Name { get; set; }
        public int ID { get; set; }
        public bool IsHQ { get; set; }
        public decimal Differential { get; set; }
        public int AvgSalePrice { get; set; }
        public int AvgMarketPrice { get; set; }
        public int numRecentSales { get; set; }
        public bool ItemHasListings { get; set; }
        public bool ItemHasHistory { get; set; }
    }
}
