#!/usr/bin/env python3
"""
Laya runner: Laya reviews EVERY closed candle in ATAS with the Fabio skill.

  ATAS strategy (bridge, localhost:8765) --new candle--> this runner --claude -p--> Laya (Claude Code + Fabio skill)
                                         <--review / set-up decision--

Waiting for candles costs nothing (it is a local long-poll). Claude is called once per closed candle (once for a batch
if Laya fell behind, so no candle is ever skipped). Each call uses your Claude subscription through Claude Code.

  python3 laya_runner.py                       # watch the strategy's bridge on localhost:8765
  python3 laya_runner.py --model sonnet --skill-command /fabio-orderflow-strategy
  python3 laya_runner.py --bridge http://localhost:8765 --token SECRET --max-seconds 150

Needs: Python 3.9+, Claude Code installed and logged in (`claude` on PATH), no ANTHROPIC_API_KEY set if you want the
subscription to be used. Standard library only.
"""
import argparse
import datetime as dt
import json
import os
import shlex
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
INSTRUCTIONS = HERE / "LAYA_CANDLE_INSTRUCTIONS.md"
PHASES = {"away_from_zones", "approaching_zone", "battle_at_zone", "setup_long", "setup_short", "invalidated", "in_trade", "error", "skipped"}
ACTIONS = {"wait", "watch", "alert_long", "alert_short"}


class Bridge:
    def __init__(self, base: str, token: str):
        self.base = base.rstrip("/")
        self.token = token

    def call(self, method: str, path: str, body=None, timeout: float = 15.0) -> dict:
        data = json.dumps(body).encode("utf-8") if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        if self.token:
            req.add_header("X-Bridge-Token", self.token)
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read().decode("utf-8") or "{}")
        except urllib.error.HTTPError as e:
            try:
                d = json.loads(e.read().decode("utf-8"))
            except Exception:
                d = {"error": str(e)}
            d["ok"] = False
            d["http_status"] = e.code
            return d


def compact(card: dict) -> dict:
    """A recent candle without the long checklist, plus Laya's earlier read of it."""
    c = {k: card[k] for k in ("seq", "time", "bar", "footprint", "engine_signal") if k in card}
    ctx = card.get("context", {})
    c["zone"] = {k: ctx.get(k) for k in ("nearest_zone", "nearest_zone_price", "distance_to_zone_ticks", "approach")}
    r = card.get("laya_review")
    if r:
        c["your_read"] = {k: r.get(k) for k in ("phase", "action", "read")}
    return c


def extract_json(text: str):
    text = (text or "").strip()
    for candidate in (text,):
        try:
            return json.loads(candidate)
        except Exception:
            pass
    if "```" in text:
        for block in text.split("```")[1::2]:
            block = block[4:] if block.startswith("json") else block
            try:
                return json.loads(block.strip())
            except Exception:
                pass
    a, b = text.find("{"), text.rfind("}")
    if a >= 0 and b > a:
        return json.loads(text[a:b + 1])
    raise ValueError("no JSON object in Laya's reply")


def build_prompt(instructions: str, skill_command: str, state: dict, recent: list, new: list, pending: list) -> str:
    payload = {
        "state": state,
        "recent": [compact(c) for c in recent],
        "new": new,
        "pending_setups": [
            {"id": t["id"], "status": t["status"], "setup": {k: t["setup"].get(k) for k in
                                                             ("side", "tag", "entry", "stop", "target", "rr", "checklist_text")}}
            for t in pending if t.get("setup")
        ],
    }
    head = (skill_command.strip() + "\n\n") if skill_command else ""
    seqs = [c["seq"] for c in new]
    return (head + instructions + "\n\n---\n"
            + f"NEW_CANDLE_SEQS: {json.dumps(seqs)}\n"
            + f"PENDING_SETUP_IDS: {json.dumps([p['id'] for p in payload['pending_setups']])}\n"
            + "DATA:\n" + json.dumps(payload, separators=(",", ":"), default=str)
            + "\n\nReply with only the JSON object described above.")


def ask_laya(cmd: list, prompt: str, model: str, max_seconds: float) -> dict:
    args = list(cmd) + ["-p", "--output-format", "json", "--max-turns", "6", "--permission-mode", "dontAsk"]
    if model:
        args += ["--model", model]
    env = dict(os.environ)
    env.setdefault("CLAUDE_CODE_SYNC_SKILLS", "1")      # make sure the claude.ai-synced Fabio skill is loaded
    p = subprocess.run(args, input=prompt, capture_output=True, text=True, timeout=max_seconds, env=env)
    if p.returncode != 0 and not p.stdout.strip():
        raise RuntimeError(f"claude exited {p.returncode}: {p.stderr.strip()[:300]}")
    out = p.stdout.strip()
    try:
        envelope = json.loads(out)
    except json.JSONDecodeError:
        envelope = {"result": out}
    if isinstance(envelope, dict) and envelope.get("subtype") not in (None, "success"):
        raise RuntimeError(f"claude run ended with {envelope.get('subtype')}: {str(envelope.get('result'))[:300]}")
    result = envelope.get("result", out) if isinstance(envelope, dict) else out
    return extract_json(result if isinstance(result, str) else json.dumps(result))


def clean_review(r: dict, model: str) -> dict:
    phase = str(r.get("phase", "")).strip()
    action = str(r.get("action", "wait")).strip()
    out = {
        "read": str(r.get("read", "")).strip() or "(no read given)",
        "phase": phase if phase in PHASES else "approaching_zone" if phase else "away_from_zones",
        "action": action if action in ACTIONS else "watch",
        "next": str(r.get("next", "")).strip(),
        "by": "laya",
        "model": model or "default",
    }
    try:
        out["confidence"] = max(0, min(100, int(r.get("confidence", 0))))
    except (TypeError, ValueError):
        out["confidence"] = 0
    return out


def log(msg: str):
    print(dt.datetime.now().strftime("%H:%M:%S"), msg, flush=True)


def main() -> int:
    ap = argparse.ArgumentParser(description="Laya reviews every closed ATAS candle with the Fabio skill.")
    ap.add_argument("--bridge", default=os.environ.get("FABIO_BRIDGE_URL", "http://localhost:8765"))
    ap.add_argument("--token", default=os.environ.get("FABIO_BRIDGE_TOKEN", ""))
    ap.add_argument("--claude", default="claude", help="command that runs Claude Code (default: claude)")
    ap.add_argument("--model", default="", help="sonnet, opus, haiku ... (default: your Claude Code default)")
    ap.add_argument("--skill-command", default="", help="e.g. /fabio-orderflow-strategy to force-load the skill")
    ap.add_argument("--max-seconds", type=float, default=150, help="give up on one review after this many seconds")
    ap.add_argument("--context", type=int, default=8, help="earlier candles sent as context")
    ap.add_argument("--max-batch", type=int, default=3, help="most candles reviewed in one call when catching up")
    ap.add_argument("--from-seq", type=int, default=None, help="review from this candle seq (default: only new candles)")
    ap.add_argument("--exit-when-bridge-down", action="store_true", help="stop instead of waiting when the bridge disappears")
    ap.add_argument("--once", action="store_true", help="review one batch and stop")
    a = ap.parse_args()

    bridge = Bridge(a.bridge, a.token)
    cmd = shlex.split(a.claude)
    instructions = INSTRUCTIONS.read_text(encoding="utf-8")
    stop = {"now": False}

    def _terminate(*_):
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, _terminate)          # Stop from the desk app or Ctrl+C: leave at once, even mid-wait

    try:
        return run(a, bridge, cmd, instructions, stop)
    except KeyboardInterrupt:
        log("Laya runner stopped")
        return 0


def run(a, bridge, cmd, instructions, stop) -> int:
    after = a.from_seq - 1 if a.from_seq is not None else None
    log(f"Laya runner -> bridge {a.bridge} (model: {a.model or 'default'})")
    while not stop["now"]:
        try:
            if after is None:
                info = bridge.call("GET", "/candles?limit=1")
                if info.get("ok") is False:
                    raise urllib.error.URLError(info.get("error", "bridge error"))
                after = int(info.get("last_seq", -1)) - 1      # start with a read of the latest closed candle
                log(f"connected; reviewing from seq {after + 1}")
            w = bridge.call("GET", f"/candles/wait?after={after}&timeout=60", timeout=80)
        except (urllib.error.URLError, ConnectionError, TimeoutError, OSError) as e:
            if a.exit_when_bridge_down:
                log(f"bridge not reachable ({e}); stopping")
                return 0
            log(f"bridge not reachable ({e}); is ATAS running with the Fabio strategy and the bridge on? retrying in 10 s")
            time.sleep(10)
            continue
        new = w.get("candles", [])
        if not new:
            continue
        if len(new) > a.max_batch:                          # behind (started late, Mac slept): read the latest ones only
            for c in new[:-a.max_batch]:
                bridge.call("POST", f"/candles/{c['seq']}/review", {"read": "skipped: Laya was catching up", "phase": "skipped",
                                                                    "action": "wait", "by": "laya", "confidence": 0})
            log(f"caught up: {len(new) - a.max_batch} older candle(s) marked skipped")
            new = new[-a.max_batch:]

        recent = bridge.call("GET", f"/candles?after={max(-1, new[0]['seq'] - a.context - 1)}&limit={a.context + len(new)}").get("candles", [])
        recent = [c for c in recent if c["seq"] < new[0]["seq"]][-a.context:]
        state = bridge.call("GET", "/state")
        pending = bridge.call("GET", "/setups?status=pending&limit=5").get("setups", [])
        span = f"seq {new[0]['seq']}" + (f"-{new[-1]['seq']}" if len(new) > 1 else "")
        log(f"candle {span} closed ({new[-1].get('time', '')}); Laya is reading it" + (f" + {len(pending)} pending set-up(s)" if pending else ""))

        t0 = time.time()
        try:
            answer = ask_laya(cmd, build_prompt(instructions, a.skill_command, state, recent, new, pending), a.model, a.max_seconds)
            reviews = {int(r.get("seq", -1)): r for r in answer.get("reviews", []) if isinstance(r, dict)}
            for c in new:
                r = reviews.get(c["seq"]) or (reviews.get(-1) if len(new) == 1 else None)
                body = clean_review(r, a.model) if r else {"read": "Laya returned no read for this candle.", "phase": "error",
                                                           "action": "wait", "by": "laya", "confidence": 0}
                res = bridge.call("POST", f"/candles/{c['seq']}/review", body)
                log(f"  seq {c['seq']}: {body['phase']} / {body['action']} ({body.get('confidence', 0)}%) - {body['read']}"
                    + ("" if res.get("ok") else f"  [not stored: {res.get('error')}]"))
            pending_ids = {p["id"] for p in pending}
            for d in answer.get("setup_decisions", []) or []:
                if not isinstance(d, dict) or d.get("id") not in pending_ids:
                    continue
                res = bridge.call("POST", f"/setups/{urllib.parse.quote(str(d['id']), safe='')}/decision",
                                  {"approve": bool(d.get("approve")), "reason": str(d.get("reason", "")).strip() or "no reason given"})
                log(f"  set-up {d['id']}: {'APPROVED' if d.get('approve') else 'REJECTED'} - {d.get('reason', '')}"
                    + ("" if res.get("ok") else f"  [not accepted: {res.get('error')}]"))
            log(f"  ({time.time() - t0:.0f} s)")
        except subprocess.TimeoutExpired:
            note = f"Laya took longer than {a.max_seconds:.0f} s; candle not reviewed."
            bridge.call("POST", f"/candles/{new[-1]['seq']}/review", {"read": note, "phase": "error", "action": "wait", "by": "laya"})
            log("  " + note)
        except Exception as e:  # keep running whatever one review does
            note = f"Laya could not review this candle: {str(e)[:200]}"
            bridge.call("POST", f"/candles/{new[-1]['seq']}/review", {"read": note, "phase": "error", "action": "wait", "by": "laya"})
            log("  " + note)

        after = new[-1]["seq"]
        if a.once:
            break
    log("Laya runner stopped")
    return 0


if __name__ == "__main__":
    sys.exit(main())
