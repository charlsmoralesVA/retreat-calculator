// FabioCore.cs - pure logic, no ATAS dependency (so it can be unit-tested).
// Implements ONLY what SKILL.md states. Anything else is a TODO stub.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow
{
    public enum Bias { Neutral, Bullish, Bearish }

    public sealed class SessionProfile
    {
        public DateTime Date;
        public decimal Poc, Vah, Val;
        public decimal High, Low;
    }

    public static class FabioCore
    {
        // ---------- PILLAR 2: location (volume profile) ----------
        // Standard 70% value area built outward from the POC.
        public static SessionProfile BuildProfile(
            DateTime date, IDictionary<decimal, decimal> volumeByPrice, decimal valueAreaPct = 0.70m)
        {
            if (volumeByPrice.Count == 0) return null;
            var prices = volumeByPrice.Keys.OrderBy(p => p).ToList();
            decimal total = volumeByPrice.Values.Sum();
            int pocIdx = 0; decimal pocVol = -1;
            for (int i = 0; i < prices.Count; i++)
                if (volumeByPrice[prices[i]] > pocVol) { pocVol = volumeByPrice[prices[i]]; pocIdx = i; }

            int lo = pocIdx, hi = pocIdx; decimal acc = pocVol, target = total * valueAreaPct;
            while (acc < target && (lo > 0 || hi < prices.Count - 1))
            {
                decimal up = hi < prices.Count - 1 ? volumeByPrice[prices[hi + 1]] : -1;
                decimal dn = lo > 0 ? volumeByPrice[prices[lo - 1]] : -1;
                if (up >= dn) { hi++; acc += up; } else { lo--; acc += dn; }
            }
            return new SessionProfile
            {
                Date = date, Poc = prices[pocIdx], Vah = prices[hi], Val = prices[lo],
                High = prices.Last(), Low = prices.First()
            };
        }

        // ---------- PILLAR 1: bias ----------
        // SKILL.md: value higher day-over-day = bullish, lower = bearish, overlapping = neutral.
        // Overlap test: today's VA intersects yesterday's VA.
        public static Bias DailyBias(SessionProfile prev, SessionProfile cur)
        {
            if (prev == null || cur == null) return Bias.Neutral;
            bool overlap = cur.Val <= prev.Vah && cur.Vah >= prev.Val;
            if (overlap) return Bias.Neutral;
            return cur.Val > prev.Vah ? Bias.Bullish : Bias.Bearish;
        }

        // Multi-session narrative (>= 5 sessions). One counter-shift is treated as retracement:
        // bias = majority direction of the last N shifts.
        public static Bias NarrativeBias(IList<SessionProfile> sessions, int n = 5)
        {
            if (sessions.Count < 2) return Bias.Neutral;
            int up = 0, dn = 0;
            for (int i = Math.Max(1, sessions.Count - n + 1); i < sessions.Count; i++)
            {
                var b = DailyBias(sessions[i - 1], sessions[i]);
                if (b == Bias.Bullish) up++; else if (b == Bias.Bearish) dn++;
            }
            return up > dn ? Bias.Bullish : dn > up ? Bias.Bearish : Bias.Neutral;
        }

        // ---------- PILLAR 3: confirmation ("one side loses, the other wins") ----------
        // APPROXIMATION of a discretionary read. Thresholds are NOT from Fabio - calibrate yourself.
        public sealed class Bar
        {
            public decimal Open, High, Low, Close, Volume, Delta;
            public int BigTradeCountBuy, BigTradeCountSell; // aggressive prints >= BigTradeMin
        }

        public sealed class ConfirmParams
        {
            public decimal ZoneTolerance = 2m;        // price units around the zone
            public decimal MinAbsorbDelta = 150m;     // |delta| of the absorbed bar
            public decimal MaxAbsorbProgress = 0.35m; // body/range at most this fraction in delta's direction
            public decimal MinConfirmDelta = 100m;    // delta of the confirming bar, aligned with its body
            public int MaxBigTradesPerBar = 3;        // SKILL.md: if more, raise the contract filter
        }

        // Long at a support zone (VAL / swing low): sellers absorbed (negative delta, no result),
        // then buyers win in the body of the next candle.
        public static bool LongConfirmed(Bar absorb, Bar confirm, decimal zone, ConfirmParams p)
        {
            bool atZone = Math.Abs(absorb.Low - zone) <= p.ZoneTolerance;
            decimal range = Math.Max(absorb.High - absorb.Low, 0.0001m);
            bool sellersAbsorbed = absorb.Delta <= -p.MinAbsorbDelta
                                   && (absorb.Open - absorb.Close) / range <= p.MaxAbsorbProgress;
            bool buyersWin = confirm.Close > confirm.Open && confirm.Delta >= p.MinConfirmDelta
                             && confirm.Close > absorb.High;
            return atZone && sellersAbsorbed && buyersWin;
        }

        public static bool ShortConfirmed(Bar absorb, Bar confirm, decimal zone, ConfirmParams p)
        {
            bool atZone = Math.Abs(absorb.High - zone) <= p.ZoneTolerance;
            decimal range = Math.Max(absorb.High - absorb.Low, 0.0001m);
            bool buyersAbsorbed = absorb.Delta >= p.MinAbsorbDelta
                                  && (absorb.Close - absorb.Open) / range <= p.MaxAbsorbProgress;
            bool sellersWin = confirm.Close < confirm.Open && confirm.Delta <= -p.MinConfirmDelta
                              && confirm.Close < absorb.Low;
            return atZone && buyersAbsorbed && sellersWin;
        }

        // Reactor filter (effort vs result on the approach): fewer/lighter progress into the zone
        // = counter-side still participating. TODO: needs the approach legs; implement once defined.
        public static bool ApproachShowsStruggle(IList<Bar> approach) => true; // TODO

        // ---------- MODELS NOT YET IMPLEMENTED (need references/*.md) ----------
        // TODO IVB (initial-balance breakout) Model 1 / Model 2, statistical TP
        // TODO Gap model, stair model, momentum trend model, reload trend model, locked-value model
        // TODO Stacked profiles + POC hint + explosion rule, band squeeze filter
    }
}
