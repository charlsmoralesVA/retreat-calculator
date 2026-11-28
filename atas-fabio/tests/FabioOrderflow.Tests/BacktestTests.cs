using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ATAS.Indicators;
using FabioOrderflow.Atas;
using FabioOrderflow.Core;

namespace FabioOrderflow.Tests
{
    /// <summary>The multi-year backtest path: bar files, Databento import, roll stitching, lean engine, sweep, report, ATAS export.</summary>
    internal static class BacktestTests
    {
        public static void Run()
        {
            TestBarFile();
            TestDatabentoCsv();
            TestStitcher();
            TestLeanAndRoutes();
            TestSweepAndReport();
            TestLosingTradeCap();
            TestAtasExport();
            TestSettingsLine();
            TestModels();
            TestFootprintLadder();
            TestOptionsLevels();
            foreach (var d in _dirs) try { Directory.Delete(d, true); } catch (Exception) { }
        }

        static readonly List<string> _dirs = new List<string>();

        static string Tmp(string name)
        {
            var dir = Path.Combine(Path.GetTempPath(), "fabio-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir); _dirs.Add(dir);
            return Path.Combine(dir, name);
        }

        static void TestBarFile()
        {
            T.Section("Bar file (gzip, footprint, rolls, back-adjustment)");
            string path = Tmp("t.bars.gz");
            var h = new BarFileHeader { Instrument = "ES", TickSize = 0.25m, Minutes = 5, Source = "test" };
            var t0 = new DateTime(2024, 3, 13, 9, 30, 0);
            using (var w = new BarFile.Writer(path, h))
            {
                w.Write(new BarRecord { Symbol = "ESH4", Bar = Synth.B(t0, 5000, 5001, 4999.5m, 5000.75m, 100, 20),
                    Levels = new[] { new PriceLevelVol(4999.5m, 10, 0), new PriceLevelVol(5000.75m, 5, 25), new PriceLevelVol(5001m, 0, 60) } });
                w.WriteRoll(new RollEvent { TradingDate = new DateTime(2024, 3, 14), OldSymbol = "ESH4", NewSymbol = "ESM4", Offset = 52.25m, Method = "test" });
                w.Write(new BarRecord { Symbol = "ESM4", Bar = Synth.B(t0.AddDays(1), 5060, 5061, 5059, 5060.5m, 80, -10),
                    Levels = new[] { new PriceLevelVol(5059m, 30, 5), new PriceLevelVol(5061m, 15, 30) } });
            }
            T.Expect(BarFile.LooksLikeBarFile(path), "written file is recognised as a bar file");
            var recs = new List<object>(); BarFileHeader hh = null;
            using (var r = BarFile.OpenText(path)) recs = BarFile.ReadAll(r, x => hh = x).ToList();
            var b1 = (BarRecord)recs[0];
            T.Expect(hh.Instrument == "ES" && hh.TickSize == 0.25m && hh.Minutes == 5, "header round trip");
            T.Expect(b1.Levels.Length == 3 && b1.Levels[1].Price == 5000.75m && b1.Levels[1].Ask == 25 && b1.Levels[2].Ask == 60, "footprint round trip, empty ticks skipped");
            T.Expect(b1.Bar.Delta == 20 && b1.Bar.Time == t0 && b1.Symbol == "ESH4", "bar fields round trip");
            var s = BarFile.Load(path, false, new TimeSpan(9, 30, 0), new TimeSpan(16, 0, 0));
            T.Eq(1, s.Rolls.Count, "roll read");
            T.Eq(5052.25m, s.Bars[0].Open, "the older contract is back-adjusted by the roll offset");
            T.Eq(5051.75m, s.Levels[0][0].Price, "its footprint is shifted too");
            T.Eq(5060m, s.Bars[1].Open, "the latest contract keeps its real prices");
        }

        static void TestDatabentoCsv()
        {
            T.Section("Databento trades CSV");
            string raw = "ts_recv,ts_event,rtype,publisher_id,instrument_id,action,side,depth,price,size,flags,ts_in_delta,sequence,symbol\n" +
                         "1704205800000000100,1704205800000000000,0,1,5002,T,B,0,4800250000000,3,0,0,1,ESH4\n" +
                         "1704205800000000200,1704205800000000100,0,1,5002,T,A,0,4800000000000,2,0,0,2,ESH4\n" +
                         "1704205800000000300,1704205800000000200,0,1,9001,T,B,0,25500000000,1,0,0,3,ESH4-ESM4\n" +
                         "1704205800000000400,1704205800000000300,0,1,5002,T,N,0,4800250000000,1,0,0,4,ESH4\n";
            var t = DatabentoCsv.Read(new StringReader(raw)).ToList();
            T.Eq(3, t.Count, "calendar spread skipped");
            T.Expect(t[0].Price == 4800.25m && t[0].Aggressor == Side.Long && t[0].Size == 3, "raw fixed-point price, B = buyer aggressor");
            T.Expect(t[1].Aggressor == Side.Short && t[2].Aggressor == Side.None, "A = seller aggressor, N = unknown");
            T.Eq(new DateTime(2024, 1, 2, 14, 30, 0, DateTimeKind.Utc), t[0].Utc, "nanosecond timestamp read as UTC");
            var ny = ExchangeTime.FromUtc(t[0].Utc, ExchangeTime.Find("America/New_York"));
            T.Eq(new DateTime(2024, 1, 2, 9, 30, 0), ny, "14:30 UTC = 09:30 New York in winter");

            string pretty = "ts_event,instrument_id,action,side,price,size,symbol\n" +
                            "2024-07-01T13:30:00.123456789Z,77,T,A,5500.500000000,4,ES.v.0\n";
            var p = DatabentoCsv.Read(new StringReader(pretty)).Single();
            T.Expect(p.Price == 5500.5m && p.Utc == new DateTime(2024, 7, 1, 13, 30, 0, DateTimeKind.Utc).AddTicks(1234567), "pretty timestamp (nanoseconds) and decimal price");
            T.Eq("id77", p.Symbol, "a continuous symbol is identified by its instrument id (changes at the roll)");
            bool threw = false;
            try { DatabentoCsv.Read(new StringReader("a,b,c\n1,2,3\n")).ToList(); } catch (InvalidDataException) { threw = true; }
            T.Expect(threw, "a file without ts_event / price / size is refused with a clear error");
        }

        static RawTrade Tr(DateTime ny, string sym, decimal px, decimal size)
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ny, DateTimeKind.Unspecified), ExchangeTime.Find("America/New_York"));
            return new RawTrade { Utc = utc, Symbol = sym, Price = px, Size = size, Aggressor = Side.Long };
        }

        static void TestStitcher()
        {
            T.Section("Front month by volume, rolls stitched");
            var zone = ExchangeTime.Find("America/New_York");
            var st = new FrontMonthStitcher(zone);
            var emitted = new List<string>(); var rolls = new List<RollEvent>();
            Action<RawTrade> add = tr => { RollEvent r; string sym; var tick = st.Add(tr, out r, out sym); if (r != null) rolls.Add(r); if (tick != null) emitted.Add(sym); };
            var d = new DateTime(2024, 3, 11);
            // day 1 (measure only): H heavy
            add(Tr(d.AddHours(10), "ESH4", 5000, 100)); add(Tr(d.AddHours(10), "ESM4", 5050, 10));
            // day 2: H is front; M overtakes in volume, both trade at the close
            add(Tr(d.AddDays(1).AddHours(10), "ESH4", 5010, 50)); add(Tr(d.AddDays(1).AddHours(10), "ESM4", 5060, 500));
            add(Tr(d.AddDays(1).AddHours(15.9), "ESH4", 5012, 1)); add(Tr(d.AddDays(1).AddHours(15.9), "ESM4", 5064.25m, 1));
            // day 3: M is front; a stray H print later must not flip back
            add(Tr(d.AddDays(2).AddHours(10), "ESM4", 5070, 40)); add(Tr(d.AddDays(2).AddHours(10), "ESH4", 5020, 900));
            add(Tr(d.AddDays(3).AddHours(10), "ESM4", 5075, 40));
            T.Expect(emitted.Count(x => x == "ESH4") == 2 && emitted.Skip(2).All(x => x == "ESM4"), "day 2 trades the old front, then the new one only");
            T.Eq(1, rolls.Count, "one roll, no flip back to the expired contract");
            T.Expect(rolls[0].OldSymbol == "ESH4" && rolls[0].NewSymbol == "ESM4" && rolls[0].Offset == 52.25m, "offset = new - old last prices of the same day (52.25)");
            T.Eq(new DateTime(2024, 3, 13), rolls[0].TradingDate, "roll takes effect on the next trading date");
            T.Eq(new DateTime(2024, 3, 13), BarFile.TradingDate(new DateTime(2024, 3, 12, 18, 0, 0)), "18:00 New York belongs to the next trading date");

            // a continuous feed (one instrument id at a time): roll at the id change, never back
            string path = Tmp("cont.bars.gz");
            int bars; List<string> warns;
            using (var w = new BarFile.Writer(path, new BarFileHeader { Instrument = "ES" }))
            {
                var imp = new TradeImporter(new FabioParams(), 5, w, zone, true);
                imp.Add(Tr(d.AddHours(10), "id1", 5000, 5)); imp.Add(Tr(d.AddHours(10.2), "id1", 5001, 5));
                imp.Add(Tr(d.AddDays(1).AddHours(10), "id2", 5053, 5));
                imp.Add(Tr(d.AddDays(1).AddHours(10.1), "id1", 5002, 5));      // late print of the old contract
                imp.Add(Tr(d.AddDays(1).AddHours(10.3), "id2", 5054, 5));
                imp.Finish(); bars = imp.Bars; warns = imp.Warnings;
            }
            var cs = BarFile.Load(path, false, new TimeSpan(9, 30, 0), new TimeSpan(16, 0, 0));
            T.Expect(cs.Rolls.Count == 1 && cs.Rolls[0].Offset == 52m && cs.Rolls[0].Method == "price-gap", "continuous feed: one roll at the contract change, offset from the price gap");
            T.Expect(warns.Any(x => x.Contains("ignored")), "a late print of the old contract is ignored, not a roll back");
        }

        static void TestLeanAndRoutes()
        {
            T.Section("Lean backtest engine = full engine");
            var p = new FabioParams();
            var zone = ExchangeTime.Find("America/New_York");
            string path = Tmp("syn.bars.gz");
            var header = new BarFileHeader { Instrument = "ES", Source = "synthetic", Synthetic = true };
            var trades = SyntheticMarket.Trades(new DateTime(2024, 1, 1), 120, 3).ToList();
            int rolls;
            using (var w = new BarFile.Writer(path, header))
            {
                var imp = new TradeImporter(p, 5, w, zone, false);
                foreach (var t in trades) imp.Add(t);
                imp.Finish();
                rolls = imp.Stitcher.Rolls.Count;
            }
            T.Expect(rolls >= 1, "synthetic quarter roll detected (" + rolls + ")");
            var s = BarFile.Load(path, true, p.RthStart, p.RthEnd);
            T.Expect(s.Sessions > 60 && s.Header.Synthetic, "loaded " + s.Sessions + " cash sessions, flagged synthetic");

            var lean = BarBacktest.Run(s, p, new BacktestParams());
            var full = new FabioEngine(p); var fullSignals = new List<SetupEvaluation>();
            for (int i = 0; i < s.Bars.Count; i++) fullSignals.AddRange(full.Update(i, s.Bars[i], s.Levels[i], false).NewSignals);
            T.Expect(lean.Signals == fullSignals.Count && lean.Signals > 0, "lean mode finds the same set-ups as the full engine (" + lean.Signals + ")");
            var fullTrades = Backtester.Simulate(s.Bars, fullSignals, p, new BacktestParams()).Trades.Where(t => t.ExitReason != "open").ToList();
            T.Expect(fullTrades.Count == lean.Trades.Count && fullTrades.Zip(lean.Trades, (a, b) => a.EntryTime == b.EntryTime && a.R == b.R).All(x => x), "and the same trades");

            var from = s.Bars[s.Bars.Count / 2].Time.Date;
            var window = BarBacktest.Run(s, p, new BacktestParams(), from);
            T.Expect(window.Trades.All(t => t.EntryTime.Date >= from) && window.Trades.Count <= lean.Trades.Count, "trade window: earlier bars only warm the engine up");
        }

        static void TestSweepAndReport()
        {
            T.Section("Walk-forward sweep and report");
            var p = new FabioParams();
            string path = Tmp("syn2.bars.gz");
            using (var w = new BarFile.Writer(path, new BarFileHeader { Instrument = "ES", Source = "synthetic", Synthetic = true }))
            {
                var imp = new TradeImporter(p, 5, w, ExchangeTime.Find("America/New_York"), false);
                foreach (var t in SyntheticMarket.Trades(new DateTime(2024, 1, 1), 160, 11)) imp.Add(t);
                imp.Finish();
            }
            var s = BarFile.Load(path, true, p.RthStart, p.RthEnd);
            var split = s.Bars[(int)(s.Bars.Count * 0.7)].Time.Date;
            var bp = new BacktestParams { PointValue = 50, CommissionPerContract = 4 };
            var grid = "AbsorbDeltaMult=1.0,1.5;ZoneToleranceTicks=4,6,8";
            var sw = BarBacktest.Sweep(s, p, bp, grid, split, 1, 2);
            T.Eq(6, sw.Rows.Count, "every combination of the grid ran");
            T.Expect(sw.Rows.All(r => r.InSample.Trades + r.OutSample.Trades >= 0) && sw.Rows.Zip(sw.Rows.Skip(1), (a, b) => a.Score >= b.Score).All(x => x), "rows ranked by in-sample score");
            var any = sw.Rows.First();
            var pc = BarBacktest.Clone(p); foreach (var kv in any.Values) BarBacktest.Set(pc, kv.Key, kv.Value);
            var r = BarBacktest.Run(s, pc, bp);
            T.Eq(r.Trades.Count(t => t.EntryTime.Date < split), any.InSample.Trades, "in-sample = trades before the split only");
            T.Eq(r.Trades.Count(t => t.EntryTime.Date >= split), any.OutSample.Trades, "out-of-sample = trades from the split on");
            if (r.Trades.Count > 0)
            {
                var t0 = r.Trades[0];
                T.Near(t0.R - (4m / 50m) / t0.Risk, Perf.NetR(t0, bp), 0.0001m, "R after costs subtracts the commission in points");
            }
            bool threw = false; try { BarBacktest.ParseGrid("NoSuchSetting=1,2"); } catch (ArgumentException) { threw = true; }
            T.Expect(threw, "an unknown setting name in the grid is refused");

            var rep = new BacktestReport { Series = s, Run = r, Sweep = sw };
            string dir = Path.GetDirectoryName(path);
            rep.WriteAll(dir);
            foreach (var f in new[] { "summary.json", "trades.csv", "report.md", "report.html" })
                T.Expect(File.Exists(Path.Combine(dir, f)), f + " written");
            var json = Json.Parse(File.ReadAllText(Path.Combine(dir, "summary.json"))) as Dictionary<string, object>;
            T.Expect(json != null && json.ContainsKey("by_year") && json.ContainsKey("sweep") && json.ContainsKey("by_setup"), "summary.json parses and has the breakdowns");
            T.Expect(File.ReadAllText(Path.Combine(dir, "report.html")).Contains("Synthetic data") && File.ReadAllText(Path.Combine(dir, "report.md")).Contains("SYNTHETIC"), "synthetic data is flagged in every report");
        }

        static void TestLosingTradeCap()
        {
            T.Section("Losing-trade cap per session (as the strategy)");
            var p = new FabioParams();
            var day = new DateTime(2026, 3, 2);
            var bars = new List<Bar>();
            for (int i = 0; i < 20; i++) bars.Add(Synth.B(Synth.Open(day, i), 100, 100.5m, 99.5m, 100, 100, 0));
            // three long signals whose stops are hit on the next bar
            var sigs = new List<SetupEvaluation>();
            foreach (int k in new[] { 2, 6, 10 })
            {
                bars[k + 1] = Synth.B(Synth.Open(day, k + 1), 100, 100.25m, 98, 98.5m, 100, -50);
                sigs.Add(new SetupEvaluation { Side = Side.Long, Stop = 99, Target = 102, Entry = 100, ConfirmGlobalIndex = k, Zone = new Zone { Kind = ZoneKind.Val, Price = 99.5m } });
            }
            var capped = Backtester.Simulate(bars, sigs, p, new BacktestParams { MaxLosingTradesPerSession = 2, MaxDailyLossR = 0 });
            var free = Backtester.Simulate(bars, sigs, p, new BacktestParams { MaxLosingTradesPerSession = 0, MaxDailyLossR = 0 });
            T.Expect(capped.Trades.Count == 2 && capped.Skipped.Contains("max losing trades"), "after 2 losers the third set-up of the session is skipped");
            T.Eq(3, free.Trades.Count, "without the cap all three are taken");
        }

        static void TestOptionsLevels()
        {
            T.Section("Options (gamma) levels on the bridge, for Laya's context");
            string journal = Tmp("journal.jsonl");
            var br = new LayaBridge { JournalPath = journal };
            br.SetState("{\"last_price\": 5230.25, \"instrument\": \"ES\"}");
            T.Expect(!br.StateJson.Contains("options_levels"), "no levels entered: the state is unchanged");
            string err;
            var body = (Dictionary<string, object>)Json.Parse("{\"session\":\"2026-12-01\",\"source\":\"ATAS Key Levels\",\"levels\":[{\"name\":\"Call wall\",\"price\":5250},{\"name\":\"Zero gamma\",\"price\":5225.5}]}");
            T.Expect(br.SetOptionsLevels(body, DateTime.UtcNow, out err), "levels saved");
            var st = (Dictionary<string, object>)Json.Parse(br.StateJson);
            var ol = (Dictionary<string, object>)st["options_levels"];
            var lv = ((List<object>)ol["levels"]).Cast<Dictionary<string, object>>().ToList();
            T.Expect(lv[0]["name"].ToString() == "Call wall" && (decimal)lv[0]["points_from_price"] == 19.75m && lv[0]["side"].ToString() == "above", "Laya's state has each level with its distance from the last price");
            T.Expect((decimal)lv[1]["points_from_price"] == -4.75m && lv[1]["side"].ToString() == "below", "and its side");
            T.Expect(ol["note"].ToString().Contains("not part of Fabio's method"), "flagged as context, not Fabio's method");
            var br2 = new LayaBridge { JournalPath = journal };
            br2.SetState("{\"last_price\": 5240}");
            T.Expect(br2.StateJson.Contains("Call wall"), "levels survive a restart of the strategy (saved next to the journal)");
            T.Expect(!br.SetOptionsLevels((Dictionary<string, object>)Json.Parse("{\"levels\":[{\"name\":\"x\",\"price\":-1}]}"), DateTime.UtcNow, out err), "a bad price is refused");
            T.Expect(!br.SetOptionsLevels((Dictionary<string, object>)Json.Parse("{\"nolevels\":1}"), DateTime.UtcNow, out err) && err.Contains("levels"), "a body without levels is refused");
            T.Expect(br.SetOptionsLevels((Dictionary<string, object>)Json.Parse("{\"levels\":[]}"), DateTime.UtcNow, out err) && !br.StateJson.Contains("options_levels"), "an empty list clears them");
        }

        static void TestFootprintLadder()
        {
            T.Section("Footprint ladder on Laya's candle cards");
            var c = new CandleCard();
            var bar = Synth.B(new DateTime(2026, 3, 2, 10, 0, 0), 100, 101, 100, 101, 0, 0);
            var lv = new[]
            {
                new PriceLevelVol(101.00m, 5, 40), new PriceLevelVol(100.75m, 4, 45), new PriceLevelVol(100.50m, 6, 50),
                new PriceLevelVol(100.25m, 10, 18), new PriceLevelVol(100.00m, 60, 2)
            };
            CandleCard.FillFootprint(c, bar, lv, new FabioParams());
            var j = c.LadderJson();
            var rows = (List<object>)j["rows_high_to_low"];
            T.Expect(rows.Count == 5 && (decimal)((List<object>)rows[0])[0] == 101.00m && (decimal)((List<object>)rows[4])[3] == -58m, "rows from the high to the low with bid, ask and delta");
            var buy = (List<decimal>)j["buy_imbalances"];
            T.Expect(buy.Contains(101.00m) && buy.Contains(100.75m) && buy.Contains(100.50m) && !buy.Contains(100.25m), "buy imbalances: ask >= 3x the bid one tick lower");
            T.Expect(((List<object>)j["stacked_buy_imbalances"]).Count == 1, "three in a row reported as stacked");
            T.Expect(((List<decimal>)j["sell_imbalances"]).Contains(100.00m), "sell imbalance: bid >= 3x the ask one tick higher");
            T.Expect(Json.Write(c.ToJson()).Contains("footprint_ladder"), "the card Laya reads carries the ladder");
            var wide = Enumerable.Range(0, 120).Select(i => new PriceLevelVol(100m + i * 0.25m, 10, 10)).ToArray();
            var c2 = new CandleCard(); CandleCard.FillFootprint(c2, Synth.B(bar.Time, 100, 130, 100, 120, 0, 0), wide, new FabioParams());
            var j2 = c2.LadderJson();
            T.Expect(((List<object>)j2["rows_high_to_low"]).Count <= 48 && (int)j2["ticks_per_row"] == 3, "a wide bar is grouped to at most 48 rows");
        }

        static void TestModels()
        {
            T.Section("Fabio's models: registry, switches, comparison");
            T.Expect(ModelRegistry.All.Count >= 2 && ModelRegistry.Find("ZoneReaction") != null && ModelRegistry.Find("IvbRetest") != null, "models registered and found by name");
            var p = new FabioParams();
            var only = ModelRegistry.Only(p, ModelRegistry.Find("IvbRetest"));
            T.Expect(!only.EnableZoneReaction && only.EnableIvb && p.EnableZoneReaction && !p.EnableIvb, "Only() switches one model on, others off, on a copy");
            var all = ModelRegistry.AllOn(p);
            T.Expect(all.EnableZoneReaction && all.EnableIvb, "AllOn() switches every model on");

            string path = Tmp("models.bars.gz");
            using (var w = new BarFile.Writer(path, new BarFileHeader { Instrument = "ES", Source = "synthetic", Synthetic = true }))
            {
                var imp = new TradeImporter(p, 5, w, ExchangeTime.Find("America/New_York"), false);
                foreach (var t in SyntheticMarket.Trades(new DateTime(2024, 1, 1), 200, 21)) imp.Add(t);
                imp.Finish();
            }
            var s = BarFile.Load(path, true, p.RthStart, p.RthEnd);
            var bp = new BacktestParams();
            var rz = BarBacktest.Run(s, ModelRegistry.Only(p, ModelRegistry.Find("ZoneReaction")), bp);
            var ri = BarBacktest.Run(s, ModelRegistry.Only(p, ModelRegistry.Find("IvbRetest")), bp);
            var ra = BarBacktest.Run(s, all, bp);
            var none = BarBacktest.Clone(p); none.EnableZoneReaction = false; none.EnableIvb = false;
            var rn = BarBacktest.Run(s, none, bp);
            T.Expect(rz.Trades.All(t => t.Model == "ZoneReaction") && ri.Trades.All(t => t.Model.StartsWith("IVB")), "each model alone trades only its own set-ups (" + rz.Trades.Count + " / " + ri.Trades.Count + ")");
            T.Expect(rn.Signals == 0, "all models off: no set-ups");
            T.Expect(ra.Trades.Count > 0 && ra.Trades.Select(t => t.Model).Distinct().Count() >= 1, "all together trades (" + ra.Trades.Count + ")");
            var defaults = BarBacktest.Run(s, p, bp);
            T.Eq(rz.Trades.Count, defaults.Trades.Count, "the defaults are ZoneReaction alone (IVB is off by default)");

            var rows = new List<ModelRow> { new ModelRow { Name = "ZoneReaction", All = Perf.Of("z", rz.Trades, bp), InSample = Perf.Of("i", rz.Trades, bp), OutSample = Perf.Of("o", new TradeRecord[0], bp) } };
            var rep = new BacktestReport { Series = s, Run = ra, Models = rows };
            var json = Json.Write(rep.ToJson());
            T.Expect(json.Contains("models_compared") && json.Contains("by_model"), "reports carry the model comparison and the by-model split");
            T.Expect(rep.Html().Contains("Models compared") && rep.TradesCsv().StartsWith("entry_time,exit_time,side,model,"), "HTML table and a model column in trades.csv");
        }

        static void TestSettingsLine()
        {
            T.Section("Settings line: backtest -> ATAS indicator and strategy");
            var p = new FabioParams { AbsorbDeltaMult = 1.5m, ZoneToleranceTicks = 4, RequireApproachFilter = false, TickSize = 0.5m };
            string line = BarBacktest.SettingsLine(p);
            T.Eq("ZoneToleranceTicks=4;AbsorbDeltaMult=1.5;RequireApproachFilter=False", line, "only the changed rule settings, instrument settings left out");
            var q = new FabioParams();
            T.Expect(BarBacktest.ApplySettingsLine(q, line) == null && q.AbsorbDeltaMult == 1.5m && q.ZoneToleranceTicks == 4 && !q.RequireApproachFilter, "the line applies back exactly");
            T.Expect(BarBacktest.ApplySettingsLine(new FabioParams(), "Bogus=1; AbsorbDeltaMult=abc").Contains("Bogus: unknown setting"), "errors are reported, not ignored");

            var strat = new FabioStrategy { EngineSettings = "AbsorbDeltaMult=1.75;MaxSignalsPerSession=2" };
            strat.Feed.Reset();
            T.Expect(strat.Feed.P.AbsorbDeltaMult == 1.75m && strat.Feed.P.MaxSignalsPerSession == 2 && strat.Feed.EngineSettingsError == null, "the strategy runs the pasted settings");
            var ind = new FabioLevelsIndicator { EngineSettings = "Nope=3" };
            ind.Feed.Reset();
            T.Expect(ind.Feed.PanelText(false).Contains("ENGINE SETTINGS NOT APPLIED"), "a wrong line is shown loudly in the panel");
        }

        static void TestAtasExport()
        {
            T.Section("ATAS indicator: export history -> fabio-backtest gives the chart's trades");
            var sc = new List<IndicatorCandle>();
            var day = new DateTime(2026, 3, 2);
            var ny = ExchangeTime.Find("America/New_York");
            Func<Bar, PriceLevelVol[], IndicatorCandle> toCandle = (b, lv) =>
            {
                var c = new IndicatorCandle
                {
                    Time = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(b.Time, DateTimeKind.Unspecified), ny),
                    Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume, Delta = b.Delta
                };
                foreach (var l in lv) c.TestLevels.Add(new PriceLevel { Price = l.Price, Bid = l.Bid, Ask = l.Ask, Volume = l.Bid + l.Ask });
                return c;
            };
            VolumeProfile last = null;
            foreach (var center in new decimal[] { 4500, 4504, 4508, 4512, 4516, 4520 })
            {
                last = new VolumeProfile();
                foreach (var kv in Synth.Session(day, center)) { foreach (var l in kv.Value) last.Add(l); sc.Add(toCandle(kv.Key, kv.Value)); }
                day = day.AddDays(1);
            }
            var prior = last.Compute(0.25m, 0.70m);
            var plan = Program.LongAtVal(prior, day);
            for (int k = 0; k < plan.Bars.Count; k++) sc.Add(toCandle(plan.Bars[k], plan.Levels[k]));
            int n = plan.Bars.Count;
            var run = Synth.B(Synth.Open(day, n++), prior.Val + 2.0m, prior.Val + 8m, prior.Val + 1.75m, prior.Val + 7.5m, 1500, 300);
            sc.Add(toCandle(run, Synth.Uniform(run.Low, run.High, run.Volume, run.Delta)));
            var q = Synth.B(Synth.Open(day, n++), prior.Val + 2.0m, prior.Val + 2.5m, prior.Val + 1.5m, prior.Val + 2.25m, 800, 0);
            sc.Add(toCandle(q, Synth.Uniform(q.Low, q.High, q.Volume, q.Delta)));

            string folder = Path.GetDirectoryName(Tmp("x"));
            var ind = new FabioLevelsIndicator();
            ind.InstrumentInfo.TickSize = 0.25m; ind.InstrumentInfo.Instrument = "ESM6";
            ind.ExportFolder = folder; ind.ExportHistory = true;
            ind.TestCandles = i => sc[i];
            ind.TestCurrentBar = sc.Count;
            var calc = ind.GetType().GetMethod("OnCalculate", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < sc.Count; i++) calc.Invoke(ind, new object[] { i, 0m });

            var f = ind.Feed;
            var files = Directory.GetFiles(folder, "*.bars.gz");
            T.Expect(files.Length == 1 && f.ExportStatus.Contains("bars ->"), "export written when the history finished loading: " + f.ExportStatus);
            T.Expect(f.PanelText(false).Contains("export:"), "export status shown in the panel");
            if (files.Length != 1) return;
            T.Expect(Path.GetFileName(files[0]).StartsWith("ESM6_5m_"), "file named after the instrument and the timeframe (" + Path.GetFileName(files[0]) + ")");
            var p = new FabioParams();
            var s = BarFile.Load(files[0], true, p.RthStart, p.RthEnd);
            T.Eq(sc.Count - 1, s.Bars.Count, "every closed bar exported (the forming bar is not)");
            var r = BarBacktest.Run(s, p, new BacktestParams());
            T.Expect(f.Trades != null && f.Trades.Trades.Count == 1 && r.Trades.Count == 1, "same trade count as the chart");
            if (r.Trades.Count == 1 && f.Trades != null && f.Trades.Trades.Count == 1)
                T.Expect(r.Trades[0].EntryTime == f.Trades.Trades[0].EntryTime && r.Trades[0].Exit == f.Trades.Trades[0].Exit && r.Trades[0].ExitReason == f.Trades.Trades[0].ExitReason,
                    "same entry, exit and result as the chart (" + r.Trades[0].ExitReason + ")");
        }
    }
}
