#!/usr/bin/env python3
"""Render the project's original audio with Python's standard library only.

No samples, commercial soundtrack, recorded voices, or copied melodies are used.
The deterministic score is an original industrial/military instrumental. WAVs are
rendered offline; Godot never invokes this script. See Reports/AUDIO.md.
"""
from __future__ import annotations

import argparse
from array import array
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import sys
import wave

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Generated" / "Audio"
RATE = 22050
TAU = math.tau
BPM = 116
BEAT = 60.0 / BPM
BARS = 16
LOOP_SECONDS = BARS * 4 * BEAT
SCORE_SEED = 0x1A0D2026


def hz(midi: int) -> float:
    return 440 * 2 ** ((midi - 69) / 12)


def silence(seconds: float) -> array:
    return array("f", [0]) * round(seconds * RATE)


def layer(dst: array, clip: array, seconds: float, gain: float = 1.0, wrap: bool = False) -> None:
    begin = round(seconds * RATE)
    n = len(dst)
    for i, value in enumerate(clip):
        at = begin + i
        if wrap:
            dst[at % n] += value * gain
        elif 0 <= at < n:
            dst[at] += value * gain


def fade_edges(clip: array, milliseconds: float = 3.0) -> array:
    n = min(len(clip) // 2, round(milliseconds * RATE / 1000))
    for i in range(n):
        gain = i / max(1, n)
        clip[i] *= gain
        clip[-i - 1] *= gain
    return clip


def tone(midi: int, seconds: float, kind: str = "bell", seed: int = 0) -> array:
    clip = silence(seconds)
    frequency = hz(midi)
    rng = random.Random(seed)
    low = 0.0
    for i in range(len(clip)):
        t = i / RATE
        attack = min(1.0, t / .006)
        release = min(1.0, (seconds - t) / .04)
        phase = TAU * frequency * t
        if kind == "bass":
            low += .18 * (rng.uniform(-1, 1) - low)
            value = math.tanh(2.7 * (math.sin(phase) + .38 * math.sin(phase * 2) + .13 * low))
            env = attack * release * math.exp(-t * 1.8)
        elif kind == "steel":
            value = math.sin(phase + 1.8 * math.sin(phase * 2.003) * math.exp(-t * 9))
            value += .24 * math.sin(phase * 3.01) + .13 * math.sin(phase * 4.97)
            env = attack * release * math.exp(-t * 4.2)
        elif kind == "drive":
            # Band-limited harmonic stack and soft saturation; no sampled guitar.
            raw = sum(math.sin(phase * h) / h for h in (1, 2, 3, 4, 5, 7))
            value = math.tanh(raw * 2.4) * .75 + math.sin(phase * 1.004) * .18
            env = attack * release * (.25 + .75 * math.exp(-t * 9))
        elif kind == "pad":
            value = (math.sin(phase) + .3 * math.sin(phase * 1.002) + .15 * math.sin(phase * 2)) / 1.45
            env = min(1.0, t / .12) * min(1.0, (seconds - t) / .25)
        else:
            value = math.sin(phase) + .3 * math.sin(phase * 2.76) * math.exp(-t * 8)
            env = attack * release * math.exp(-t * 5)
        clip[i] = value * env
    return fade_edges(clip)


def drum(kind: str, seed: int) -> array:
    seconds = {"kick": .38, "snare": .24, "hat": .09, "metal": .27, "tom": .32}[kind]
    clip = silence(seconds)
    rng = random.Random(seed)
    low = last = phase = 0.0
    for i in range(len(clip)):
        t = i / RATE
        noise = rng.uniform(-1, 1)
        low += .13 * (noise - low)
        high = noise - last
        last = noise
        if kind == "kick":
            phase += TAU * (46 + 112 * math.exp(-t * 43)) / RATE
            value = math.sin(phase) * math.exp(-t * 12) + noise * math.exp(-t * 120) * .19
        elif kind == "snare":
            value = (.82 * (noise - low) + .28 * math.sin(TAU * 180 * t)) * math.exp(-t * 19)
        elif kind == "hat":
            value = high * math.exp(-t * 65) * .58
        elif kind == "metal":
            value = sum(math.sin(TAU * f * t) for f in (327, 559, 883, 1327)) * .2 * math.exp(-t * 19)
            value += high * math.exp(-t * 40) * .15
        else:
            phase += TAU * (85 + 95 * math.exp(-t * 18)) / RATE
            value = math.sin(phase) * math.exp(-t * 13) + low * math.exp(-t * 22) * .3
        clip[i] = value
    return fade_edges(clip, 1.0)


def noise_effect(seconds: float, kind: str, seed: int) -> array:
    clip = silence(seconds)
    rng = random.Random(seed)
    low = mid = last = phase = 0.0
    for i in range(len(clip)):
        t = i / RATE
        noise = rng.uniform(-1, 1)
        low += .035 * (noise - low)
        mid += .25 * (noise - mid)
        high = noise - last
        last = noise
        end = min(1.0, (seconds - t) / .025)
        if kind == "rifle":
            pulse = math.exp(-t * 65) + (.7 * math.exp(-(t - .065) * 70) if t >= .065 else 0)
            value = (high * .6 + mid * .9) * pulse + math.sin(TAU * 180 * t) * math.exp(-t * 40) * .27
        elif kind == "sniper":
            value = (noise * .6 + high * .3) * math.exp(-t * 49) + mid * math.exp(-t * 8) * .22
            value += math.sin(TAU * (130 * t + 7 * (1 - math.exp(-t * 20)))) * math.exp(-t * 18) * .65
        elif kind in ("tank", "explosion", "apocalypse"):
            decay = 7 if kind == "explosion" else 12
            phase += TAU * (32 + 80 * math.exp(-t * 14)) / RATE
            value = (low * 4 + mid * .45) * math.exp(-t * decay)
            value += math.sin(phase) * math.exp(-t * (decay + 2)) * .75
            value += high * math.exp(-t * 70) * .4
            if kind == "apocalypse" and t >= .115:
                u = t - .115
                value += (mid * 1.6 + math.sin(TAU * 53 * u) * .7) * math.exp(-u * 11)
        elif kind == "tesla":
            frequency = 350 + 1800 * (1 - t / seconds) ** 2
            phase += TAU * frequency / RATE
            crackle = .5 + .5 * math.sin(TAU * 47 * t)
            value = (math.sin(phase + noise * 1.8) * .55 + high * .3) * crackle * math.exp(-t * 2.7)
        elif kind == "prism":
            phase += TAU * (1800 - 1400 * t / seconds) / RATE
            value = (math.sin(phase) * .6 + math.sin(phase * 1.51) * .2 + high * .09) * math.exp(-t * 7)
        elif kind == "impact":
            value = (mid * .8 + math.sin(TAU * 270 * t) * .3) * math.exp(-t * 32)
        elif kind == "flesh":
            value = (low * 3 + mid * .35) * math.exp(-t * 33)
        elif kind == "flak":
            value = (noise * .8 + math.sin(TAU * 95 * t) * .5) * math.exp(-t * 24)
        elif kind == "dog":
            phase += TAU * (160 + 90 * math.sin(TAU * 15 * t)) / RATE
            value = (math.tanh(math.sin(phase) * 4) * .55 + mid * .4) * math.sin(math.pi * t / seconds) ** 2
        elif kind == "click":
            value = high * math.exp(-t * 180) * .55 + math.sin(TAU * 1500 * t) * math.exp(-t * 120) * .4
        elif kind == "machine":
            pulse = (1 + math.sin(TAU * 18 * t)) ** 4 / 16
            value = (mid * .6 + math.sin(TAU * 94 * t) * .3) * pulse * math.sin(math.pi * t / seconds)
        elif kind == "radio":
            value = (noise - low) * math.exp(-t * 45) * .18
        else:
            raise ValueError(kind)
        clip[i] = value * end
    return fade_edges(clip, 1.0)


def signal(notes: list[tuple[int, float]], kind: str = "bell", spacing: float = .11, tail: float = .35) -> array:
    clip = silence(spacing * len(notes) + tail)
    layer(clip, noise_effect(.09, "radio", 171), 0, .35)
    for n, (pitch, gain) in enumerate(notes):
        layer(clip, tone(pitch, tail, kind, n + 5), n * spacing + .018, gain)
    return fade_edges(clip)


def music(menu: bool) -> tuple[array, array]:
    left, right = silence(LOOP_SECONDS), silence(LOOP_SECONDS)
    cache: dict[tuple, array] = {}

    def put(clip: array, at: float, gain: float, pan: float = 0.0) -> None:
        layer(left, clip, at, gain * math.sqrt((1 - pan) / 2), True)
        layer(right, clip, at, gain * math.sqrt((1 + pan) / 2), True)

    def note(pitch: int, length: float, kind: str, at: float, gain: float, pan: float = 0.0) -> None:
        key = (pitch, length, kind)
        if key not in cache:
            cache[key] = tone(pitch, length, kind, SCORE_SEED + pitch)
        put(cache[key], at, gain, pan)

    hits = {name: drum(name, SCORE_SEED + i) for i, name in enumerate(("kick", "snare", "hat", "metal", "tom"))}
    # Entire score authored here: E pedal, C, D and B responses, 16-bar form.
    roots = [40, 40, 36, 38, 40, 43, 38, 35, 40, 40, 36, 38, 45, 43, 38, 35]
    bass_steps = [0, 0, 7, 0, 10, 0, 7, 3]
    lead = [76, 79, 78, 71, 74, 76, 83, 78, 79, 74, 76, 71, 78, 74, 71, 74]
    for bar, root in enumerate(roots):
        at = bar * 4 * BEAT
        # Backbeat snare with additional quiet march strokes and evolving fills.
        for step in range(8):
            t = at + step * BEAT / 2
            put(hits["hat"], t, .045 if menu else (.08 if step % 2 else .12), .38 if step % 2 else -.25)
            if step in (0, 3, 4, 6):
                put(hits["kick"], t, .22 if menu else .44)
            if step in (2, 6):
                put(hits["snare"], t, .10 if menu else .25, -.07)
            if not menu and step in (5, 7):
                put(hits["snare"], t + .75 * BEAT / 2, .045, .2)
            pitch = root + bass_steps[(step + (bar % 4 == 3)) % 8]
            note(pitch, BEAT * .39, "bass", t, .15 if menu else .25)
            if not menu:
                # Palm-muted, distorted power dyads with syncopated rests.
                if step not in ((1, 5) if bar % 4 < 2 else (3,)):
                    note(pitch + 12, BEAT * .36, "drive", t, .105, -.58)
                    note(pitch + 19, BEAT * .34, "drive", t + .005, .072, .58)
        if bar % 2 == 0:
            put(hits["metal"], at + BEAT * 3.5, .05 if menu else .14, -.4)
        if bar % 4 == 3:
            for offset, gain in ((2.75, .08), (3.25, .12), (3.75, .15)):
                put(hits["tom"], at + offset * BEAT, gain * (.5 if menu else 1), .25)
        # Four-part pad gives menu its sustained musical identity.
        for n, interval in enumerate((12, 19, 24)):
            note(root + interval, 4.3 * BEAT, "pad", at, .045 if menu else .018, (n - 1) * .5)
        if menu or bar >= 4:
            motif = lead[(bar * 3) % len(lead):(bar * 3) % len(lead) + 3]
            if len(motif) < 3:
                motif += lead[:3 - len(motif)]
            for k, pitch in enumerate(motif):
                note(pitch, .65 * BEAT, "steel", at + (.5 + k * 1.25) * BEAT, .065 if menu else .09, -.15 + k * .15)
        if bar in (7, 15):
            note(71, BEAT * .7, "steel", at + 3 * BEAT, .09, .15)
    # Short, stereo, tempo-related delay; circular mixing preserves reverb tails.
    dry_left, dry_right = array("f", left), array("f", right)
    layer(left, dry_right, .75 * BEAT, .14, True)
    layer(right, dry_left, .5 * BEAT, .14, True)
    return left, right


def write_wav(name: str, channels: tuple[array, ...], description: str, loop: bool = False) -> dict:
    n = len(channels[0])
    assert all(len(c) == n for c in channels)
    peak = max(max(abs(x) for x in c) for c in channels)
    # Keep enough headroom for overlapping effects; no hard-clipped output.
    target = .67 if loop else .78
    scale = target / max(peak, .001)
    pcm = array("h")
    energy = 0.0
    first, last = [], []
    for i in range(n):
        for c in channels:
            value = c[i] * scale
            energy += value * value
            pcm.append(round(value * 32767))
    for c in channels:
        first.append(round(c[0] * scale * 32767))
        last.append(round(c[-1] * scale * 32767))
    if sys.byteorder != "little":
        pcm.byteswap()
    path = OUT / f"{name}.wav"
    with wave.open(str(path), "wb") as output:
        output.setnchannels(len(channels))
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(pcm.tobytes())
    return {"file": path.name, "description": description, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "bytes": path.stat().st_size, "frames": n, "rate": RATE, "channels": len(channels), "bits": 16,
            "seconds": round(n / RATE, 6), "peak": round(target, 6), "rms": round(math.sqrt(energy / (n * len(channels))), 6),
            "loop": loop, "loop_begin": 0, "loop_end_exclusive": n if loop else None,
            "loop_boundary_step_pcm": max(abs(a - b) for a, b in zip(first, last)) if loop else None}


def generate() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    entries = []
    for menu in (True, False):
        name = "iron_advance_menu" if menu else "iron_advance_battle"
        entries.append(write_wav(name, music(menu),
            "Iron Advance — original industrial instrumental, menu arrangement" if menu else
            "Iron Advance — original industrial instrumental, full battle arrangement", True))
    sounds = {
        "ui_click": (noise_effect(.065, "click", 7), "Short metallic switch click"),
        "select": (signal([(76, .5), (81, .32)], "steel", .075, .18), "Radio selection acknowledgement, non-verbal"),
        "order": (signal([(69, .45), (76, .42), (81, .3)], "steel", .065, .16), "Radio order acknowledgement, non-verbal"),
        "rifle": (noise_effect(.21, "rifle", 12), "Dry two-round rifle burst"),
        "sniper": (noise_effect(.62, "sniper", 13), "Sharp single rifle crack with resonant tail"),
        "tank": (noise_effect(.64, "tank", 14), "Cannon muzzle thump and pressure crack"),
        "apocalypse": (noise_effect(.92, "apocalypse", 15), "Heavy double cannon report"),
        "tesla": (noise_effect(.68, "tesla", 16), "Electrical arc with modulated crackle"),
        "prism": (noise_effect(.52, "prism", 17), "Descending coherent energy discharge"),
        "flak": (noise_effect(.30, "flak", 18), "Flak/autocannon percussive report"),
        "dog": (noise_effect(.23, "dog", 19), "Synthesized rough animal attack growl"),
        "impact": (noise_effect(.18, "impact", 20), "Metallic armor strike"),
        "flesh": (noise_effect(.13, "flesh", 21), "Soft infantry impact"),
        "explosion": (noise_effect(1.25, "explosion", 22), "Low rumbling vehicle/building explosion"),
        "construction": (noise_effect(.72, "machine", 23), "Mechanical construction ratchet"),
        "building_ready": (signal([(64, .35), (71, .4), (76, .45)], "steel", .13, .55), "Construction complete metallic chime"),
        "production_ready": (signal([(79, .4), (76, .4), (83, .45)], "bell", .10, .38), "New production ready three-note signal"),
        "chat": (signal([(88, .35), (93, .28)], "bell", .12, .22), "Bright incoming radio message alert"),
        "victory": (signal([(64, .35), (67, .4), (71, .45), (76, .5)], "steel", .23, .95), "Original rising E-minor result fanfare"),
        "defeat": (signal([(64, .45), (60, .4), (59, .4), (52, .5)], "pad", .23, 1.15), "Original falling result cue"),
    }
    for name, (clip, description) in sounds.items():
        entries.append(write_wav(name, (clip,), description))
    manifest = {"schema": 1, "title": "Iron Advance", "provenance": "Original composition and synthesized effects authored for this project; no external audio used.",
                "not_original_ra2_audio": True, "license": "CC0-1.0 dedication for the generated score and WAV recordings; to the extent rights exist, waived by the project contributor.",
                "generator": "Tools/prepare_audio.py", "generator_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                "seed": SCORE_SEED, "bpm": BPM, "bars": BARS, "time_signature": "4/4", "files": entries}
    (OUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"generated": len(entries), "bytes": sum(e["bytes"] for e in entries), "music_seconds_each": entries[0]["seconds"], "directory": str(OUT)}))


def validate() -> None:
    manifest = json.loads((OUT / "manifest.json").read_text(encoding="utf-8"))
    hashes = set()
    total = 0
    for entry in manifest["files"]:
        path = OUT / entry["file"]
        assert path.parent == OUT and path.suffix == ".wav"
        assert hashlib.sha256(path.read_bytes()).hexdigest() == entry["sha256"], path
        assert entry["sha256"] not in hashes, "duplicate audio"
        hashes.add(entry["sha256"])
        with wave.open(str(path), "rb") as source:
            assert source.getnchannels() == entry["channels"] and source.getsampwidth() == 2
            assert source.getframerate() == RATE and source.getnframes() == entry["frames"]
            pcm = array("h", source.readframes(source.getnframes()))
        if sys.byteorder != "little":
            pcm.byteswap()
        peak = max(abs(x) for x in pcm)
        rms = math.sqrt(sum(x * x for x in pcm) / len(pcm)) / 32767
        assert 2000 < peak < 32767 and rms > .015, (path, peak, rms)
        if entry["loop"]:
            assert entry["loop_end_exclusive"] == entry["frames"]
            # Circular delay/note tails are preserved and boundary discontinuity stays quiet.
            assert entry["loop_boundary_step_pcm"] < 3000
        total += path.stat().st_size
    assert len(manifest["files"]) == 22
    assert manifest["generator_sha256"] == hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    print(json.dumps({"valid": True, "distinct_wavs": len(hashes), "total_bytes": total, "checks": "PCM headers, lengths, hashes, signal level, no clipping, unique content, loop boundary, generator provenance"}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate", action="store_true", help="Validate existing output without regenerating")
    args = parser.parse_args()
    if not args.validate:
        generate()
    validate()
