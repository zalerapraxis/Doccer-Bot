using System;
using System.Collections.Generic;
using System.Text;

namespace Doccer_Bot.Models
{
    // used for both supplying the xworder func with params and returning the results back to the calling command
    public class MarketItemXWOrderModel
    {
        public string Name { get; set; }
        public int ItemID { get; set; }
        public int Price { get; set; }
        public int Quantity { get; set; }
        public bool IsHQ { get; set; } // return value
        public bool ShouldBeHQ { get; set; } // parameter value
        public int NeededQuantity { get; set; } // parameter value
        public string Server { get; set; } // return value
    }

    public class MarktetItemXWOrderListModel
    {
        public List<MarketItemXWOrderModel> List { get; set; }
        public int TotalCost { get; set; }
    }
}
