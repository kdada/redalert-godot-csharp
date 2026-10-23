#!/usr/bin/env python3
"""Run real Godot/ENet peers; optional UDP loss/jitter. No game window or replacement server."""
from __future__ import annotations
import argparse
import heapq
import hashlib
import shutil
import json
import os
from pathlib import Path
import random
import selectors
import signal
import socket
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parents[1]

class DatagramProxy:
    def __init__(self, server_port: int, seed: int, loss: float, jitter_ms: float):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", 0))
        self.sock.setblocking(False)
        self.port = self.sock.getsockname()[1]
        self.server = ("127.0.0.1", server_port)
        self.client = None
        self.random = random.Random(seed)
        self.loss, self.jitter = loss, jitter_ms / 1000
        self.dropped = self.forwarded = 0
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        selector = selectors.DefaultSelector()
        selector.register(self.sock, selectors.EVENT_READ)
        queue = []
        serial = 0
        try:
            while not self.stop.is_set():
                now = time.monotonic()
                while queue and queue[0][0] <= now:
                    _, _, data, target = heapq.heappop(queue)
                    self.sock.sendto(data, target)
                    self.forwarded += 1
                for _, _ in selector.select(.005):
                    data, source = self.sock.recvfrom(65535)
                    if source == self.server:
                        target = self.client
                    else:
                        self.client = source
                        target = self.server
                    if target is None:
                        continue
                    if self.random.random() < self.loss:
                        self.dropped += 1
                        continue
                    serial += 1
                    heapq.heappush(queue, (time.monotonic() + self.random.random() * self.jitter, serial, data, target))
        finally:
            selector.close()
            self.sock.close()

    def close(self):
        self.stop.set()
        self.thread.join(2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--players", type=int, default=8, choices=range(2, 9))
    parser.add_argument("--ticks", type=int, default=2400)
    parser.add_argument("--timeout", type=int, default=150)
    parser.add_argument("--loss", type=float, default=0)
    parser.add_argument("--jitter-ms", type=float, default=0)
    parser.add_argument("--dedicated", action="store_true")
    parser.add_argument("--godot", default="godot")
    parser.add_argument("--report-dir", type=Path)
    args = parser.parse_args()
    if not 0 <= args.loss <= .2 or not 0 <= args.jitter_ms <= 500:
        parser.error("loss or jitter outside test bounds")
    label = f"headless-{args.players}{'-impaired' if args.loss or args.jitter_ms else ''}{'-dedicated' if args.dedicated else ''}-{time.time_ns()}"
    reports = args.report_dir.resolve() if args.report_dir else ROOT / "Reports" / label
    reports.mkdir(parents=True)
    # Freeze assemblies so an independent build cannot mutate a running match.
    snapshot = Path(os.environ["XDG_CACHE_HOME"]) / "redalert-command" / "matches" / label
    snapshot.mkdir(parents=True)
    for filename in ("project.godot", "RedAlert.csproj"):
        shutil.copy2(ROOT / filename, snapshot / filename)
    for folder in ("Scripts", "Scenes"):
        shutil.copytree(ROOT / folder, snapshot / folder)
    # project.godot can autoload the editor's runtime helper; preserve that
    # dependency in the isolated snapshot instead of suppressing engine errors.
    if (ROOT / "addons").is_dir():
        shutil.copytree(ROOT / "addons", snapshot / "addons")
    binary = ROOT / ".godot/mono/temp/bin/Debug"
    shutil.copytree(binary, snapshot / ".godot/mono/temp/bin/Debug")
    metadata = ROOT / ".godot/mono/metadata"
    if metadata.exists():
        shutil.copytree(metadata, snapshot / ".godot/mono/metadata")
    build_hashes = {name: hashlib.sha256((binary / name).read_bytes()).hexdigest() for name in ("RedAlert.dll", "RedAlert.Simulation.dll")}
    engine_errors = []
    probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    probe.bind(("127.0.0.1", 0))
    port = probe.getsockname()[1]
    probe.close()
    processes = []
    readers = []
    proxies = []
    ready = threading.Event()

    def terminate(_signum, _frame):
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, terminate)

    def start(role: str, options: list[str], fps: int):
        command = [args.godot, "--headless", "--path", str(snapshot), "--max-fps", str(fps), "--log-file", str(reports / f"{role}.godot.log"), "--", *options, f"--report={reports / (role + '.json')}", f"--timeout={args.timeout}"]
        process = subprocess.Popen(command, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        processes.append((role, process))
        def read_output():
            with (reports / f"{role}.log").open("w") as log:
                for line in process.stdout:
                    log.write(line)
                    log.flush()
                    if role == "server" and "SERVER LISTEN" in line:
                        ready.set()
                    if "ERROR:" in line or "FATAL:" in line:
                        engine_errors.append(f"{role}: {line.strip()}")
                    if "TEST COMPLETE" in line or "ERROR:" in line:
                        print(f"[{role}] {line.strip()}", flush=True)
        reader = threading.Thread(target=read_output, daemon=True)
        reader.start()
        readers.append(reader)
        return process

    try:
        server_options = ["--server", "--fast", "--bot", f"--players={args.players}", f"--ticks={args.ticks}", f"--port={port}", "--country=America"]
        if args.dedicated:
            server_options.append("--dedicated")
        start("server", server_options, 120)
        if not ready.wait(30):
            raise RuntimeError(f"Server did not become ready; inspect {reports / 'server.log'}")
        country_names = ["America", "Russia", "France", "Britain"]
        count = args.players if args.dedicated else args.players - 1
        for index in range(count):
            slot = index if args.dedicated else index + 1
            client_port = port
            if args.loss or args.jitter_ms:
                proxy = DatagramProxy(port, 1100 + index, args.loss, args.jitter_ms)
                proxies.append(proxy)
                client_port = proxy.port
            start(f"client-{index+1}", ["--join=127.0.0.1", "--bot", f"--port={client_port}", f"--name=Client-{index+1}", f"--country={country_names[slot % 4]}"], 45 + index * 7)
        deadline = time.monotonic() + args.timeout + 15
        for role, process in processes:
            code = process.wait(timeout=max(1, deadline - time.monotonic()))
            if code != 0:
                raise RuntimeError(f"{role} exited {code}; inspect {reports / (role+'.log')}")
        for reader in readers:
            reader.join(2)
        assert not engine_errors, "Native/managed engine errors: " + repr(engine_errors)
        peer_reports = [json.loads((reports / f"{role}.json").read_text()) for role, _ in processes]
        assert all(r["passed"] and r["replayVerified"] for r in peer_reports), "peer/replay verification failed"
        assert all(r["tick"] == args.ticks for r in peer_reports), "incomplete tick streams"
        assert len({r["hash"] for r in peer_reports}) == 1, "final hashes diverged"
        assert all(r["players"] == args.players for r in peer_reports), "player cap/count incorrect"
        if args.players >= 4:
            assert set(peer_reports[0]["countries"]) == {"America", "Russia", "France", "Britain"}, "four-country lobby coverage missing"
        assert all(r["rejectedPackets"] == 0 for r in peer_reports), "unexpected packet rejection"
        assert peer_reports[0]["received"] > 0, "server did not receive real player orders"
        assert all(r["submitted"] > 0 for r in peer_reports[1:]), "client commands did not traverse ENet"
        summary = {"passed": True, "players": args.players, "processes": len(processes), "ticks": args.ticks, "simulatedSeconds": args.ticks / 20, "hash": peer_reports[0]["hash"], "loss": args.loss, "jitterMs": args.jitter_ms, "proxyDroppedDatagrams": sum(p.dropped for p in proxies), "proxyForwardedDatagrams": sum(p.forwarded for p in proxies), "reports": str(reports), "snapshot": str(snapshot), "buildHashes": build_hashes, "peers": peer_reports}
        (reports / "summary.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False))
        print(json.dumps({k:v for k,v in summary.items() if k != "peers"}, indent=2), flush=True)
        return 0
    finally:
        for _, process in processes:
            if process.poll() is None:
                process.terminate()
        for _, process in processes:
            if process.poll() is None:
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
        for proxy in proxies:
            proxy.close()

if __name__ == "__main__":
    raise SystemExit(main())
