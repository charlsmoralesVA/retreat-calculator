using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// Reads trades from CSV: timestamp,price,volume[,aggressor]
    ///  * optional header line, '#' comment lines, blank lines ignored
    ///  * timestamp: wall-clock text (kept as is, it must already be in the clock RTH start/end refer to) or ISO-8601 with
    ///    'Z' / an offset (converted to UTC, then shifted by offsetHours)
    ///  * aggressor: the side that INITIATED the trade. B / BUY / 1 / +1 = buyer lifted the offer; S / SELL / -1 = seller hit
    ///    the bid; anything else (or missing) = unknown, the aggregator applies the tick rule. Vendors code this differently:
    ///    pass your own tokens if needed (check your data vendor's documentation).
    /// </summary>
    public static class CsvTicks
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static IEnumerable<Tick> Read(TextReader reader, double offsetHours = 0,
            ICollection<string> buyTokens = null, ICollection<string> sellTokens = null)
        {
            var buy = new HashSet<string>(buyTokens ?? new[] { "B", "BUY", "1", "+1" }, StringComparer.OrdinalIgnoreCase);
            var sell = new HashSet<string>(sellTokens ?? new[] { "S", "SELL", "-1" }, StringComparer.OrdinalIgnoreCase);

            string line; bool first = true; int n = 0;
            while ((line = reader.ReadLine()) != null)
            {
                n++;
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split(',');
                if (f.Length < 3) throw new FormatException("line " + n + ": expected timestamp,price,volume[,aggressor]");

                DateTime time;
                if (!TryParseTime(f[0].Trim(), offsetHours, out time))
                {
                    if (first) { first = false; continue; }           // header
                    throw new FormatException("line " + n + ": bad timestamp '" + f[0] + "'");
                }
                first = false;

                decimal price, vol;
                if (!decimal.TryParse(f[1].Trim(), NumberStyles.Float, Inv, out price) ||
                    !decimal.TryParse(f[2].Trim(), NumberStyles.Float, Inv, out vol))
                    throw new FormatException("line " + n + ": bad price or volume");
                if (vol <= 0) continue;

                Side s = Side.None;
                if (f.Length > 3)
                {
                    string tok = f[3].Trim();
                    if (buy.Contains(tok)) s = Side.Long; else if (sell.Contains(tok)) s = Side.Short;
                }
                yield return new Tick { Time = time, Price = price, Volume = vol, Aggressor = s };
            }
        }

        public static bool TryParseTime(string s, double offsetHours, out DateTime t)
        {
            t = default(DateTime);
            DateTime parsed;
            if (!DateTime.TryParse(s, Inv, DateTimeStyles.RoundtripKind, out parsed)) return false;
            if (parsed.Kind == DateTimeKind.Unspecified) { t = parsed; return true; }          // already wall-clock
            t = DateTime.SpecifyKind(parsed.ToUniversalTime().AddHours(offsetHours), DateTimeKind.Unspecified);
            return true;
        }
    }
}
