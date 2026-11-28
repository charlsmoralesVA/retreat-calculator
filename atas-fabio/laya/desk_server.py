#!/usr/bin/env python3
"""
Laya's local dashboard: the Laya Tape Desk in any browser at http://localhost:8770, without the Claude desktop app.

  python3 laya/desk_server.py                    dashboard only
  python3 laya/desk_server.py --start-runner     dashboard + Laya reading every candle (what ./start-laya.sh does)

What it does, all on this Mac (it listens on 127.0.0.1 only):
  /                     the desk (app/laya-desk.html) in local mode
  /api/...              passed to the ATAS strategy's bridge (default http://localhost:8766), token added here
  /local/runner         start / stop Laya (laya_runner.py) and read her log
  /local/ask            "Ask Laya about this candle": one Claude Code call with your Fabio skill
  /reports/             backtest reports, and a button that backtests the latest ATAS export (backtest-atas.sh)

Standard library only. Requests that change something need the desk's own header and origin, so other web pages
cannot drive it.
"""
import argparse
import collections
import html
import json
import os
import re
import shlex
import signal
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
DESK = ROOT / "app" / "laya-desk.html"
REPORTS = ROOT / "reports"
LOGS = ROOT / "logs"
SAFE_PATH = re.compile(r"^/[A-Za-z0-9/_\-]*(\?[A-Za-z0-9_\-=&.%]*)?$")
REPORT_FILES = {"report.html": "text/html", "report.md": "text/plain", "laya_review.md": "text/plain",
                "summary.json": "application/json", "trades.csv": "text/csv"}


class Job:
    """A child process (Laya's runner or a backtest) with its output kept for the dashboard."""

    def __init__(self, name: str, logfile: Path):
        self.name, self.logfile = name, logfile
        self.proc = None
        self.lines = collections.deque(maxlen=800)
        self.seq = 0
        self.started = None
        self.exit_code = None
        self.lock = threading.Lock()
        self.want_running = False       # set by start, cleared by stop: the watchdog restarts the job while it is set
        self.argv, self.cwd = None, None
        self.restarts = collections.deque(maxlen=20)

    def running(self) -> bool:
        return self.proc is not None and self.proc.poll() is None

    def add(self, line: str):
        with self.lock:
            self.seq += 1
            self.lines.append((self.seq, line))
        try:
            with open(self.logfile, "a", encoding="utf-8") as f:
                f.write(line + "\n")
        except OSError:
            pass

    def start(self, argv, cwd) -> bool:
        self.want_running, self.argv, self.cwd = True, argv, cwd
        if self.running():
            return False
        LOGS.mkdir(exist_ok=True)
        self.add("$ " + " ".join(shlex.quote(a) for a in argv))
        env = dict(os.environ, PYTHONUNBUFFERED="1")
        self.proc = subprocess.Popen(argv, cwd=str(cwd), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                     bufsize=1, env=env, start_new_session=True)
        self.started, self.exit_code = time.time(), None
        threading.Thread(target=self._pump, args=(self.proc,), daemon=True).start()
        return True

    def _pump(self, proc):
        for line in proc.stdout:
            self.add(line.rstrip("\n"))
        code = proc.wait()
        self.exit_code = code
        self.add(f"[{self.name} stopped, exit {code}]")

    def watchdog_tick(self, max_per_hour: int = 10, delay: float = 15.0):
        """Restart the job if it stopped without being asked to (a crash, a killed process), at most max_per_hour."""
        if not self.want_running or self.running() or self.argv is None:
            return
        if self.started and time.time() - self.started < delay:
            return
        now = time.time()
        recent = [t for t in self.restarts if now - t < 3600]
        if len(recent) >= max_per_hour:
            if self.want_running:
                self.add(f"[{self.name} stopped {len(recent)} times in an hour: not restarting again; press Start to try]")
                self.want_running = False
            return
        self.restarts.append(now)
        self.add(f"[{self.name} stopped unexpectedly (exit {self.exit_code}); restarting]")
        argv, cwd = self.argv, self.cwd
        self.start(argv, cwd)

    def stop(self):
        self.want_running = False
        p = self.proc
        if p is None or p.poll() is not None:
            return
        try:
            os.killpg(p.pid, signal.SIGINT)
        except OSError:
            p.send_signal(signal.SIGINT)
        for _ in range(30):
            if p.poll() is not None:
                return
            time.sleep(0.1)
        try:
            os.killpg(p.pid, signal.SIGTERM)
        except OSError:
            p.terminate()

    def view(self, after: int) -> dict:
        with self.lock:
            lines = [l for s, l in self.lines if s > after]
            return {"running": self.running(), "next": self.seq, "lines": lines[-400:], "exit_code": self.exit_code,
                    "started": self.started}


class Desk:
    def __init__(self, a):
        self.a = a
        self.runner = Job("Laya", LOGS / "laya-runner.log")
        self.backtest = Job("backtest", LOGS / "backtest.log")
        self.ask_lock = threading.Lock()

    def runner_argv(self, model: str, skill: str):
        argv = [sys.executable, "-u", str(HERE / "laya_runner.py"), "--bridge", self.a.bridge]
        if self.a.token:
            argv += ["--token", self.a.token]
        if self.a.claude != "claude":
            argv += ["--claude", self.a.claude]
        if model:
            argv += ["--model", model]
        if skill:
            argv += ["--skill-command", skill]
        return argv

    def bridge(self, method: str, path: str, body: bytes):
        req = urllib.request.Request(self.a.bridge.rstrip("/") + path, data=body if method == "POST" else None, method=method)
        req.add_header("Content-Type", "application/json")
        if self.a.token:
            req.add_header("X-Bridge-Token", self.a.token)
        try:
            with urllib.request.urlopen(req, timeout=8) as r:
                return r.status, r.read()
        except urllib.error.HTTPError as e:
            return e.code, e.read()
        except (urllib.error.URLError, OSError) as e:
            return 502, json.dumps({"error": f"bridge not reachable: {e}"}).encode()

    def ask(self, prompt: str, model: str, skill: str) -> str:
        head = (skill.strip() + "\n\n") if skill else ""
        full = (head + "Use your Fabio order-flow skill for this answer.\n\nDESK_QUESTION:\n" + prompt)
        args = shlex.split(self.a.claude) + ["-p", "--allowedTools", "Skill", "Read", "Glob", "Grep", "--output-format", "json",
                                             "--max-turns", "10", "--permission-mode", "dontAsk"]
        if model:
            args += ["--model", model]
        env = dict(os.environ)
        env.setdefault("CLAUDE_CODE_SYNC_SKILLS", "1")
        p = subprocess.run(args, input=full, capture_output=True, text=True, timeout=self.a.ask_seconds, env=env)
        out = p.stdout.strip()
        if p.returncode != 0 and not out:
            raise RuntimeError(f"claude exited {p.returncode}: {p.stderr.strip()[:300]}")
        try:
            env_ = json.loads(out)
        except json.JSONDecodeError:
            return out
        if isinstance(env_, dict):
            if env_.get("subtype") not in (None, "success"):
                raise RuntimeError(f"claude run ended with {env_.get('subtype')}")
            return str(env_.get("result", ""))
        return out

    def latest_export(self):
        folder = Path(self.a.exports).expanduser()
        files = sorted(folder.glob("*.bars.gz"), key=lambda f: f.stat().st_mtime, reverse=True) if folder.is_dir() else []
        return files[0] if files else None


def reports_page(desk: Desk) -> bytes:
    rows = []
    if REPORTS.is_dir():
        for d in sorted((p for p in REPORTS.iterdir() if p.is_dir()), key=lambda p: p.stat().st_mtime, reverse=True):
            s = {}
            try:
                s = json.loads((d / "summary.json").read_text(encoding="utf-8"))
            except (OSError, ValueError):
                pass
            o, data = s.get("overall", {}), s.get("data", {})
            links = " · ".join(f'<a href="/reports/{html.escape(d.name)}/{f}">{label}</a>' for f, label in
                               (("report.html", "report"), ("laya_review.md", "Laya's review"), ("trades.csv", "trades"))
                               if (d / f).exists())
            rows.append(
                f"<tr><td>{html.escape(d.name)}{' <span class=syn>synthetic</span>' if data.get('synthetic') else ''}</td>"
                f"<td>{html.escape(str(data.get('first_bar', ''))[:10])} to {html.escape(str(data.get('last_bar', ''))[:10])}</td>"
                f"<td>{o.get('trades', '-')}</td><td>{_r(o.get('total_r'))}</td><td>{_r(o.get('max_drawdown_r'))}</td><td>{links}</td></tr>")
    exp = desk.latest_export()
    exp_text = (f"Latest ATAS export: <code>{html.escape(exp.name)}</code>" if exp else
                "No ATAS export yet: in ATAS, turn on the indicator's <b>Export history for backtest</b>.")
    table = ("<table><tr><th>Report</th><th>Data</th><th>Trades</th><th>Total R</th><th>Max DD (R)</th><th></th></tr>"
             + "".join(rows) + "</table>") if rows else "<p>No reports yet.</p>"
    page = """<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Laya Backtests</title><style>
:root{--bg:#f7f7f5;--panel:#fff;--ink:#1d1d1b;--muted:#6b6b66;--line:#e3e2dc;--accent:#2f5d8a}
@media (prefers-color-scheme:dark){:root{--bg:#141413;--panel:#1d1d1b;--ink:#ecebe6;--muted:#9b9a94;--line:#33332f;--accent:#7fb0e0}}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 -apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif}
main{max-width:1040px;margin:0 auto;padding:24px 16px}h1{font-size:22px;margin:0 0 4px}a{color:var(--accent)}
.card{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:12px;margin:12px 0;overflow-x:auto}
table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}th,td{padding:6px 8px;border-bottom:1px solid var(--line);text-align:left;white-space:nowrap}
th{color:var(--muted)}button{font:inherit;padding:8px 14px;border-radius:8px;border:1px solid var(--line);background:var(--accent);color:#fff;cursor:pointer}
button:disabled{opacity:.5;cursor:default}pre{white-space:pre-wrap;font-size:12px;max-height:320px;overflow:auto;margin:8px 0 0}
.syn{font-size:12px;color:var(--muted);border:1px solid var(--line);border-radius:6px;padding:0 6px}.sub{color:var(--muted)}
</style></head><body><main>
<h1>Backtests</h1><p class="sub"><a href="/">&larr; back to the desk</a></p>
<div class="card"><p>__EXPORT__</p><button id="run" __DISABLED__>Backtest the latest ATAS export</button>
<span class="sub"> runs the defaults, a sweep and Laya's review (one Claude call); minutes, not hours</span><pre id="log"></pre></div>
<div class="card">__TABLE__</div>
<p class="sub">Years of data instead: see BACKTEST_GUIDE.md (Databento). Results in R after costs. Not financial advice.</p>
<script>
(function(){var after=0,log=document.getElementById("log"),btn=document.getElementById("run");
function poll(){fetch("/local/backtest?after="+after).then(function(r){return r.json()}).then(function(v){
 if(v.lines.length){log.textContent+=v.lines.join("\\n")+"\\n";log.scrollTop=log.scrollHeight}after=v.next;
 btn.disabled=v.running||btn.dataset.noexport==="1";btn.textContent=v.running?"Backtest running...":"Backtest the latest ATAS export";
 if(!v.running&&btn.dataset.wasrunning==="1"){btn.dataset.wasrunning="";location.reload()}if(v.running)btn.dataset.wasrunning="1";
}).catch(function(){}).then(function(){setTimeout(poll,1500)})}
btn.onclick=function(){fetch("/local/backtest",{method:"POST",headers:{"X-Laya-Desk":"1","Content-Type":"application/json"},body:"{}"})
 .then(function(r){return r.json()}).then(function(v){if(v.error)log.textContent+=v.error+"\\n"})};
poll()})();
</script></main></body></html>"""
    page = page.replace("__EXPORT__", exp_text).replace("__TABLE__", table)
    page = page.replace("__DISABLED__", "" if exp else 'disabled data-noexport="1"')
    return page.encode("utf-8")


def make_handler(desk: Desk, port: int):
    allowed_hosts = {f"localhost:{port}", f"127.0.0.1:{port}"}
    allowed_origins = {f"http://localhost:{port}", f"http://127.0.0.1:{port}"}

    class H(BaseHTTPRequestHandler):
        server_version = "LayaDesk/1"

        def log_message(self, fmt, *args):  # quiet
            pass

        def _send(self, code: int, body: bytes, ctype="application/json"):
            self.send_response(code)
            self.send_header("Content-Type", ctype + ("; charset=utf-8" if ctype.startswith("text") or ctype.endswith("json") else ""))
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.end_headers()
            self.wfile.write(body)

        def _json(self, code: int, obj):
            self._send(code, json.dumps(obj).encode("utf-8"))

        def _host_ok(self) -> bool:
            return self.headers.get("Host", "") in allowed_hosts

        def _write_ok(self) -> bool:
            origin = self.headers.get("Origin")
            return self.headers.get("X-Laya-Desk") == "1" and (origin is None or origin in allowed_origins)

        def _body(self) -> bytes:
            n = int(self.headers.get("Content-Length") or 0)
            return self.rfile.read(min(n, 2_000_000)) if n > 0 else b""

        def do_GET(self):
            if not self._host_ok():
                return self._send(403, b'{"error":"bad host"}')
            path = self.path
            if path in ("/", "/index.html"):
                try:
                    return self._send(200, DESK.read_bytes(), "text/html")
                except OSError:
                    return self._send(500, b"app/laya-desk.html is missing", "text/plain")
            if path == "/local/info":
                return self._json(200, {"local": True, "bridge": desk.a.bridge, "version": 1})
            if path.startswith("/local/runner"):
                return self._json(200, desk.runner.view(_after(path)))
            if path.startswith("/local/backtest"):
                return self._json(200, desk.backtest.view(_after(path)))
            if path in ("/reports", "/reports/"):
                return self._send(200, reports_page(desk), "text/html")
            m = re.match(r"^/reports/([A-Za-z0-9_.\-]+)/([a-z_.]+)$", path)
            if m and m.group(2) in REPORT_FILES:
                f = REPORTS / m.group(1) / m.group(2)
                if f.resolve().parent.parent == REPORTS.resolve() and f.is_file():
                    return self._send(200, f.read_bytes(), REPORT_FILES[m.group(2)])
                return self._send(404, b"not found", "text/plain")
            if path.startswith("/api/") and SAFE_PATH.match(path[4:]):
                code, body = desk.bridge("GET", path[4:], b"")
                return self._send(code, body)
            return self._send(404, b'{"error":"not found"}')

        def do_POST(self):
            if not self._host_ok() or not self._write_ok():
                return self._send(403, b'{"error":"not allowed"}')
            path, raw = self.path, self._body()
            try:
                data = json.loads(raw or b"{}")
            except ValueError:
                return self._json(400, {"error": "bad json"})
            if path.startswith("/api/") and SAFE_PATH.match(path[4:]):
                code, body = desk.bridge("POST", path[4:], raw or b"{}")
                return self._send(code, body)
            if path == "/local/runner/start":
                model = str(data.get("model") or "")[:40]
                skill = str(data.get("skill") or "")[:80]
                if model and not re.match(r"^[A-Za-z0-9.\-_\[\]]+$", model):
                    return self._json(400, {"error": "bad model name"})
                if skill and not re.match(r"^/?[A-Za-z0-9:_\-]+$", skill):
                    return self._json(400, {"error": "bad skill command"})
                ok = desk.runner.start(desk.runner_argv(model, skill), ROOT)
                return self._json(200, {"started": ok, "running": desk.runner.running()})
            if path == "/local/runner/stop":
                desk.runner.stop()
                return self._json(200, {"running": desk.runner.running()})
            if path == "/local/ask":
                prompt = str(data.get("prompt") or "")
                if not prompt or len(prompt) > 200_000:
                    return self._json(400, {"error": "empty or too long"})
                if not desk.ask_lock.acquire(blocking=False):
                    return self._json(429, {"error": "Laya is already answering a question"})
                try:
                    text = desk.ask(prompt, str(data.get("model") or "")[:40], str(data.get("skill") or "")[:80])
                    return self._json(200, {"text": text})
                except Exception as e:  # noqa: BLE001 - the desk shows the reason
                    return self._json(500, {"error": f"Laya could not answer: {e}"})
                finally:
                    desk.ask_lock.release()
            if path == "/local/backtest":
                exp = desk.latest_export()
                if exp is None:
                    return self._json(400, {"error": "no ATAS export found in " + desk.a.exports})
                ok = desk.backtest.start(["/bin/bash", str(ROOT / "backtest-atas.sh"), "--no-open", str(exp)], ROOT)
                return self._json(200, {"started": ok})
            return self._json(404, {"error": "not found"})

    return H


def _r(v) -> str:
    try:
        return f"{float(v):+.1f}" if v is not None else "-"
    except (TypeError, ValueError):
        return "-"


def _after(path: str) -> int:
    m = re.search(r"after=(-?\d+)", path)
    return int(m.group(1)) if m else 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Laya's local dashboard (the Laya Tape Desk in your browser).")
    ap.add_argument("--port", type=int, default=int(os.environ.get("LAYA_DESK_PORT", "8770")))
    ap.add_argument("--bridge", default=os.environ.get("FABIO_BRIDGE_URL", "http://localhost:8766"))
    ap.add_argument("--token", default=os.environ.get("FABIO_BRIDGE_TOKEN", ""))
    ap.add_argument("--claude", default="claude", help="command that runs Claude Code")
    ap.add_argument("--start-runner", action="store_true", help="start Laya on every candle right away")
    ap.add_argument("--model", default="", help="model for Laya's reads when started here (sonnet, opus ...)")
    ap.add_argument("--skill-command", default="", help="e.g. /favio-orderflow-scalp to force-load the skill")
    ap.add_argument("--open", action="store_true", help="open the dashboard in the browser")
    ap.add_argument("--ask-seconds", type=float, default=240)
    ap.add_argument("--exports", default="~/Documents/FabioBacktest", help="where the ATAS indicator writes its exports")
    a = ap.parse_args()

    desk = Desk(a)
    try:
        srv = ThreadingHTTPServer(("127.0.0.1", a.port), make_handler(desk, a.port))
    except OSError as e:
        print(f"port {a.port} is busy ({e}). Is the dashboard already running? Or use --port.", file=sys.stderr)
        return 1
    url = f"http://localhost:{a.port}"
    print(f"Laya's dashboard: {url}   (bridge {a.bridge}; Ctrl+C stops the dashboard and Laya)", flush=True)
    if a.start_runner:
        desk.runner.start(desk.runner_argv(a.model, a.skill_command), ROOT)
        print("Laya started: she reads every closed candle once the ATAS strategy's bridge is up.", flush=True)
    if a.open:
        threading.Timer(0.8, lambda: webbrowser.open(url)).start()

    def _watch():
        while True:
            time.sleep(5)
            try:
                desk.runner.watchdog_tick()
            except Exception as e:  # noqa: BLE001 - the watchdog must never die
                print(f"watchdog: {e}", flush=True)

    threading.Thread(target=_watch, daemon=True).start()

    def _stop(*_):
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, _stop)
    try:
        srv.serve_forever(poll_interval=0.5)
    except KeyboardInterrupt:
        pass
    finally:
        print("stopping ...", flush=True)
        desk.runner.stop()
        desk.backtest.stop()
        srv.server_close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
