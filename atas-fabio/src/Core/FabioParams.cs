using System;

namespace FabioOrderflow.Core
{
    public enum DeltaLevelMode { Balanced, Swing }

    /// <summary>
    /// All tunables in one place. Each default is tagged:
    ///   [Stated]    written in SKILL.md
    ///   [Principle] SKILL.md gives the idea, the number is mine
    ///   [Assumed]   not in SKILL.md; calibrate or replace once the reference files are available
    /// </summary>
    public sealed class FabioParams
    {
        // ---- instrument / session ----
        public decimal TickSize = 0.25m;
        public TimeSpan RthStart = new TimeSpan(9, 30, 0);   // [Stated] value areas come from the regular/cash session; the clock times are yours
        public TimeSpan RthEnd = new TimeSpan(16, 0, 0);
        public bool RthOnly = true;
        public TimeSpan LondonStart = new TimeSpan(3, 0, 0);   // [Assumed] London open in New York time; "London" = from here to the cash open

        // ---- profile ----
        public decimal ValueAreaPct = 0.70m;                 // [Assumed] standard 70% value area

        // ---- bias ----
        public int NarrativeSessions = 5;                    // [Stated] multi-session narrative, >= 5 sessions
        public int CompositionSessions = 60;                 // [Stated] 3-month composition, ~60 sessions
        public int AcceptanceBars = 2;                       // [Assumed] closes needed to call price "accepted"
        public bool StrictValueShift = false;                // [Assumed] false: both VA edges moved the same way; true: no overlap at all
        public bool RequireBiasAlignment = true;             // [Principle] "weigh the entry against the long-term auction"

        // ---- zones ----
        public decimal ZoneToleranceTicks = 6;               // [Assumed]
        public bool UseDeltaLevels = false;                  // [Principle] delta/sniper levels exist; selection rule is mine
        public DeltaLevelMode DeltaMode = DeltaLevelMode.Balanced; // [Stated] balanced: outside VA only; swing: most significant even inside

        // ---- big trades ----
        public int BigTradeMin = 60;                         // [Stated] ~60-75 contracts on the 5-minute chart
        public int MaxBigTradesPerBar = 3;                   // [Stated] more than ~3 per candle -> scale the filter up
        public decimal BigTradeScaleUp = 1.25m;              // [Assumed]
        public int EffortBigPrints = 0;                      // [Assumed] 0 = effort measured by delta only

        // ---- battle of orders ----
        public int StatsWindow = 20;                         // [Assumed]
        public int MinStatsBars = 5;                         // [Assumed]
        public int MaxConfirmBars = 3;                       // [Assumed] bars allowed between absorption and confirmation
        public decimal AbsorbDeltaMult = 1.25m;              // [Principle] "lots of effort": |delta| vs recent average
        public decimal MaxAbsorbProgress = 0.35m;            // [Principle] "no result": share of the range closed in the aggressor's favour
        public decimal ConfirmDeltaMult = 0.75m;             // [Principle] the other side "enters aggressively"
        public decimal MinBodyFraction = 0.50m;              // [Principle] "in the body of the candle"
        public bool RequireBreakAbsorbExtreme = false;       // [Assumed]
        public int FootprintEdgeTicks = 2;                   // [Assumed] rows at the bar's extreme for the footprint absorption read
        public decimal FootprintEdgeShare = 0.35m;           // [Assumed] share of the faded side's aggression printed there

        // ---- reactor approach filter ----
        public int ApproachBars = 6;                         // [Assumed]
        public decimal StruggleEff = 0.75m;                  // [Principle] little progress despite effort
        public decimal ExplosiveEff = 1.75m;                 // [Principle] exploded effortlessly into the level
        public bool RequireApproachFilter = true;            // [Principle]

        // ---- exits ----
        public decimal StopBufferTicks = 2;                  // [Stated] stop "just beyond the zone"; size is mine
        public decimal DefaultRr = 2.0m;                     // [Stated] commonly 1:2
        public decimal MinRr = 1.0m;                         // [Principle] momentum model is quoted ~1:1

        // ---- discipline ----
        public int MaxSignalsPerSession = 3;                 // [Principle] "not a machine gun"
        public int MinBarsBetweenSignals = 3;                // [Assumed]

        // ---- IVB (initial balance breakout) ----
        public bool EnableIvb = false;                       // [Assumed] model named in SKILL.md, rules are in references/deep-charts-models.md
        public int IbMinutes = 60;                           // [Assumed]
    }
}
