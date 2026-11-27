using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace FabioOrderflow.Core
{
    /// <summary>A contract roll: from TradingDate on, NewSymbol is the front month. Offset = new price - old price at the roll.</summary>
    public sealed class RollEvent
    {
        public DateTime TradingDate;
        public string OldSymbol, NewSymbol;
        public decimal Offset;
        public string Method = "";      // how the offset was measured
    }

    public sealed class BarFileHeader
    {
        public string Instrument = "";
        public decimal TickSize = 0.25m;
        public int Minutes = 5;
        public string TimeZone = "America/New_York";
        public string Source = "";
        public bool Synthetic;
    }

    /// <summary>One bar with its footprint, in exchange time, at the contract's own (unadjusted) prices.</summary>
    public sealed class BarRecord
    {
        public Bar Bar;
        public PriceLevelVol[] Levels;
        public string Symbol = "";
    }

    /// <summary>
    /// Bars with footprint in memory, ready for the engine. Prices are back-adjusted across rolls so that the latest
    /// contract keeps its real prices and the profiles of older sessions line up with it.
    /// </summary>
    public sealed class BarSeries
    {
        public BarFileHeader Header = new BarFileHeader();
        public List<Bar> Bars = new List<Bar>();
        public List<PriceLevelVol[]> Levels = new List<PriceLevelVol[]>();
        public List<RollEvent> Rolls = new List<RollEvent>();
        public int Sessions;
        public DateTime First { get { return Bars.Count == 0 ? DateTime.MinValue : Bars[0].Time; } }
        public DateTime Last { get { return Bars.Count == 0 ? DateTime.MinValue : Bars[Bars.Count - 1].Time; } }
    }

    /// <summary>
    /// The bar file: gzip text, one line per bar, footprint included, so years of trades are read once and then
    /// replayed in seconds. Written by the importer (Databento / CSV trades) and by the ATAS indicator's export.
    ///   #FABIO-BARS 1
    ///   #instrument=ES tick=0.25 minutes=5 tz=America/New_York source=... synthetic=false
    ///   R,2024-03-14,ESH4,ESM4,52.25,method
    ///   B,2024-03-14T09:30:00,ESM4,open,high,low,close,volume,delta,bigBuys,bigSells|firstPrice|bid:ask,bid:ask,...
    /// The footprint lists every tick from firstPrice upwards; an empty entry means no volume at that price.
    /// </summary>
    public static class BarFile
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const string Magic = "#FABIO-BARS 1";

        public sealed class Writer : IDisposable
        {
            private readonly Stream _file;
            private readonly GZipStream _gz;
            private readonly StreamWriter _w;
            private readonly decimal _tick;
            public int Count { get; private set; }

            public Writer(string path, BarFileHeader h)
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _file = File.Create(path);
                _gz = new GZipStream(_file, CompressionLevel.Optimal);
                _w = new StreamWriter(_gz, new UTF8Encoding(false));
                _tick = h.TickSize;
                _w.WriteLine(Magic);
                _w.WriteLine(string.Format(Inv, "#instrument={0} tick={1} minutes={2} tz={3} source={4} synthetic={5}",
                    Clean(h.Instrument), h.TickSize, h.Minutes, Clean(h.TimeZone), Clean(h.Source), h.Synthetic ? "true" : "false"));
            }

            public void WriteRoll(RollEvent r)
            {
                _w.WriteLine(string.Format(Inv, "R,{0:yyyy-MM-dd},{1},{2},{3},{4}", r.TradingDate, Clean(r.OldSymbol), Clean(r.NewSymbol), r.Offset, Clean(r.Method)));
            }

            public void Write(BarRecord rec)
            {
                var b = rec.Bar;
                var sb = new StringBuilder(256);
                sb.Append("B,").Append(b.Time.ToString("yyyy-MM-ddTHH:mm:ss", Inv)).Append(',').Append(Clean(rec.Symbol)).Append(',')
                  .Append(D(b.Open)).Append(',').Append(D(b.High)).Append(',').Append(D(b.Low)).Append(',').Append(D(b.Close)).Append(',')
                  .Append(D(b.Volume)).Append(',').Append(D(b.Delta)).Append(',').Append(b.BigBuys.ToString(Inv)).Append(',').Append(b.BigSells.ToString(Inv));
                var lv = rec.Levels;
                if (lv != null && lv.Length > 0)
                {
                    var sorted = lv.OrderBy(x => x.Price).ToArray();
                    decimal first = sorted[0].Price;
                    sb.Append('|').Append(D(first)).Append('|');
                    int idx = 0;
                    int n = (int)Math.Round((sorted[sorted.Length - 1].Price - first) / _tick) + 1;
                    if (n > 100000) throw new InvalidDataException("footprint spans too many ticks; is the tick size right?");
                    for (int i = 0; i < n; i++)
                    {
                        if (i > 0) sb.Append(',');
                        decimal px = first + i * _tick;
                        decimal bid = 0, ask = 0; bool any = false;
                        while (idx < sorted.Length && sorted[idx].Price <= px + _tick / 2)
                        {
                            bid += sorted[idx].Bid; ask += sorted[idx].Ask; any = true; idx++;
                        }
                        if (any && (bid != 0 || ask != 0)) sb.Append(D(bid)).Append(':').Append(D(ask));
                    }
                }
                _w.WriteLine(sb.ToString());
                Count++;
            }

            public void Dispose()
            {
                _w.Flush(); _w.Dispose();
            }
        }

        private static string Clean(string s) { return string.IsNullOrEmpty(s) ? "" : s.Replace(",", "_").Replace(" ", "_").Replace("|", "_"); }

        private static string D(decimal v) { return (v / 1.000000000000000000000000000000000m).ToString(Inv); }

        public static bool LooksLikeBarFile(string path)
        {
            try
            {
                using (var r = OpenText(path)) return r.ReadLine() == Magic;
            }
            catch (Exception) { return false; }
        }

        public static TextReader OpenText(string path)
        {
            Stream s = File.OpenRead(path);
            var head = new byte[2];
            int n = s.Read(head, 0, 2);
            s.Seek(0, SeekOrigin.Begin);
            if (n == 2 && head[0] == 0x1f && head[1] == 0x8b) s = new GZipStream(s, CompressionMode.Decompress);
            return new StreamReader(s, Encoding.UTF8);
        }

        /// <summary>Streams the file: the header first (via the out callback), then rolls and bars in file order.</summary>
        public static IEnumerable<object> ReadAll(TextReader r, Action<BarFileHeader> onHeader)
        {
            string line = r.ReadLine();
            if (line != Magic) throw new InvalidDataException("not a Fabio bar file (first line must be '" + Magic + "')");
            var h = new BarFileHeader();
            int lineNo = 1;
            bool headerDone = false;
            while ((line = r.ReadLine()) != null)
            {
                lineNo++;
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    foreach (var kv in line.Substring(1).Split(' '))
                    {
                        int eq = kv.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = kv.Substring(0, eq), v = kv.Substring(eq + 1);
                        if (k == "instrument") h.Instrument = v;
                        else if (k == "tick") h.TickSize = decimal.Parse(v, Inv);
                        else if (k == "minutes") h.Minutes = int.Parse(v, Inv);
                        else if (k == "tz") h.TimeZone = v;
                        else if (k == "source") h.Source = v;
                        else if (k == "synthetic") h.Synthetic = v == "true";
                    }
                    continue;
                }
                if (!headerDone) { headerDone = true; if (onHeader != null) onHeader(h); }
                if (line[0] == 'R')
                {
                    var f = line.Split(',');
                    yield return new RollEvent
                    {
                        TradingDate = DateTime.ParseExact(f[1], "yyyy-MM-dd", Inv), OldSymbol = f[2], NewSymbol = f[3],
                        Offset = decimal.Parse(f[4], Inv), Method = f.Length > 5 ? f[5] : ""
                    };
                }
                else if (line[0] == 'B') yield return ParseBar(line, h.TickSize, lineNo);
                else throw new InvalidDataException("line " + lineNo + ": unknown record");
            }
            if (!headerDone && onHeader != null) onHeader(h);
        }

        private static BarRecord ParseBar(string line, decimal tick, int lineNo)
        {
            try
            {
                int bar1 = line.IndexOf('|');
                string main = bar1 < 0 ? line : line.Substring(0, bar1);
                var f = main.Split(',');
                var b = new Bar
                {
                    Time = DateTime.ParseExact(f[1], "yyyy-MM-ddTHH:mm:ss", Inv),
                    Open = decimal.Parse(f[3], Inv), High = decimal.Parse(f[4], Inv), Low = decimal.Parse(f[5], Inv), Close = decimal.Parse(f[6], Inv),
                    Volume = decimal.Parse(f[7], Inv), Delta = decimal.Parse(f[8], Inv),
                    BigBuys = int.Parse(f[9], Inv), BigSells = int.Parse(f[10], Inv)
                };
                PriceLevelVol[] levels = new PriceLevelVol[0];
                if (bar1 >= 0)
                {
                    int bar2 = line.IndexOf('|', bar1 + 1);
                    decimal first = decimal.Parse(line.Substring(bar1 + 1, bar2 - bar1 - 1), Inv);
                    var cells = line.Substring(bar2 + 1).Split(',');
                    var list = new List<PriceLevelVol>(cells.Length);
                    for (int i = 0; i < cells.Length; i++)
                    {
                        string c = cells[i];
                        if (c.Length == 0) continue;
                        int colon = c.IndexOf(':');
                        list.Add(new PriceLevelVol(first + i * tick, decimal.Parse(c.Substring(0, colon), Inv), decimal.Parse(c.Substring(colon + 1), Inv)));
                    }
                    levels = list.ToArray();
                }
                return new BarRecord { Bar = b, Levels = levels, Symbol = f[2] };
            }
            catch (Exception e) when (!(e is InvalidDataException))
            {
                throw new InvalidDataException("line " + lineNo + ": bad bar record (" + e.Message + ")");
            }
        }

        /// <summary>
        /// Loads a bar file for the engine. Back-adjusts prices across rolls (older contracts shifted by the later roll
        /// offsets, so today's contract keeps real prices). rthOnly keeps only cash-session bars: the trade rules use the
        /// cash session only, and it makes multi-year runs much lighter. from/to filter by exchange date (to exclusive).
        /// </summary>
        public static BarSeries Load(string path, bool rthOnly, TimeSpan rthStart, TimeSpan rthEnd, DateTime? from = null, DateTime? to = null)
        {
            using (var r = OpenText(path)) return Load(r, rthOnly, rthStart, rthEnd, from, to);
        }

        public static BarSeries Load(TextReader reader, bool rthOnly, TimeSpan rthStart, TimeSpan rthEnd, DateTime? from = null, DateTime? to = null)
        {
            var s = new BarSeries();
            var clock = new SessionClock(rthStart, rthEnd);
            var recs = new List<BarRecord>();
            foreach (var o in ReadAll(reader, h => s.Header = h))
            {
                var roll = o as RollEvent;
                if (roll != null) { s.Rolls.Add(roll); continue; }
                var rec = (BarRecord)o;
                if (from.HasValue && rec.Bar.Time.Date < from.Value.Date) continue;
                if (to.HasValue && rec.Bar.Time.Date >= to.Value.Date) continue;
                if (rthOnly && !clock.IsRth(rec.Bar.Time)) continue;
                recs.Add(rec);
            }

            // back-adjust: a bar traded before roll k gets the sum of the offsets of rolls k..last
            var rolls = s.Rolls.OrderBy(x => x.TradingDate).ToList();
            decimal tick = s.Header.TickSize;
            int ri = 0; decimal pending = rolls.Sum(x => x.Offset);
            foreach (var rec in recs)
            {
                DateTime td = TradingDate(rec.Bar.Time);
                while (ri < rolls.Count && td >= rolls[ri].TradingDate) { pending -= rolls[ri].Offset; ri++; }
                if (pending != 0)
                {
                    decimal adj = Math.Round(pending / tick) * tick;
                    var b = rec.Bar;
                    b.Open += adj; b.High += adj; b.Low += adj; b.Close += adj;
                    var lv = rec.Levels;
                    for (int i = 0; i < lv.Length; i++) lv[i] = new PriceLevelVol(lv[i].Price + adj, lv[i].Bid, lv[i].Ask);
                }
                s.Bars.Add(rec.Bar); s.Levels.Add(rec.Levels);
            }
            s.Sessions = s.Bars.Select(b => clock.SessionKey(b.Time)).Where(k => k.HasValue).Distinct().Count();
            return s;
        }

        /// <summary>CME trading date: the electronic session starting at 18:00 New York belongs to the next day.</summary>
        public static DateTime TradingDate(DateTime exchangeTime)
        {
            return exchangeTime.AddHours(6).Date;
        }
    }
}
