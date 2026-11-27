#!/usr/bin/env python3
"""
fabio-atas MCP server: gives Laya (Claude with the Fabio skill) tools to work with the
"Fabio Orderflow Strategy" running in ATAS / ATAS X on this machine.

The strategy exposes a local bridge (http://localhost:8765 by default). This server only talks to that bridge:
  atas_status      - is ATAS reachable, and what the engine sees now (bias, levels, VWAP, checklist, history)
  wait_for_setup   - block until a set-up needs a decision (or the timeout passes)
  list_setups      - set-ups by status (pending / approved / rejected / expired / taken / skipped / all)
  decide_setup     - approve or reject a pending set-up, with the reason (required)
  journal          - recent events: set-ups, decisions, entries, exits
  recent_candles   - closed candles with footprint, zone and approach context (and Laya's reads)
  wait_for_candle  - block until the next candle closes
  review_candle    - record Laya's read of a candle (alerts reach ATAS as notifications)
  todays_plan      - Laya's pre-market map for the session

Laya can only APPROVE or VETO a set-up. Stops, targets, loss limits and the session-end flatten stay in the
strategy's code and cannot be changed from here.

Settings (environment variables):
  FABIO_BRIDGE_URL    default http://localhost:8765
  FABIO_BRIDGE_TOKEN  set it if you set "Bridge token" in the strategy settings
"""
import json
import os
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

try:  # mcp 2.x
    from mcp.server.mcpserver import MCPServer as _Server
except ImportError:  # mcp 1.x
    from mcp.server.fastmcp import FastMCP as _Server

BASE = os.environ.get("FABIO_BRIDGE_URL", "http://localhost:8765").rstrip("/")
TOKEN = os.environ.get("FABIO_BRIDGE_TOKEN", "")
INSTRUCTIONS_FILE = Path(__file__).with_name("LAYA_FABIO_INSTRUCTIONS.md")

server = _Server("fabio-atas")


def _call(method: str, path: str, body: dict | None = None, timeout: float = 10.0) -> dict:
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if TOKEN:
        req.add_header("X-Bridge-Token", TOKEN)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode("utf-8") or "{}")
    except urllib.error.HTTPError as e:
        try:
            detail = json.loads(e.read().decode("utf-8"))
        except Exception:
            detail = {"error": str(e)}
        detail.setdefault("ok", False)
        detail["http_status"] = e.code
        return detail
    except (urllib.error.URLError, TimeoutError, ConnectionError) as e:
        return {
            "ok": False,
            "error": f"ATAS bridge not reachable at {BASE}: {e}. Is ATAS running with the "
                     "'Fabio Orderflow Strategy' on a chart and 'Bridge for Laya enabled'?",
        }


@server.tool()
def atas_status() -> dict:
    """Check the connection to ATAS and get the current market picture from the Fabio engine:
    bias (value-area shifts, 5-session narrative, 3-month composition), prior POC/VAH/VAL, location vs value,
    session VWAP, initial balance, zones, the live checklist of the nearest zone and the history statistics."""
    health = _call("GET", "/health")
    if not health.get("ok"):
        return health
    return {"ok": True, "bridge": health, "state": _call("GET", "/state")}


@server.tool()
def wait_for_setup(timeout_seconds: int = 60) -> dict:
    """Wait until the strategy publishes a set-up that needs Laya's decision. Returns the pending set-ups
    (each with its full checklist, entry, stop, target) or timed_out=true. Max 120 seconds per call."""
    t = max(1, min(120, int(timeout_seconds)))
    return _call("GET", f"/setups/wait?timeout={t}", timeout=t + 15)


@server.tool()
def list_setups(status: str = "pending", limit: int = 10) -> dict:
    """List set-ups, newest first. status: pending, approved, rejected, expired, auto, taken, skipped or all."""
    q = urllib.parse.urlencode({"status": status, "limit": max(1, int(limit))})
    return _call("GET", f"/setups?{q}")


@server.tool()
def decide_setup(setup_id: str, approve: bool, reason: str) -> dict:
    """Approve or reject a PENDING set-up. Always give the reason in one or two sentences, citing the evidence
    (bias, location, approach, absorption, who won the battle, confluence). A set-up not decided before the
    strategy's approval timeout expires and is not traded."""
    if not reason or not reason.strip():
        return {"ok": False, "error": "a reason is required"}
    sid = urllib.parse.quote(setup_id, safe="")
    return _call("POST", f"/setups/{sid}/decision", {"approve": bool(approve), "reason": reason.strip()})


@server.tool()
def recent_candles(after_seq: int = -1, limit: int = 10) -> dict:
    """Closed candles (oldest first) with the bar, footprint summary, zone/approach context, the engine checklist and
    Laya's earlier read if any. after_seq=-1 returns the latest `limit` candles."""
    q = urllib.parse.urlencode({"after": int(after_seq), "limit": max(1, min(50, int(limit)))})
    return _call("GET", f"/candles?{q}")


@server.tool()
def wait_for_candle(after_seq: int, timeout_seconds: int = 60) -> dict:
    """Wait until a candle after `after_seq` closes (use the last seq you reviewed). Returns the new candle(s) or
    timed_out=true. Max 120 seconds per call."""
    t = max(1, min(120, int(timeout_seconds)))
    return _call("GET", f"/candles/wait?after={int(after_seq)}&timeout={t}", timeout=t + 15)


@server.tool()
def review_candle(seq: int, read: str, phase: str = "approaching_zone", action: str = "wait",
                  confidence: int = 0, next_condition: str = "") -> dict:
    """Record Laya's read of one candle. phase: away_from_zones, approaching_zone, battle_at_zone, setup_long,
    setup_short, invalidated, in_trade. action: wait, watch, alert_long, alert_short (alerts notify the trader in ATAS)."""
    if not read or not read.strip():
        return {"ok": False, "error": "a read is required"}
    body = {"read": read.strip(), "phase": phase, "action": action, "confidence": max(0, min(100, int(confidence))),
            "next": next_condition, "by": "laya-chat"}
    return _call("POST", f"/candles/{int(seq)}/review", body)


@server.tool()
def todays_plan() -> dict:
    """Laya's pre-market map for the current session: bias and why, key levels, if-then scenarios, no-trade conditions."""
    return _call("GET", "/plan")


@server.tool()
def journal(limit: int = 30) -> dict:
    """Recent events from the strategy: set-ups, Laya's decisions, entries, exits and why trades were skipped."""
    return _call("GET", f"/journal?limit={max(1, int(limit))}")


@server.prompt()
def fabio_review() -> str:
    """Instructions for reviewing ATAS set-ups the way Fabio's method selects trades."""
    try:
        return INSTRUCTIONS_FILE.read_text(encoding="utf-8")
    except OSError:
        return "Review each pending set-up with the Fabio orderflow skill: bias -> location -> confirmation."


if __name__ == "__main__":
    server.run()
