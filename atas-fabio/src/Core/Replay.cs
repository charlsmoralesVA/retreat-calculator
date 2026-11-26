using System;
using System.Collections.Generic;

namespace FabioOrderflow.Core
{
    public sealed class ReplayResult
    {
        public List<Bar> Bars = new List<Bar>();
        public List<SetupEvaluation> Signals = new List<SetupEvaluation>();
        public EngineState LastState;
        public int Sessions;
    }

    /// <summary>Raw trades -> time bars with footprint -> engine, exactly as the chart would feed it.</summary>
    public static class Replay
    {
        public static ReplayResult Run(IEnumerable<Tick> ticks, FabioParams p, int barMinutes)
        {
            var agg = new TickAggregator(p, barMinutes);
            var engine = new FabioEngine(p);
            var res = new ReplayResult();
            int idx = 0;

            Action<AggBar> feed = ab =>
            {
                res.Bars.Add(ab.Bar);
                var st = engine.Update(idx, ab.Bar, ab.Levels, false);
                res.Signals.AddRange(st.NewSignals);   // delivered when the NEXT bar arrives, i.e. at the signal bar's close
                res.LastState = st;
                idx++;
            };

            foreach (var t in ticks)
                foreach (var b in agg.Add(t)) feed(b);
            foreach (var b in agg.Flush()) feed(b);

            res.Sessions = engine.CompletedSessions;
            return res;
        }
    }
}
