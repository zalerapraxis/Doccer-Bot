using System;
using System.Collections.Generic;
using System.Text;

namespace Doccer_Bot
{
    public enum CalendarSyncStatus
    {
        /// <summary>
        ///     Sync is possible
        /// </summary>
        OK,
        /// <summary>
        ///     Missing Google Auth credentials
        /// </summary>
        NullCredentials,
        /// <summary>
        ///     Missing Calendar ID
        /// </summary>
        NullCalendarId,
        /// <summary>
        ///     Unassigned Calendar ID
        /// </summary>
        EmptyCalendarId,
        /// <summary>
        ///     Discord server unavailable
        /// </summary>
        ServerUnavailable
    }
}
