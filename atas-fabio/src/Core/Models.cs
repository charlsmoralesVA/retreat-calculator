using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    /// <summary>Everything a model may look at when the engine judges one closed (or forming) cash-session bar.</summary>
    public sealed class ModelContext
    {
        public IReadOnlyList<Bar> Bars;                       // cash-session bars so far (all sessions)
        public IReadOnlyList<PriceLevelVol[]> Levels;         // footprint of each bar, parallel to Bars
        public int Index;                                     // the bar being judged
        public int SessionFirst;                              // first bar of today's cash session
        public SessionSnapshot Prior;                         // the previous cash session (profile, value area, OHLC)
        public IReadOnlyList<SessionSnapshot> Sessions;       // the last sessions, oldest first (up to ~65)
        public ProfileStats Composition;                      // 3-month composition value area (null until ready)
        public IList<Zone> Zones;                             // today's zones from the profile (and IB / delta levels when enabled)
        public IbInfo Ib;
        public Bias Bias;                                     // final bias (value shifts + long-term auction)
        public decimal Price;                                 // close of the bar being judged
        public FabioParams P;
        public EvalContext Eval;                              // footprint and delta levels for the confluence checks
        public bool Closed;
    }

    /// <summary>
    /// One of Fabio's trade models. Each model has its own switch (and its own settings in FabioParams), its own name in
    /// the set-ups, Laya's cards and every backtest report, so models can be backtested alone, compared and tuned.
    ///
    /// Zone models answer for the zones they own, in the order the engine lists the zones (so ties between zones resolve
    /// exactly as before). Other models (trend, gap, opening-range ...) return their candidates from Evaluate.
    /// To add a model: see MODELS_GUIDE.md.
    /// </summary>
    public abstract class SetupModel
    {
        /// <summary>Name shown in set-ups and reports, e.g. "ZoneReaction".</summary>
        public abstract string Name { get; }
        /// <summary>One line: what the model trades, and where the rules come from (Stated / Principle / Assumed).</summary>
        public abstract string Description { get; }
        public abstract bool IsEnabled(FabioParams p);
        public abstract void SetEnabled(FabioParams p, bool on);
        /// <summary>Settings of this model that a sweep may tune (FabioParams field names).</summary>
        public virtual string[] TunableSettings { get { return new string[0]; } }

        public virtual bool OwnsZone(Zone z) { return false; }
        public virtual SetupEvaluation EvaluateZone(ModelContext c, Zone z) { return null; }
        public virtual IEnumerable<SetupEvaluation> Evaluate(ModelContext c) { return Enumerable.Empty<SetupEvaluation>(); }
    }

    /// <summary>SKILL.md's core model: fade VAL / VAH from inside value, the POC as a rejector after acceptance outside, delta levels.</summary>
    public sealed class ZoneReactionModel : SetupModel
    {
        public override string Name { get { return "ZoneReaction"; } }
        public override string Description
        {
            get { return "Battle of orders at the prior value area: VAL / VAH faded from inside value, the POC as a rejector after acceptance outside, delta levels when enabled (SKILL.md: bias -> location -> confirmation)."; }
        }
        public override bool IsEnabled(FabioParams p) { return p.EnableZoneReaction; }
        public override void SetEnabled(FabioParams p, bool on) { p.EnableZoneReaction = on; }
        public override string[] TunableSettings
        {
            get { return new[] { "ZoneToleranceTicks", "AbsorbDeltaMult", "MaxAbsorbProgress", "ConfirmDeltaMult", "MinBodyFraction", "MaxConfirmBars",
                                 "StruggleEff", "ExplosiveEff", "RequireApproachFilter", "RequireBiasAlignment", "UseDeltaLevels", "DefaultRr", "MinRr", "StopBufferTicks" }; }
        }
        public override bool OwnsZone(Zone z) { return z.Kind != ZoneKind.IbHigh && z.Kind != ZoneKind.IbLow; }
        public override SetupEvaluation EvaluateZone(ModelContext c, Zone z)
        {
            return SetupEvaluator.EvaluateZoneReaction(c.Bars, c.Index, c.SessionFirst, z, c.Bias, c.Zones, c.P, Name, c.Eval);
        }
    }

    /// <summary>
    /// Provisional IVB / initial-balance model: a retest of the IB edge after a breakout, judged with the same battle of
    /// orders. The skill names the model; its rules live in the deep-charts reference, so this version is Assumed and off by default.
    /// </summary>
    public sealed class IvbRetestModel : SetupModel
    {
        public override string Name { get { return "IVB-2 (provisional)"; } }
        public override string Description
        {
            get { return "Initial balance: after the first close outside the IB, a retest of its edge won by the breakout side (named in SKILL.md; rules Assumed until taken from the reference)."; }
        }
        public override bool IsEnabled(FabioParams p) { return p.EnableIvb; }
        public override void SetEnabled(FabioParams p, bool on) { p.EnableIvb = on; }
        public override string[] TunableSettings { get { return new[] { "IbMinutes", "ZoneToleranceTicks", "AbsorbDeltaMult", "ConfirmDeltaMult", "DefaultRr" }; } }
        public override bool OwnsZone(Zone z) { return z.Kind == ZoneKind.IbHigh || z.Kind == ZoneKind.IbLow; }
        public override SetupEvaluation EvaluateZone(ModelContext c, Zone z)
        {
            return SetupEvaluator.EvaluateZoneReaction(c.Bars, c.Index, c.SessionFirst, z, c.Bias, c.Zones, c.P, Name, c.Eval);
        }
    }

    public static class ModelRegistry
    {
        /// <summary>Every model the engine knows, in evaluation order. New models are added here (MODELS_GUIDE.md).</summary>
        public static readonly IReadOnlyList<SetupModel> All = new List<SetupModel>
        {
            new ZoneReactionModel(),
            new IvbRetestModel(),
        };

        public static SetupModel Find(string name)
        {
            return All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(m.GetType().Name.Replace("Model", ""), name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A copy of p with only the given model switched on.</summary>
        public static FabioParams Only(FabioParams p, SetupModel model)
        {
            var c = BarBacktest.Clone(p);
            foreach (var m in All) m.SetEnabled(c, m == model);
            return c;
        }

        /// <summary>A copy of p with every model switched on.</summary>
        public static FabioParams AllOn(FabioParams p)
        {
            var c = BarBacktest.Clone(p);
            foreach (var m in All) m.SetEnabled(c, true);
            return c;
        }
    }
}
