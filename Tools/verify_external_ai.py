#!/usr/bin/env python3
"""Exercise the real Godot single-player loop through its external-AI HTTP API."""
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REPORT = ROOT / "Reports/external-ai-godot-smoke.json"
LOG = ROOT / "Reports/external-ai-godot-smoke.log"


def main():
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def call(path, value=None):
        data = None if value is None else json.dumps(value).encode()
        req = urllib.request.Request(base + path, data=data,
                                     headers={} if data is None else {"Content-Type": "application/json"})
        with opener.open(req, timeout=3) as response:
            return response.status, json.load(response)

    REPORT.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("w", encoding="utf-8") as output:
        process = subprocess.Popen([
            "godot", "--headless", "--path", str(ROOT), "--", "--players=3", "--external-ai=1,2",
            f"--ai-api-port={port}", "--ticks=240", "--timeout=60", f"--report={REPORT}",
        ], cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 30
            while True:
                try:
                    _, roster = call("/api/v1/players")
                    break
                except (OSError, urllib.error.URLError):
                    if process.poll() is not None or time.monotonic() > deadline:
                        raise RuntimeError("Godot did not expose the API; inspect " + str(LOG))
                    time.sleep(0.1)
            assert [p["id"] for p in roster["players"] if p["controller"] == "external"] == [1, 2]
            receipts = []
            for player in (1, 2):
                _, state = call(f"/api/v1/players/{player}/state")
                assert all(e["owner"] == player for e in state["units"]), "initial fog leaked enemy units"
                barracks = next(b for b in state["buildings"] if b["own"] and b["type"] == "Barracks")
                orders = [{"type": "Train", "actors": [barracks["id"]],
                           "unit": "Conscript" if player == 1 else "Gi"}]
                if player == 1:
                    unit = next(u for u in state["units"] if u["own"] and u["type"] == "Rhino")
                    orders.append({"type": "Move", "actors": [unit["id"]], "x": unit["x"] - 2, "y": unit["y"]})
                value = {"matchId": state["matchId"], "requestId": f"godot-player-{player}", "commands": orders}
                status, receipt = call(f"/api/v1/players/{player}/commands", value)
                assert status == 202
                receipts.append((player, value, receipt))
            for player, value, receipt in receipts:
                deadline = time.monotonic() + 5
                while receipt["status"] == "queued" and time.monotonic() < deadline:
                    time.sleep(0.05)
                    _, receipt = call(receipt["receiptUrl"])
                assert receipt["status"] == "applied" and all(r["accepted"] for r in receipt["results"]), receipt
                status, retry = call(f"/api/v1/players/{player}/commands", value)
                assert status == 200 and retry["requestId"] == receipt["requestId"]
            chat_value = {"matchId": roster["matchId"], "requestId": "godot-chat-all", "channel": "all", "text": "AI1 public"}
            status, public = call("/api/v1/players/1/messages", chat_value)
            assert status == 201 and public["message"]["senderId"] == 1
            assert call("/api/v1/players/1/messages", chat_value)[0] == 200
            for player in (1, 2):
                value = {"matchId": roster["matchId"], "requestId": f"godot-chat-team-{player}", "channel": "team", "text": f"AI{player} private"}
                assert call(f"/api/v1/players/{player}/messages", value)[0] == 201
            for player in (1, 2):
                _, page = call(f"/api/v1/players/{player}/messages?after=0&limit=100&matchId={roster['matchId']}")
                texts = [m["text"] for m in page["messages"]]
                assert "AI1 public" in texts and f"AI{player} private" in texts and f"AI{3-player} private" not in texts
                _, empty = call(f"/api/v1/players/{player}/messages?after={page['nextCursor']}")
                assert not empty["messages"]
            assert process.wait(timeout=60) == 0, "Godot failed"
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
    result = json.loads(REPORT.read_text())
    assert result["passed"] and result["replayVerified"] and result["accepted"] == 3 and result["rejected"] == 0, result
    assert result["tick"] == 240 and result["players"] == 3
    print(json.dumps({"passed": True, "godotHttpIntegration": True, "allTeamChatVerified": True, "externalPlayers": [1, 2],
                      "accepted": result["accepted"], "rejected": result["rejected"],
                      "replayVerified": result["replayVerified"], "tick": result["tick"],
                      "hash": result["hash"], "report": str(REPORT), "log": str(LOG)}, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
