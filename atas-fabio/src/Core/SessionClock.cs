using System;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// Regular-session (cash) clock. SKILL.md: bias comes from the regular/cash session value areas.
    /// Times are in the chart's time zone; set RthStart/RthEnd to the instrument's cash session as your chart shows it.
    /// A session that crosses midnight is not supported.
    /// </summary>
    public sealed class SessionClock
    {
        private readonly TimeSpan _start, _end;
        public SessionClock(TimeSpan rthStart, TimeSpan rthEnd) { _start = rthStart; _end = rthEnd; }

        public bool IsRth(DateTime barOpenTime)
        {
            var t = barOpenTime.TimeOfDay;
            return t >= _start && t < _end;
        }

        /// <summary>Session identity (the date) for RTH bars, null for everything else.</summary>
        public DateTime? SessionKey(DateTime barOpenTime)
        {
            return IsRth(barOpenTime) ? barOpenTime.Date : (DateTime?)null;
        }

        public TimeSpan Start { get { return _start; } }
    }
}
