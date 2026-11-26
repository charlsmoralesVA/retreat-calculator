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
