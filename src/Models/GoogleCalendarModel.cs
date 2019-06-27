using System;
using System.Collections.Generic;
using System.Text;
using Discord;

namespace Doccer_Bot.Models
{
    public class CalendarEvent
    {
        public string Name { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Timezone { get; set; }
        public IUserMessage AlertMessage { get; set; }
        public bool ManuallyAdjusted { get; set; }
        public string UniqueId { get; set; }
    }
}
