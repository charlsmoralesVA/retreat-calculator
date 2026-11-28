using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// Local HTTP endpoint for Laya, listening on http://localhost:PORT/ only (never on the network).
    ///   GET  /health                         {"ok":true,...}
    ///   GET  /state                          what the engine sees now (bias, levels, VWAP, checklist, history stats)
    ///   GET  /setups?status=pending&limit=20 tickets, newest first (status: pending|approved|rejected|expired|auto|taken|skipped|all)
    ///   GET  /setups/wait?timeout=30         long-poll: returns as soon as a set-up is pending
    ///   POST /setups/{id}/decision           body {"approve":true|false,"reason":"..."}
    ///   GET  /candles?after=N&limit=20       closed candles (cards) after seq N, oldest first, with Laya's review if any
    ///   GET  /candles/wait?after=N&timeout=30 long-poll: returns as soon as a candle after N has closed
    ///   POST /candles/{seq}/review           body {"read":"...", ...anything else Laya wants to record}
    ///   GET  /reviews?limit=20               Laya's latest candle reviews
    ///   GET  /plan, POST /plan               Laya's pre-market plan for the session ({"session":"yyyy-MM-dd", ...})
    ///   GET  /options, POST /options         the day's options (gamma) levels entered by the trader; also inside /state
    ///   GET  /journal?limit=50               recent events
    /// If a token is set, every request must carry the header X-Bridge-Token.
    /// </summary>
    public sealed class BridgeServer : IDisposable
    {
        public const string Version = "1";
        private readonly LayaBridge _bridge;
        private readonly int _port;
        private readonly string _token;
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _loop;

        public BridgeServer(LayaBridge bridge, int port, string token)
        {
            _bridge = bridge; _port = port; _token = token ?? "";
        }

        public bool IsRunning { get { return _listener != null && _listener.IsListening; } }
        public string Url { get { return "http://localhost:" + _port + "/"; } }
        public string LastError { get; private set; }

        public bool Start()
        {
            if (IsRunning) return true;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(Url);
                _listener.Start();
                _cts = new CancellationTokenSource();
                _loop = Task.Run(() => Loop(_cts.Token));
                LastError = null;
                return true;
            }
            catch (Exception e)
            {
                LastError = "bridge could not listen on " + Url + ": " + e.Message;
                try { _listener?.Close(); } catch (Exception) { }
                _listener = null;
                return false;
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch (Exception) { }
            try { _listener?.Stop(); _listener?.Close(); } catch (Exception) { }
            _listener = null;
        }

        private async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) { break; }
                var _ = Task.Run(() => Handle(ctx));
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            int code = 200; string body;
            try
            {
                var req = ctx.Request;
                if (_token.Length > 0 && req.Headers["X-Bridge-Token"] != _token) { code = 401; body = Err("missing or wrong X-Bridge-Token"); }
                else body = Route(req, out code);
            }
            catch (Exception e) { code = 500; body = Err(e.Message); }
            try
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode = code;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception) { /* client went away */ }
        }

        private string Route(HttpListenerRequest req, out int code)
        {
            code = 200;
            string path = req.Url.AbsolutePath.TrimEnd('/');
            string method = req.HttpMethod.ToUpperInvariant();
            var now = DateTime.UtcNow;

            if (method == "GET" && (path == "" || path == "/health"))
                return Json.Write(new Dictionary<string, object> { { "ok", true }, { "service", "fabio-atas-bridge" }, { "version", Version }, { "time_utc", now } });

            if (method == "GET" && path == "/state") return _bridge.StateJson;

            if (method == "GET" && path == "/setups")
            {
                var list = _bridge.Tickets(req.QueryString["status"] ?? "all", now, Int(req.QueryString["limit"], 20));
                return Json.Write(new Dictionary<string, object> { { "setups", list.Select(t => (object)t.ToJson()).ToList() } });
            }

            if (method == "GET" && path == "/setups/wait")
            {
                int timeout = Math.Max(0, Math.Min(120, Int(req.QueryString["timeout"], 30)));
                var list = _bridge.WaitForPending(TimeSpan.FromSeconds(timeout), () => DateTime.UtcNow);
                return Json.Write(new Dictionary<string, object> { { "setups", list.Select(t => (object)t.ToJson()).ToList() }, { "timed_out", list.Count == 0 } });
            }

            if (method == "POST" && path.StartsWith("/setups/") && path.EndsWith("/decision"))
            {
                string id = path.Substring("/setups/".Length, path.Length - "/setups/".Length - "/decision".Length);
                string text;
                using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) text = r.ReadToEnd();
                Dictionary<string, object> obj;
                try { obj = Json.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text) as Dictionary<string, object>; }
                catch (FormatException) { obj = null; }
                if (obj == null || !obj.ContainsKey("approve") || !(obj["approve"] is bool)) { code = 400; return Err("body must be {\"approve\": true|false, \"reason\": \"...\"}"); }
                string reason = obj.ContainsKey("reason") ? Convert.ToString(obj["reason"], CultureInfo.InvariantCulture) : "";
                string error;
                if (!_bridge.Decide(Uri.UnescapeDataString(id), (bool)obj["approve"], reason, now, out error)) { code = 409; return Err(error); }
                return Json.Write(new Dictionary<string, object> { { "ok", true }, { "id", id }, { "approved", (bool)obj["approve"] } });
            }

            if (method == "GET" && path == "/candles")
            {
                var list = _bridge.Candles(Int(req.QueryString["after"], -1), Int(req.QueryString["limit"], 20));
                return Json.Write(new Dictionary<string, object> { { "candles", list.Select(c => (object)_bridge.CandleJson(c)).ToList() }, { "last_seq", _bridge.LastCandleSeq } });
            }

            if (method == "GET" && path == "/candles/wait")
            {
                int timeout = Math.Max(0, Math.Min(120, Int(req.QueryString["timeout"], 30)));
                var list = _bridge.WaitForCandles(Int(req.QueryString["after"], -1), TimeSpan.FromSeconds(timeout));
                return Json.Write(new Dictionary<string, object> { { "candles", list.Select(c => (object)_bridge.CandleJson(c)).ToList() }, { "timed_out", list.Count == 0 }, { "last_seq", _bridge.LastCandleSeq } });
            }

            if (method == "POST" && path.StartsWith("/candles/") && path.EndsWith("/review"))
            {
                string seqText = path.Substring("/candles/".Length, path.Length - "/candles/".Length - "/review".Length);
                int seq;
                if (!int.TryParse(seqText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seq)) { code = 400; return Err("bad candle seq"); }
                string text;
                using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) text = r.ReadToEnd();
                Dictionary<string, object> obj;
                try { obj = Json.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text) as Dictionary<string, object>; }
                catch (FormatException) { code = 400; return Err("body must be a JSON object with at least \"read\""); }
                string error;
                if (!_bridge.ReviewCandle(seq, obj, now, out error)) { code = error.StartsWith("unknown") ? 404 : 400; return Err(error); }
                return Json.Write(new Dictionary<string, object> { { "ok", true }, { "seq", seq } });
            }

            if (method == "GET" && path == "/options")
                return Json.Write(new Dictionary<string, object> { { "options_levels", _bridge.OptionsLevels } });

            if (method == "POST" && path == "/options")
            {
                string text;
                using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) text = r.ReadToEnd();
                Dictionary<string, object> obj;
                try { obj = Json.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text) as Dictionary<string, object>; }
                catch (FormatException) { code = 400; return Err("body must be a JSON object"); }
                string error;
                if (!_bridge.SetOptionsLevels(obj, now, out error)) { code = 400; return Err(error); }
                return Json.Write(new Dictionary<string, object> { { "ok", true } });
            }

            if (method == "GET" && path == "/plan")
                return Json.Write(new Dictionary<string, object> { { "plan", _bridge.Plan } });

            if (method == "POST" && path == "/plan")
            {
                string text;
                using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) text = r.ReadToEnd();
                Dictionary<string, object> obj;
                try { obj = Json.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text) as Dictionary<string, object>; }
                catch (FormatException) { code = 400; return Err("body must be a JSON object"); }
                string error;
                if (!_bridge.SetPlan(obj, now, out error)) { code = 400; return Err(error); }
                return Json.Write(new Dictionary<string, object> { { "ok", true } });
            }

            if (method == "GET" && path == "/reviews")
                return Json.Write(new Dictionary<string, object> { { "reviews", _bridge.Reviews(Int(req.QueryString["limit"], 20)).Cast<object>().ToList() } });

            if (method == "GET" && path == "/journal")
                return Json.Write(new Dictionary<string, object> { { "events", _bridge.Journal(Int(req.QueryString["limit"], 50)).Cast<object>().ToList() } });

            code = 404;
            return Err("unknown endpoint " + method + " " + path);
        }

        private static int Int(string s, int dflt) { int v; return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : dflt; }
        private static string Err(string msg) { return Json.Write(new Dictionary<string, object> { { "ok", false }, { "error", msg } }); }
    }
}
