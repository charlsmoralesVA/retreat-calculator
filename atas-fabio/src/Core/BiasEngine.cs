using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    public sealed class SessionSnapshot
    {
        public DateTime Date;
        public VolumeProfile Profile;
        public ProfileStats Stats;
        public decimal Open, High, Low, Close;
    }

    /// <summary>
    /// Pillar 1 - bias (SKILL.md):
    ///  * value shifting higher day over day = bullish, lower = bearish, overlapping = neutral
    ///  * one counter-shift inside a longer trend is usually just a retracement
    ///  * the auction only truly flips when price accepts back inside the composition (3-month) value area
    ///  * keep a multi-session narrative (>= 5 sessions), not a one-day view
    /// </summary>
    public static class BiasEngine
    {
        public static Bias ValueShift(ProfileStats prev, ProfileStats cur, bool strict)
        {
            if (prev == null || cur == null) return Bias.Neutral;
            if (strict)
            {
                if (cur.Val > prev.Vah) return Bias.Bullish;
                if (cur.Vah < prev.Val) return Bias.Bearish;
                return Bias.Neutral;   // any overlap = neutral
            }
            if (cur.Val > prev.Val && cur.Vah > prev.Vah) return Bias.Bullish;
            if (cur.Val < prev.Val && cur.Vah < prev.Vah) return Bias.Bearish;
            return Bias.Neutral;
        }

        /// <summary>Majority direction of the value shifts over the last n sessions (a lone counter-shift is outvoted).</summary>
        public static Bias Narrative(IList<SessionSnapshot> sessions, int n, bool strict)
        {
            if (sessions.Count < 2) return Bias.Neutral;
            int up = 0, dn = 0;
            for (int i = Math.Max(1, sessions.Count - n + 1); i < sessions.Count; i++)
            {
                var b = ValueShift(sessions[i - 1].Stats, sessions[i].Stats, strict);
                if (b == Bias.Bullish) up++; else if (b == Bias.Bearish) dn++;
            }
            return up > dn ? Bias.Bullish : dn > up ? Bias.Bearish : Bias.Neutral;
        }

        public static Bias LastShift(IList<SessionSnapshot> sessions, bool strict)
        {
            if (sessions.Count < 2) return Bias.Neutral;
            return ValueShift(sessions[sessions.Count - 2].Stats, sessions[sessions.Count - 1].Stats, strict);
        }

        /// <summary>Merged profile of the last k completed sessions (the "composition profile").</summary>
        public static ProfileStats Composition(IList<SessionSnapshot> sessions, int k, decimal tick, decimal vaPct, int minSessions)
        {
            if (sessions.Count < minSessions) return null;
            var merged = new VolumeProfile();
            for (int i = Math.Max(0, sessions.Count - k); i < sessions.Count; i++) merged.Merge(sessions[i].Profile);
            return merged.Compute(tick, vaPct);
        }

        /// <summary>
        /// Inside only after acceptance: the last N closes all inside the composition value area.
        /// Otherwise follow the last close outside; if the last close is inside but acceptance is not yet
        /// complete, keep the previous reading (the auction has not flipped yet).
        /// </summary>
        public static AuctionPosition ClassifyAuction(IList<decimal> closes, ProfileStats comp, int acceptanceBars, AuctionPosition previous)
        {
            if (comp == null || closes.Count == 0) return AuctionPosition.Inside;
            int n = Math.Max(1, acceptanceBars);
            if (closes.Count >= n)
            {
                bool allInside = true;
                for (int i = closes.Count - n; i < closes.Count; i++)
                    if (closes[i] > comp.Vah || closes[i] < comp.Val) { allInside = false; break; }
                if (allInside) return AuctionPosition.Inside;
            }
            decimal last = closes[closes.Count - 1];
            if (last > comp.Vah) return AuctionPosition.Above;
            if (last < comp.Val) return AuctionPosition.Below;
            return previous;
        }

        /// <summary>Long-term auction wins while price is outside the composition value area; inside it the short-term narrative decides.</summary>
        public static Bias FinalBias(Bias narrative, AuctionPosition auction, bool compositionKnown, out string note)
        {
            if (!compositionKnown)
            {
                note = "composition not available yet; narrative only";
                return narrative;
            }
            if (auction == AuctionPosition.Above)
            {
                note = narrative == Bias.Bearish ? "above composition VA: bearish shifts = retracement" : "above composition VA";
                return Bias.Bullish;
            }
            if (auction == AuctionPosition.Below)
            {
                note = narrative == Bias.Bullish ? "below composition VA: bullish shifts = retracement" : "below composition VA";
                return Bias.Bearish;
            }
            note = "inside composition VA (balanced long term); short-term narrative decides";
            return narrative;
        }
    }
}
