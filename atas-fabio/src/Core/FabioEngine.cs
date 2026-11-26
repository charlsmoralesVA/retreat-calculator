using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    public sealed class EngineState
    {
        public bool InRth;
        public bool Ready;                       // at least one completed prior session exists
        public SessionSnapshot Prior;
        public Bias DailyShift, Narrative, Final;
        public AuctionPosition Auction;
        public ValueLocation Location;
        public string BiasNote = "";
        public ProfileStats Composition;
        public List<Zone> Zones = new List<Zone>();
        public IbInfo Ib;
        public GapInfo Gap;
        /// <summary>Checklist of the most relevant zone right now (monitoring; may still be "waiting").</summary>
        public SetupEvaluation Best;
        /// <summary>Confirmed signals on bars that have just closed.</summary>
        public List<SetupEvaluation> NewSignals = new List<SetupEvaluation>();
        public int SignalsThisSession;
        /// <summary>Session VWAP (context only: SKILL.md lists VWAP as a concept but gives no trade rule for it here).</summary>
        public decimal Vwap;
        public decimal LastPrice;
        public DateTime LastBarTime;
    }

    /// <summary>
    /// Stateful driver. Feed it one chart bar at a time (and again on every tick of the forming bar).
    ///  * Profiles use COMPLETED regular-session bars only, so repeated ticks never double count.
    ///  * A bar is judged for a signal exactly once, when the next bar arrives (i.e. at its close).
    ///  * ETH bars are ignored when RthOnly is set.
    /// </summary>
    public sealed class FabioEngine
    {
        private readonly FabioParams _p;
        private readonly SessionClock _clock;

        private readonly List<Bar> _rth = new List<Bar>();
        private readonly List<int> _rthGlobal = new List<int>();
        private readonly List<DateTime> _rthKey = new List<DateTime>();
        private readonly List<PriceLevelVol[]> _rthLevels = new List<PriceLevelVol[]>();
        private readonly List<SessionSnapshot> _sessions = new List<SessionSnapshot>();

        private int _accum = -1;
        private DateTime? _curKey;
        private int _curFirst;
        private VolumeProfile _curProfile = new VolumeProfile();
        private decimal _curOpen, _curHigh, _curLow, _curClose;
        private decimal _vwapPv, _vwapV;
        private readonly List<decimal> _sessionCloses = new List<decimal>();
        private readonly List<decimal> _allCloses = new List<decimal>();
        private ValueLocation _location = ValueLocation.Inside;
        private AuctionPosition _auction = AuctionPosition.Inside;
        private Bias _narrative = Bias.Neutral, _dailyShift = Bias.Neutral;
        private ProfileStats _comp;
        private int _signalsThisSession, _lastSignalRth = -1000, _lastSignalAbsorb = -1;
        private readonly List<SetupEvaluation> _pending = new List<SetupEvaluation>();
        private bool _hasData;
        private EngineState _last;

        public FabioEngine(FabioParams p)
        {
            _p = p;
            _clock = new SessionClock(p.RthStart, p.RthEnd);
        }

        public int CompletedSessions { get { return _sessions.Count; } }

        public void Reset()
        {
            _rth.Clear(); _rthGlobal.Clear(); _rthKey.Clear(); _rthLevels.Clear(); _sessions.Clear();
            _accum = -1; _curKey = null; _curFirst = 0; _curProfile = new VolumeProfile();
            _sessionCloses.Clear(); _allCloses.Clear();
            _location = ValueLocation.Inside; _auction = AuctionPosition.Inside;
            _narrative = Bias.Neutral; _dailyShift = Bias.Neutral; _comp = null;
            _signalsThisSession = 0; _lastSignalRth = -1000; _lastSignalAbsorb = -1;
            _pending.Clear(); _last = null;
        }

        public EngineState Update(int index, Bar bar, PriceLevelVol[] levels, bool forming)
        {
            if (index == 0 && _hasData) Reset();
            _hasData = true;

            DateTime? key = _clock.SessionKey(bar.Time);
            if (!key.HasValue && _p.RthOnly) return Snapshot(false);
            DateTime sessionKey = key ?? bar.Time.Date;

            int last = _rth.Count - 1;
            if (last >= 0 && _rthGlobal[last] == index)
            {
                _rth[last] = bar; _rthLevels[last] = levels ?? new PriceLevelVol[0];   // tick on the forming bar
            }
            else if (last < 0 || index > _rthGlobal[last])
            {
                CloseThrough(last);                                                    // the previous bar is now final
                _rth.Add(bar); _rthGlobal.Add(index); _rthKey.Add(sessionKey); _rthLevels.Add(levels ?? new PriceLevelVol[0]);
            }
            else return _last ?? Snapshot(false);                                      // older bar: ignore

            int cur = _rth.Count - 1;
            SwitchSession(cur);
            var st = Snapshot(true);
            st.Best = Evaluate(cur, false);
            st.NewSignals.AddRange(_pending);
            _pending.Clear();
            st.SignalsThisSession = _signalsThisSession;
            _last = st;
            return st;
        }

        // ------------------------------------------------------------------ internals

        private void CloseThrough(int through)
        {
            for (int k = _accum + 1; k <= through; k++)
            {
                SwitchSession(k);
                Evaluate(k, true);     // registers a signal in _pending when everything required is met
                Accumulate(k);
            }
        }

        private void SwitchSession(int k)
        {
            DateTime key = _rthKey[k];
            if (_curKey.HasValue && _curKey.Value == key) return;
            if (_curKey.HasValue) FinalizeSession();
            _curKey = key; _curFirst = k; _curProfile = new VolumeProfile();
            _curOpen = _rth[k].Open; _curHigh = _rth[k].High; _curLow = _rth[k].Low; _curClose = _rth[k].Close;
            _sessionCloses.Clear();
            _vwapPv = 0; _vwapV = 0;
            _location = ValueLocation.Inside;
            _signalsThisSession = 0; _lastSignalRth = -1000; _lastSignalAbsorb = -1;
        }

        private void FinalizeSession()
        {
            var stats = _curProfile.Compute(_p.TickSize, _p.ValueAreaPct);
            if (stats == null) return;
            _sessions.Add(new SessionSnapshot
            {
                Date = _curKey.Value, Profile = _curProfile, Stats = stats,
                Open = _curOpen, High = _curHigh, Low = _curLow, Close = _curClose
            });
            int keep = Math.Max(_p.CompositionSessions, _p.NarrativeSessions) + 5;
            while (_sessions.Count > keep) _sessions.RemoveAt(0);
            _dailyShift = BiasEngine.LastShift(_sessions, _p.StrictValueShift);
            _narrative = BiasEngine.Narrative(_sessions, _p.NarrativeSessions, _p.StrictValueShift);
            _comp = BiasEngine.Composition(_sessions, _p.CompositionSessions, _p.TickSize, _p.ValueAreaPct, _p.NarrativeSessions);
        }

        private void Accumulate(int k)
        {
            var b = _rth[k];
            foreach (var l in _rthLevels[k]) { _curProfile.Add(l); _vwapPv += l.Price * (l.Bid + l.Ask); _vwapV += l.Bid + l.Ask; }
            _curHigh = Math.Max(_curHigh, b.High);
            _curLow = Math.Min(_curLow, b.Low);
            _curClose = b.Close;
            _sessionCloses.Add(b.Close);
            _allCloses.Add(b.Close);
            if (_allCloses.Count > 500) _allCloses.RemoveRange(0, _allCloses.Count - 500);
            if (_sessions.Count > 0) _location = ZoneEngine.Locate(_sessionCloses, _sessions[_sessions.Count - 1].Stats, _p.AcceptanceBars);
            _auction = BiasEngine.ClassifyAuction(_allCloses, _comp, _p.AcceptanceBars, _auction);
            _accum = k;
        }

        private List<Bar> SessionBars()
        {
            return _rth.GetRange(_curFirst, _rth.Count - _curFirst);
        }

        private List<Zone> SessionDeltaLevels(decimal price)
        {
            if (_curProfile.IsEmpty) return new List<Zone>();
            var devStats = _curProfile.Compute(_p.TickSize, _p.ValueAreaPct);
            return ZoneEngine.DeltaLevels(_curProfile, devStats, price, _p.DeltaMode);
        }

        private List<Zone> BuildZones(SessionSnapshot prior, decimal price, out IbInfo ib)
        {
            var zones = ZoneEngine.Build(prior.Stats, _location, _p);
            if (_p.UseDeltaLevels) zones.AddRange(SessionDeltaLevels(price));
            ib = InitialBalance.Compute(SessionBars(), _p.RthStart, _p.IbMinutes);
            if (_p.EnableIvb && ib.Complete && ib.Breakout == Side.Long)
                zones.Add(new Zone { Kind = ZoneKind.IbHigh, Price = ib.High, Candidate = Side.Long, Label = "IB high (retest after breakout up)", Source = Provenance.Assumed });
            if (_p.EnableIvb && ib.Complete && ib.Breakout == Side.Short)
                zones.Add(new Zone { Kind = ZoneKind.IbLow, Price = ib.Low, Candidate = Side.Short, Label = "IB low (retest after breakout down)", Source = Provenance.Assumed });
            return zones;
        }

        private SetupEvaluation Evaluate(int k, bool closed)
        {
            if (_sessions.Count == 0) return null;
            var prior = _sessions[_sessions.Count - 1];
            decimal price = _rth[k].Close;
            IbInfo ib;
            var zones = BuildZones(prior, price, out ib);
            string note;
            Bias final = BiasEngine.FinalBias(_narrative, _auction, _comp != null, out note);
            var ctx = new EvalContext { Levels = _rthLevels, DeltaLevels = SessionDeltaLevels(price) };

            SetupEvaluation best = null;
            foreach (var z in zones)
            {
                if (z.Candidate == Side.None) continue;
                string model = (z.Kind == ZoneKind.IbHigh || z.Kind == ZoneKind.IbLow) ? "IVB-2 (provisional)" : "ZoneReaction";
                var ev = SetupEvaluator.EvaluateZoneReaction(_rth, k, _curFirst, z, final, zones, _p, model, ctx);
                ev.Closed = closed;
                ev.ConfirmGlobalIndex = _rthGlobal[k];
                if (IsBetter(ev, best, price)) best = ev;
            }

            if (closed && best != null && best.AllRequiredMet
                && _signalsThisSession < _p.MaxSignalsPerSession
                && k - _lastSignalRth >= _p.MinBarsBetweenSignals
                && best.AbsorbIndex != _lastSignalAbsorb)
            {
                _signalsThisSession++; _lastSignalRth = k; _lastSignalAbsorb = best.AbsorbIndex;
                _pending.Add(best);
            }
            return best;
        }

        private static bool IsBetter(SetupEvaluation ev, SetupEvaluation best, decimal price)
        {
            if (best == null) return true;
            if (ev.AllRequiredMet != best.AllRequiredMet) return ev.AllRequiredMet;
            if (ev.Score != best.Score) return ev.Score > best.Score;
            return Math.Abs(ev.Zone.Price - price) < Math.Abs(best.Zone.Price - price);
        }

        private EngineState Snapshot(bool inRth)
        {
            var st = new EngineState { InRth = inRth };
            st.Prior = _sessions.Count > 0 ? _sessions[_sessions.Count - 1] : null;
            st.Ready = st.Prior != null;
            st.DailyShift = _dailyShift; st.Narrative = _narrative; st.Auction = _auction; st.Location = _location;
            st.Composition = _comp;
            string note;
            st.Final = BiasEngine.FinalBias(_narrative, _auction, _comp != null, out note);
            st.BiasNote = note;
            if (inRth && st.Ready && _rth.Count > 0)
            {
                IbInfo ib;
                st.Zones = BuildZones(st.Prior, _rth[_rth.Count - 1].Close, out ib);
                st.Ib = ib;
                st.Gap = InitialBalance.ComputeGap(st.Prior.Close, SessionBars());
            }
            if (inRth && _rth.Count > 0)
            {
                var lastBar = _rth[_rth.Count - 1];
                st.LastPrice = lastBar.Close; st.LastBarTime = lastBar.Time;
                decimal pv = _vwapPv, v = _vwapV;
                if (_accum < _rth.Count - 1 && _rthKey[_rth.Count - 1] == _curKey)   // include the forming bar
                    foreach (var l in _rthLevels[_rth.Count - 1]) { pv += l.Price * (l.Bid + l.Ask); v += l.Bid + l.Ask; }
                st.Vwap = v > 0 ? pv / v : 0;
            }
            return st;
        }
    }
}
