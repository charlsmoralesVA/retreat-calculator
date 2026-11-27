using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FabioOrderflow.Core
{
    /// <summary>One trade as the vendor sent it: UTC time, the contract, the aggressor side.</summary>
    public struct RawTrade
    {
        public DateTime Utc;
        public decimal Price, Size;
        public Side Aggressor;
        public string Symbol;
    }

    /// <summary>
    /// Reads Databento "trades" CSV (dataset GLBX.MDP3, CME Globex). Works with the raw encoding (nanosecond timestamps,
    /// prices as integers in units of 1e-9) and with "pretty" timestamps / prices. Columns are found by name in the
    /// header: ts_event (or ts_recv), price, size, side, and optionally action and symbol. Databento's side is the
    /// aggressor: B = buyer lifted the offer, A = seller hit the bid, N = not given (the aggregator then uses the tick rule).
    /// Calendar spreads (symbols with '-') are skipped. Rows whose action is not 'T' (trade) are skipped.
    /// RawTrade.Symbol identifies the contract: the raw symbol when given (ESH4), else "id" + instrument_id.
    /// </summary>
    public static class DatabentoCsv
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static IEnumerable<RawTrade> Read(TextReader r, string fallbackSymbol = "")
        {
            string header = r.ReadLine();
            if (header == null) yield break;
            var cols = header.Trim().Split(',');
            int iTs = Array.IndexOf(cols, "ts_event"), iRecv = Array.IndexOf(cols, "ts_recv");
            if (iTs < 0) iTs = iRecv;
            int iPx = Array.IndexOf(cols, "price"), iSz = Array.IndexOf(cols, "size"), iSide = Array.IndexOf(cols, "side");
            int iAct = Array.IndexOf(cols, "action"), iSym = Array.IndexOf(cols, "symbol"), iId = Array.IndexOf(cols, "instrument_id");
            if (iTs < 0 || iPx < 0 || iSz < 0)
                throw new InvalidDataException("not a Databento trades CSV: the header needs ts_event (or ts_recv), price and size; got: " + header);
            int need = Math.Max(Math.Max(Math.Max(iTs, iPx), Math.Max(Math.Max(iSz, iSide), Math.Max(iAct, iSym))), iId) + 1;

            string line; long n = 1;
            var f = new string[cols.Length + 4];
            while ((line = r.ReadLine()) != null)
            {
                n++;
                if (line.Length == 0) continue;
                int count = SplitInto(line, f);
                if (count < need) throw new InvalidDataException("line " + n + ": expected " + need + " columns, got " + count);
                if (iAct >= 0 && f[iAct] != "T") continue;
                string sym = iSym >= 0 ? f[iSym] : null;
                if (sym != null && sym.IndexOf('-') >= 0) continue;          // calendar spread
                // the contract's identity: the raw symbol (ESH4); for continuous / parent symbols (ES.v.0, ES.FUT) or
                // without a symbol column, the instrument id, which changes when the contract does
                if (string.IsNullOrEmpty(sym) || sym.IndexOf('.') >= 0) sym = iId >= 0 ? "id" + f[iId] : (fallbackSymbol ?? "");
                decimal size;
                if (!decimal.TryParse(f[iSz], NumberStyles.Float, Inv, out size) || size <= 0) continue;
                var t = new RawTrade
                {
                    Utc = ParseTime(f[iTs], n),
                    Price = ParsePrice(f[iPx], n),
                    Size = size,
                    Symbol = sym ?? "",
                    Aggressor = iSide < 0 ? Side.None : f[iSide] == "B" ? Side.Long : f[iSide] == "A" ? Side.Short : Side.None
                };
                if (t.Price <= 0) continue;                                     // undefined price
                yield return t;
            }
        }

        private static int SplitInto(string line, string[] f)
        {
            int count = 0, start = 0;
            for (int i = 0; i <= line.Length; i++)
            {
                if (i == line.Length || line[i] == ',')
                {
                    if (count < f.Length) f[count] = line.Substring(start, i - start);
                    count++; start = i + 1;
                }
            }
            return count;
        }

        public static DateTime ParseTime(string s, long line)
        {
            long ns;
            if (s.Length > 0 && char.IsDigit(s[0]) && long.TryParse(s, NumberStyles.None, Inv, out ns))
                return Epoch.AddTicks(ns / 100);
            DateTimeOffset dto;
            // pretty timestamps carry nanoseconds: keep at most 7 fractional digits for .NET
            string t = s;
            int dot = t.IndexOf('.');
            if (dot > 0)
            {
                int end = dot + 1;
                while (end < t.Length && char.IsDigit(t[end])) end++;
                if (end - dot - 1 > 7) t = t.Substring(0, dot + 8) + t.Substring(end);
            }
            if (DateTimeOffset.TryParse(t, Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dto))
                return dto.UtcDateTime;
            throw new InvalidDataException("line " + line + ": bad timestamp '" + s + "'");
        }

        public static decimal ParsePrice(string s, long line)
        {
            if (s.IndexOf('.') >= 0 || s.IndexOf('e') >= 0 || s.IndexOf('E') >= 0)
            {
                decimal d;
                if (decimal.TryParse(s, NumberStyles.Float, Inv, out d)) return d;
            }
            else
            {
                long raw;
                if (long.TryParse(s, NumberStyles.AllowLeadingSign, Inv, out raw))
                {
                    if (raw == long.MaxValue) return 0;                       // Databento's "undefined price"
                    return raw / 1000000000m;
                }
            }
            throw new InvalidDataException("line " + line + ": bad price '" + s + "'");
        }
    }

    /// <summary>
    /// Turns the trades of all contracts of one product into one continuous front-month stream, in exchange time.
    ///  * Front month for trading date D = the outright with the most volume on D-1 (CME trading date: the session that
    ///    opens at 18:00 New York belongs to the next day). The first trading date only measures volume.
    ///  * Rolls go forward only: a contract that was the front once and was replaced never comes back.
    ///  * Roll offset = last price of the new contract - last price of the old contract on D-1, both from the same day's
    ///    trades. With a single-contract feed (e.g. a continuous symbol) the offset falls back to the price gap between the
    ///    two contracts' consecutive trades, which includes the market's move in between: a warning is recorded.
    /// </summary>
    public sealed class FrontMonthStitcher
    {
        private readonly TimeZoneInfo _zone;
        private DateTime? _date;
        private Dictionary<string, decimal> _vol = new Dictionary<string, decimal>();
        private Dictionary<string, decimal> _last = new Dictionary<string, decimal>();
        private Dictionary<string, decimal> _prevVol, _prevLast;
        private readonly HashSet<string> _retired = new HashSet<string>();
        private string _front;
        private decimal _lastEmittedPrice;
        private readonly List<RollEvent> _rolls = new List<RollEvent>();
        public readonly List<string> Warnings = new List<string>();
        public long Read, Emitted;
        public IReadOnlyList<RollEvent> Rolls { get { return _rolls; } }

        public FrontMonthStitcher(TimeZoneInfo zone) { _zone = zone; }

        /// <summary>Returns the trade as a tick in exchange time when it belongs to the front month, else null. newRoll is set on the trade that starts a new contract.</summary>
        public Tick Add(RawTrade t, out RollEvent newRoll, out string symbol)
        {
            newRoll = null; symbol = null;
            Read++;
            var local = ExchangeTime.FromUtc(t.Utc, _zone);
            var td = BarFile.TradingDate(local);
            if (_date == null || td > _date.Value)
            {
                if (_date != null) { _prevVol = _vol; _prevLast = _last; _vol = new Dictionary<string, decimal>(); _last = new Dictionary<string, decimal>(); }
                _date = td;
                newRoll = ChooseFront(td);
            }

            decimal v; _vol.TryGetValue(t.Symbol, out v); _vol[t.Symbol] = v + t.Size;
            _last[t.Symbol] = t.Price;

            if (_front == null || t.Symbol != _front) return null;
            symbol = _front;
            _lastEmittedPrice = t.Price;
            Emitted++;
            return new Tick { Time = local, Price = t.Price, Volume = t.Size, Aggressor = t.Aggressor };
        }

        private RollEvent ChooseFront(DateTime td)
        {
            if (_prevVol == null || _prevVol.Count == 0) return null;
            string best = null; decimal bestV = -1;
            foreach (var kv in _prevVol)
                if (!_retired.Contains(kv.Key) && kv.Value > bestV) { best = kv.Key; bestV = kv.Value; }
            if (best == null) return null;
            if (_front == null) { _front = best; return null; }
            if (best == _front) return null;

            // a roll
            decimal oldLast, newLast; RollEvent r;
            if (_prevLast.TryGetValue(_front, out oldLast) && _prevLast.TryGetValue(best, out newLast))
                r = new RollEvent { TradingDate = td, OldSymbol = _front, NewSymbol = best, Offset = newLast - oldLast, Method = "same-day-last-prices" };
            else
            {
                decimal guess = _prevLast.TryGetValue(best, out newLast) ? newLast - _lastEmittedPrice : 0;
                r = new RollEvent { TradingDate = td, OldSymbol = _front, NewSymbol = best, Offset = guess, Method = "price-gap" };
                Warnings.Add(string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd} roll {1} -> {2}: the old contract has no trades that day, offset estimated from the price gap", td, _front, best));
            }
            _retired.Add(_front);
            _front = best;
            _rolls.Add(r);
            return r;
        }
    }

    /// <summary>Raw trades -> front-month ticks -> 5-minute (or other) bars with footprint -> bar file.</summary>
    public sealed class TradeImporter
    {
        private readonly TickAggregator _agg;
        private readonly TimeZoneInfo _zone;
        private readonly BarFile.Writer _w;
        private readonly FrontMonthStitcher _stitch;
        private readonly bool _singleSymbolMode;
        private string _curSymbol;
        private string _singlePrevSymbol; private decimal _singlePrevPrice; private bool _singleHas;
        private readonly HashSet<string> _singleRetired = new HashSet<string>();
        private long _singleIgnored;
        public int Bars { get; private set; }
        public DateTime FirstTime = DateTime.MaxValue, LastTime = DateTime.MinValue;
        public readonly List<string> Warnings = new List<string>();
        public FrontMonthStitcher Stitcher { get { return _stitch; } }

        /// <param name="singleSymbolMode">true when the feed holds one contract at a time (continuous symbol, or no symbol column)</param>
        public TradeImporter(FabioParams p, int minutes, BarFile.Writer w, TimeZoneInfo zone, bool singleSymbolMode)
        {
            _agg = new TickAggregator(p, minutes); _w = w; _stitch = new FrontMonthStitcher(zone); _singleSymbolMode = singleSymbolMode;
            _zone = zone;
        }

        public void Add(RawTrade t)
        {
            Tick tick; string sym; RollEvent roll = null;
            if (_singleSymbolMode)
            {
                var local = ExchangeTime.FromUtc(t.Utc, _zone);
                if (_singleRetired.Contains(t.Symbol))
                {
                    if (_singleIgnored++ == 0) Warnings.Add("trades of an earlier contract appeared after its roll and were ignored (" + t.Symbol + ")");
                    return;
                }
                if (_singleHas && t.Symbol != _singlePrevSymbol)
                {
                    _singleRetired.Add(_singlePrevSymbol);
                    roll = new RollEvent { TradingDate = BarFile.TradingDate(local), OldSymbol = _singlePrevSymbol, NewSymbol = t.Symbol, Offset = t.Price - _singlePrevPrice, Method = "price-gap" };
                    Warnings.Add(string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd} contract change {1} -> {2}: offset {3} estimated from the price gap (download the parent symbol, e.g. ES.FUT, for exact rolls)", roll.TradingDate, roll.OldSymbol, roll.NewSymbol, roll.Offset));
                }
                _singlePrevSymbol = t.Symbol; _singlePrevPrice = t.Price; _singleHas = true;
                tick = new Tick { Time = local, Price = t.Price, Volume = t.Size, Aggressor = t.Aggressor };
                sym = t.Symbol;
            }
            else
            {
                tick = _stitch.Add(t, out roll, out sym);
            }
            if (roll != null)
            {
                foreach (var b in _agg.Flush()) WriteBar(b);       // the old contract's last bar ends here
                _w.WriteRoll(roll);
            }
            if (tick == null) return;
            if (sym != _curSymbol) _curSymbol = sym;
            foreach (var b in _agg.Add(tick)) WriteBar(b);
        }

        public void Finish()
        {
            foreach (var b in _agg.Flush()) WriteBar(b);
            Warnings.AddRange(_stitch.Warnings);
        }

        private void WriteBar(AggBar b)
        {
            _w.Write(new BarRecord { Bar = b.Bar, Levels = b.Levels, Symbol = _curSymbol ?? "" });
            Bars++;
            if (b.Bar.Time < FirstTime) FirstTime = b.Bar.Time;
            if (b.Bar.Time > LastTime) LastTime = b.Bar.Time;
        }
    }
}
