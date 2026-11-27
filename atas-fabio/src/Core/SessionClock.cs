using System;
using System.Collections.Generic;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// Regular-session (cash) clock. SKILL.md: bias comes from the regular/cash session value areas.
    /// Times are exchange-local (see ExchangeTime). Sessions that cross midnight are supported: bars after
    /// midnight belong to the session that started the previous day.
    /// </summary>
    public sealed class SessionClock
    {
        private readonly TimeSpan _start, _end;
        private readonly bool _crossesMidnight;

        public SessionClock(TimeSpan rthStart, TimeSpan rthEnd)
        {
            _start = rthStart; _end = rthEnd;
            _crossesMidnight = rthEnd <= rthStart;
        }

        public bool IsRth(DateTime barOpenTime)
        {
            var t = barOpenTime.TimeOfDay;
            return _crossesMidnight ? (t >= _start || t < _end) : (t >= _start && t < _end);
        }

        /// <summary>Session identity (the date the session started) for RTH bars, null for everything else.</summary>
        public DateTime? SessionKey(DateTime barOpenTime)
        {
            if (!IsRth(barOpenTime)) return null;
            if (_crossesMidnight && barOpenTime.TimeOfDay < _end) return barOpenTime.Date.AddDays(-1);
            return barOpenTime.Date;
        }

        public TimeSpan Start { get { return _start; } }
    }

    /// <summary>
    /// ATAS candle times are UTC. The cash session is defined in the exchange's own clock (09:30-16:00 New York for
    /// ES/NQ), so every bar is converted with the exchange time zone, daylight saving included. This makes the session
    /// correct wherever the trader lives and whatever display time zone the chart uses.
    /// </summary>
    public static class ExchangeTime
    {
        private static readonly Dictionary<string, string> Alt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "America/New_York", "Eastern Standard Time" }, { "Eastern Standard Time", "America/New_York" },
            { "America/Chicago", "Central Standard Time" }, { "Central Standard Time", "America/Chicago" },
            { "Europe/London", "GMT Standard Time" }, { "GMT Standard Time", "Europe/London" },
            { "Europe/Berlin", "W. Europe Standard Time" }, { "W. Europe Standard Time", "Europe/Berlin" },
            { "Asia/Tokyo", "Tokyo Standard Time" }, { "Tokyo Standard Time", "Asia/Tokyo" }
        };

        /// <summary>Finds a zone by IANA or Windows id (tries the other naming if the first is unknown). Null if neither exists.</summary>
        public static TimeZoneInfo Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id.Trim()); } catch (Exception) { }
            string other;
            if (Alt.TryGetValue(id.Trim(), out other))
                try { return TimeZoneInfo.FindSystemTimeZoneById(other); } catch (Exception) { }
            return null;
        }

        public static DateTime FromUtc(DateTime utc, TimeZoneInfo zone)
        {
            if (zone == null) return utc;
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        }
    }
}
