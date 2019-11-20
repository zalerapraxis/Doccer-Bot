using System;
using System.Collections.Generic;
using System.Text;

namespace Doccer_Bot.Entities
{
    public enum InteractiveCommandReturn
    {
        /// <summary>
        ///     Market Price command
        /// </summary>
        Price,
        /// <summary>
        ///     Market History command
        /// </summary>
        History,
        /// <summary>
        ///     Market Analyze command
        /// </summary>
        Analyze,
    }
}
