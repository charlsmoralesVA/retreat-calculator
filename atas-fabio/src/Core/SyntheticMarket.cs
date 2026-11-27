using System;
using System.Collections.Generic;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// SYNTHETIC trades for practice and pipeline checks only: a random walk around a drifting value centre, with
    /// trending and balancing stretches, two contracts per quarter so rolls are exercised, aggressor flags with noise.
    /// It has no edge and no realism beyond "looks like a futures tape"; never read its results as a strategy result.
    /// </summary>
    public static class SyntheticMarket
    {
        public static IEnumerable<RawTrade> Trades(DateTime firstDay, int days, int seed, decimal tick = 0.25m, bool overnight = true)
        {
            var rnd = new Random(seed);
            var ny = ExchangeTime.Find("America/New_York");
            decimal center = 4500m, price = center, drift = 0;
            int regimeLeft = 0;
            var day = firstDay.Date;
            for (int d = 0; d < days; d++, day = day.AddDays(1))
            {
                if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday) continue;
                if (regimeLeft-- <= 0)
                {
                    regimeLeft = 3 + rnd.Next(10);
                    double u = rnd.NextDouble();
                    drift = u < 0.35 ? 6m : u < 0.7 ? -6m : 0m;     // trend up, trend down, balance
                }
                center += drift + (decimal)(rnd.NextDouble() - 0.5) * 8m;
                string front = Contract(day, false), next = Contract(day, true);
                bool rollWeek = IsRollWindow(day);
                // contango: each contract trades above the index by an amount that shrinks to zero at its expiry
                decimal basisFront = Basis(day, ContractExpiry(day, false)), basisNext = Basis(day, ContractExpiry(day, true));

                var segments = new List<KeyValuePair<DateTime, DateTime>>();
                if (overnight) segments.Add(new KeyValuePair<DateTime, DateTime>(day.AddHours(-6), day.AddHours(9.5)));
                segments.Add(new KeyValuePair<DateTime, DateTime>(day.AddHours(9.5), day.AddHours(16)));
                foreach (var seg in segments)
                {
                    bool cash = seg.Key.Hour == 9;
                    DateTime t = seg.Key;
                    while (t < seg.Value)
                    {
                        t = t.AddMilliseconds(cash ? 300 + rnd.Next(2200) : 3000 + rnd.Next(12000));
                        decimal pull = (center - price) * 0.00015m;
                        decimal np = Math.Round((price + (decimal)(rnd.NextDouble() - 0.5) * 1.2m + pull) / tick) * tick;
                        Side side = np > price ? Side.Long : np < price ? Side.Short : (rnd.Next(2) == 0 ? Side.Long : Side.Short);
                        if (rnd.NextDouble() < 0.08) side = side == Side.Long ? Side.Short : Side.Long;
                        price = np;
                        decimal vol = rnd.NextDouble() < 0.01 ? 60 + rnd.Next(120) : 1 + (int)(-Math.Log(1 - rnd.NextDouble()) * 6);
                        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), ny);
                        string sym = front;
                        // around the roll both contracts trade; the next one gains volume through the window
                        if (rollWeek && rnd.NextDouble() < RollShare(day)) sym = next;
                        decimal px = Math.Round((price + (sym == next ? basisNext : basisFront)) / tick) * tick;
                        yield return new RawTrade { Utc = utc, Price = px, Size = vol, Aggressor = side, Symbol = sym };
                        if (!rollWeek && rnd.NextDouble() < 0.002)
                            yield return new RawTrade { Utc = utc, Price = Math.Round((price + basisNext) / tick) * tick, Size = 1, Aggressor = side, Symbol = next };
                    }
                }
            }
        }

        // quarterly ES-style contracts: H M U Z; the roll window is the 8 days before the 2nd Thursday of the expiry month
        private static readonly char[] Codes = { 'H', 'M', 'U', 'Z' };

        private static string Contract(DateTime day, bool next)
        {
            int q = (day.Month - 1) / 3; int year = day.Year;
            var expiry = Expiry(year, q * 3 + 3);
            if (day >= expiry.AddDays(-8)) { q++; if (q == 4) { q = 0; year++; } }
            if (next) { q++; if (q == 4) { q = 0; year++; } }
            return "ES" + Codes[q] + (year % 10);
        }

        private static decimal Basis(DateTime day, DateTime expiry)
        {
            return 20m * (decimal)Math.Max(0, (expiry - day).TotalDays) / 91m;
        }

        private static DateTime ContractExpiry(DateTime day, bool next)
        {
            int q = (day.Month - 1) / 3; int year = day.Year;
            if (day >= Expiry(year, q * 3 + 3).AddDays(-8)) { q++; if (q == 4) { q = 0; year++; } }
            if (next) { q++; if (q == 4) { q = 0; year++; } }
            return Expiry(year, q * 3 + 3);
        }

        private static DateTime Expiry(int year, int month)
        {
            var d = new DateTime(year, month, 1);
            int thursdays = 0;
            while (true) { if (d.DayOfWeek == DayOfWeek.Thursday && ++thursdays == 2) return d; d = d.AddDays(1); }
        }

        private static bool IsRollWindow(DateTime day)
        {
            int q = (day.Month - 1) / 3;
            var exp = Expiry(day.Year, q * 3 + 3);
            return day >= exp.AddDays(-14) && day < exp.AddDays(-8);
        }

        private static double RollShare(DateTime day)
        {
            int q = (day.Month - 1) / 3;
            var exp = Expiry(day.Year, q * 3 + 3);
            double daysIn = (day - exp.AddDays(-14)).TotalDays;
            return Math.Min(0.9, 0.15 + daysIn * 0.15);
        }
    }
}
