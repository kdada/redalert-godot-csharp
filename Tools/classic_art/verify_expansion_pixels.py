#!/usr/bin/env python3
"""Independent TS/RA2 SHP color-pixel verification; standard library only.

native_shp_frame(sourcePath, palettePath, index) returns (width, height, RGBA
bytes) on the complete original canvas. Palette channels use PAL << 2; index 0
has alpha 0 (its palette RGB is retained). No shadow-specific interpretation or team recoloring.
verify_manifest_frame(manifestPath, sourcePath, palettePath, index) returns a
JSON-compatible comparison dict, with passed=False on a pixel mismatch.
Malformed inputs raise ValueError, nonexistent frames raise IndexError, and
file errors propagate as OSError. Only normal color frames can be verified.

No arguments: check GI frame 0 and the 13 retained SHP-derived cameo PNGs.
For later conversions, use --manifest/--source/--palette [--frame N], or replace
--manifest with --png to compare a full-canvas color PNG. --report is optional.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[2]
MAX_INPUT_BYTES = 256 * 1024 * 1024
MAX_PIXELS = 16 * 1024 * 1024
_png_spec = importlib.util.spec_from_file_location(
    "_expansion_validation_png", ROOT / "Tools/validate_art.py"
)
_png_module = importlib.util.module_from_spec(_png_spec)
_png_spec.loader.exec_module(_png_module)
png = _png_module.png


def _check(condition, message):
    if not condition:
        raise ValueError(message)


def _read_source(path):
    path = Path(path)
    _check(8 <= path.stat().st_size <= MAX_INPUT_BYTES, "SHP size outside 8..256 MiB bounds")
    return path.read_bytes()


def _headers(data, index):
    signature, width, height, count = struct.unpack_from("<4H", data)
    _check(signature == 0, "Expected TS/RA2 SHP signature 0")
    _check(width > 0 and height > 0 and width * height <= MAX_PIXELS,
           "SHP canvas dimensions outside pixel budget")
    _check(count > 0 and 8 + count * 24 <= len(data), "Empty or truncated SHP header table")
    if not isinstance(index, int) or isinstance(index, bool):
        raise TypeError("Frame index must be an integer")
    if not 0 <= index < count:
        raise IndexError(f"Frame {index} outside 0..{count - 1}")
    table_end = 8 + count * 24
    selected = None
    offsets = {len(data)}
    for number in range(count):
        values = struct.unpack_from("<4HB11xI", data, 8 + number * 24)
        x, y, crop_width, crop_height, compression, offset = values
        if offset:
            _check(table_end <= offset < len(data), f"Frame {number}: data offset outside payload")
            offsets.add(offset)
        if number == index:
            _check(not offset or compression in (0, 1, 2, 3),
                   f"Frame {index}: unsupported format {compression} with nonempty payload")
            _check(crop_width * crop_height <= MAX_PIXELS, "SHP crop exceeds pixel budget")
            _check(not offset or (crop_width > 0 and crop_height > 0),
                   f"Frame {index}: data attached to empty crop")
            selected = values
    return width, height, selected, offsets


def _crop_indices(data, header, offsets):
    """Decode only the selected crop, bounded by the next distinct payload offset."""
    _, _, width, height, encoding, offset = header
    out = bytearray(width * height)
    if not offset:
        return out
    end = min(candidate for candidate in offsets if candidate > offset)
    payload = memoryview(data)[offset:end]
    if encoding in (0, 1):
        _check(len(payload) >= len(out), "Truncated uncompressed SHP pixels")
        out[:] = payload[:len(out)]
        return out
    if encoding == 2:
        _check(len(payload) >= 2, "Missing format-2 shared row length")
        row_bytes = struct.unpack_from("<H", payload)[0] - 2
        _check(0 <= row_bytes <= width, "Invalid format-2 row width")
        _check(2 + row_bytes * height <= len(payload), "Truncated format-2 rows")
        for row in range(height):
            start = 2 + row * row_bytes
            out[row * width:row * width + row_bytes] = payload[start:start + row_bytes]
        return out
    position = 0
    for row in range(height):
        _check(position + 2 <= len(payload), f"Row {row}: missing RLE row length")
        row_size = struct.unpack_from("<H", payload, position)[0]
        _check(row_size >= 2 and position + row_size <= len(payload),
               f"Row {row}: invalid or truncated RLE row")
        row_end = position + row_size
        position += 2
        decoded = 0
        while position < row_end:
            token = payload[position]
            position += 1
            if token:
                _check(decoded < width, f"Row {row}: literal exceeds crop width")
                out[row * width + decoded] = token
                decoded += 1
            else:
                _check(position < row_end, f"Row {row}: missing transparent-run length")
                skipped = payload[position]
                position += 1
                _check(skipped > 0, f"Row {row}: zero-length transparent run")
                destination = decoded + skipped
                # An inclusive final transparent skip may end exactly at width+1.
                _check(destination <= width or
                       (destination == width + 1 and position == row_end),
                       f"Row {row}: transparent run exceeds crop width")
                decoded = min(destination, width)
        # A short row leaves its transparent suffix at zero.
    return out


def _put_crop(canvas, canvas_width, canvas_height, crop, x, y, width, height):
    """Copy RGBA crop into native canvas, clipping pixels outside the canvas."""
    left, right = max(0, x), min(canvas_width, x + width)
    if left >= right:
        return
    for cy in range(max(0, y), min(canvas_height, y + height)):
        source = ((cy - y) * width + left - x) * 4
        target = (cy * canvas_width + left) * 4
        span = (right - left) * 4
        canvas[target:target + span] = crop[source:source + span]


def _native_from_bytes(data, palette, index):
    width, height, header, offsets = _headers(data, index)
    _check(len(palette) == 768 and max(palette) <= 63, "Expected a 768-byte six-bit PAL")
    indices = _crop_indices(data, header, offsets)
    colors = [bytes((palette[i * 3] << 2, palette[i * 3 + 1] << 2,
                     palette[i * 3 + 2] << 2, 0 if i == 0 else 255))
              for i in range(256)]
    crop = b"".join(colors[value] for value in indices)
    canvas = bytearray(width * height * 4)
    x, y, crop_width, crop_height, _, _ = header
    _put_crop(canvas, width, height, crop, x, y, crop_width, crop_height)
    return width, height, bytes(canvas)


def native_shp_frame(sourcePath, palettePath, index):
    """Return original full-canvas color RGBA, independently of conversion metadata."""
    return _native_from_bytes(_read_source(sourcePath), Path(palettePath).read_bytes(), index)


def _rgba_png(path, indexed_zero_transparent=False):
    """Use validate_art.png for integrity/filter decoding, then expand its samples."""
    path = Path(path)
    try:
        width, height, pixels = png(path, pixels=True)
    except (AssertionError, KeyError, struct.error) as error:
        raise ValueError(f"Unsupported or invalid PNG {path}: {error}") from error
    data = path.read_bytes()
    kind = data[25]
    if kind == 6:
        _check(len(pixels) == width * height * 4, f"Invalid RGBA pixel count: {path}")
        return width, height, pixels
    if kind == 2:
        return width, height, b"".join(pixels[i:i + 3] + b"\xff"
                                      for i in range(0, len(pixels), 3))
    _check(kind == 3, f"Expected RGB, RGBA or indexed-color PNG: {path}")
    palette, transparency = b"", b""
    cursor = 8
    while cursor < len(data):
        size = struct.unpack_from(">I", data, cursor)[0]
        tag, contents = data[cursor + 4:cursor + 8], data[cursor + 8:cursor + 8 + size]
        if tag == b"PLTE":
            palette = contents
        elif tag == b"tRNS":
            transparency = contents
        cursor += size + 12
    _check(palette and len(palette) % 3 == 0, f"Invalid indexed PNG palette: {path}")
    colors = []
    for index in range(len(palette) // 3):
        alpha = transparency[index] if index < len(transparency) else 255
        if indexed_zero_transparent and index == 0:
            colors.append(palette[:3] + b"\0")
        else:
            colors.append(palette[index * 3:index * 3 + 3] + bytes([alpha]))
    _check(not pixels or max(pixels) < len(colors), f"PNG palette index outside PLTE: {path}")
    return width, height, b"".join(colors[index] for index in pixels)


def _compare(native, actual, **description):
    width, height, expected = native
    actual_width, actual_height, pixels = actual
    _check((width, height) == (actual_width, actual_height), "Canvas dimensions differ")
    mismatches = sum(expected[i:i + 4] != pixels[i:i + 4] for i in range(0, len(expected), 4))
    first = next((i for i in range(0, len(expected), 4)
                  if expected[i:i + 4] != pixels[i:i + 4]), None)
    result = dict(description, passed=mismatches == 0, canvasWidth=width,
                  canvasHeight=height, mismatchPixels=mismatches,
                  nativeRgbaSha256=hashlib.sha256(expected).hexdigest(),
                  actualRgbaSha256=hashlib.sha256(pixels).hexdigest())
    if first is not None:
        result["firstMismatch"] = {"x": (first // 4) % width, "y": first // (width * 4),
                                   "native": list(expected[first:first + 4]),
                                   "actual": list(pixels[first:first + 4])}
    return result


def verify_manifest_frame(manifestPath, sourcePath, palettePath, index):
    """Compare a normal SHP atlas frame, reconstructed with sourceRect/imageRect."""
    manifest_path = Path(manifestPath)
    document = json.loads(manifest_path.read_text())
    _check(document.get("format") == "shp-ts-ra2", "Expected standard shp-ts-ra2 manifest")
    frames = [frame for frame in document["frames"] if frame["index"] == index]
    if len(frames) != 1:
        raise IndexError(f"Manifest has no unique frame {index}")
    frame = frames[0]
    _check(frame.get("role") != "shadow", "Only normal color frames are supported")
    if document.get("shadowLayout") == "second-half":
        _check(index < len(document["frames"]) // 2, "Shadow frames are not color evidence")
    source = _read_source(sourcePath)
    palette = Path(palettePath).read_bytes()
    _check(hashlib.sha256(source).hexdigest() == document["sourceSha256"], "SHP source SHA256 mismatch")
    _check(hashlib.sha256(palette).hexdigest() == document["palette"]["sha256"], "PAL SHA256 mismatch")
    native = _native_from_bytes(source, palette, index)
    width, height = document["canvasWidth"], document["canvasHeight"]
    _check(width > 0 and height > 0 and width * height <= MAX_PIXELS, "Manifest canvas outside pixel budget")
    canvas = bytearray(width * height * 4)
    rect, source_rect = frame["imageRect"], frame["sourceRect"]
    x, y, crop_width, crop_height = (rect[key] for key in ("x", "y", "width", "height"))
    sx, sy, source_width, source_height = (source_rect[key] for key in ("x", "y", "width", "height"))
    _check(min(crop_width, crop_height, source_width, source_height) >= 0, "Negative crop dimensions")
    if source_width and source_height:
        _check((crop_width, crop_height) == (source_width, source_height), "Packed/source crop dimensions differ")
        image = document["images"][frame["imageSet"]]
        image_path = manifest_path.parent / image["color"]
        aw, ah, atlas = _rgba_png(image_path)
        _check(0 <= x <= x + crop_width <= aw and 0 <= y <= y + crop_height <= ah,
               "Packed imageRect lies outside color atlas")
        crop = b"".join(atlas[((y + row) * aw + x) * 4:((y + row) * aw + x + crop_width) * 4]
                        for row in range(crop_height))
        _put_crop(canvas, width, height, crop, sx, sy, source_width, source_height)
    return _compare(native, (width, height, bytes(canvas)), kind="manifest-frame",
                    manifest=str(manifest_path), source=str(sourcePath), frameIndex=index,
                    compression=_headers(source, index)[2][4])


def verify_png_frame(pngPath, sourcePath, palettePath, index=0):
    """Compare full-canvas PNG pixels; legacy indexed PNG index 0 is SHP-transparent."""
    return _compare(native_shp_frame(sourcePath, palettePath, index),
                    _rgba_png(pngPath, indexed_zero_transparent=True),
                    kind="full-canvas-png", png=str(pngPath), source=str(sourcePath), frameIndex=index,
                    indexedPngPolicy="Index 0 alpha normalized to 0; palette RGB retained; RGBA PNGs unmodified")


def _decoder_self_checks():
    """Small in-memory protocol cases cover modes and RLE boundary behavior."""
    palette = bytes(channel for index in range(256) for channel in (index % 64, 0, 0))
    def fixture(mode, payload=b"", width=3, height=2, offset=32):
        return (struct.pack("<4H", 0, 5, 3, 1) +
                struct.pack("<4HB11xI", 1, 1, width, height, mode, offset) + payload)
    def expected(indices, width=3, height=2):
        rgba = bytearray(5 * 3 * 4)
        for row in range(height):
            for column in range(width):
                value = indices[row * width + column]
                p = ((row + 1) * 5 + column + 1) * 4
                rgba[p:p + 4] = bytes((value * 4, 0, 0, 255 if value else 0))
        return 5, 3, bytes(rgba)
    cases = [
        ("raw-0", fixture(0, bytes([1, 0, 2, 3, 4, 5])), expected([1, 0, 2, 3, 4, 5])),
        ("raw-1", fixture(1, bytes([1, 0, 2, 3, 4, 5])), expected([1, 0, 2, 3, 4, 5])),
        ("shared-row-length-2", fixture(2, struct.pack("<H", 4) + bytes([1, 2, 3, 4])),
         expected([1, 2, 0, 3, 4, 0])),
        ("rle-3", fixture(3, struct.pack("<H", 6) + bytes([1, 0, 1, 2]) +
                          struct.pack("<H", 5) + bytes([3, 4, 5])), expected([1, 0, 2, 3, 4, 5])),
        ("short-row-and-inclusive-terminal-skip", fixture(3, struct.pack("<H", 3) + b"\x01" +
                          struct.pack("<H", 5) + bytes([3, 0, 3])), expected([1, 0, 0, 3, 0, 0])),
        ("zero-data-offset", fixture(3, offset=0), expected([0] * 6)),
        ("empty-frame-header", fixture(0, width=0, height=0, offset=0), expected([], 0, 0)),
        ("empty-frame-unused-codec-4", fixture(4, width=0, height=0, offset=0), expected([], 0, 0)),
        ("zero-offset-unused-codec-4", fixture(4, offset=0), expected([0] * 6)),

    ]
    names = []
    for name, source, answer in cases:
        _check(_native_from_bytes(source, palette, 0) == answer, "Decoder self-check failed: " + name)
        names.append(name)
    invalid = {
        "dangling-zero-run": fixture(3, struct.pack("<H", 3) + b"\0"),
        "zero-length-run": fixture(3, struct.pack("<H", 4) + bytes([0, 0])),
        "excessive-terminal-skip": fixture(3, struct.pack("<H", 4) + bytes([0, 5])),
        "nonterminal-overshoot": fixture(3, struct.pack("<H", 5) + bytes([0, 4, 1])),
        "truncated-raw": fixture(0, b"\x01"),
        "invalid-shared-row-width": fixture(2, struct.pack("<H", 6) + bytes(8)),
    }
    for name, source in invalid.items():
        try:
            _native_from_bytes(source, palette, 0)
        except ValueError:
            names.append("reject-" + name)
        else:
            raise ValueError("Decoder accepted malformed self-check: " + name)
    return names


def _baseline():
    reference = ROOT / "Assets/Reference"
    rows = [verify_manifest_frame(ROOT / "Assets/Generated/Sprites/gi/manifest.json",
                                  reference / "Battlefield/Infantry/gi.shp",
                                  reference / "Formats/unittem.pal", 0)]
    provenance = json.loads((reference / "manifest.json").read_text())
    for entry in provenance["assets"]:
        if entry.get("path", "").startswith("Cameos/") and entry.get("derived_from"):
            rows.append(verify_png_frame(reference / entry["path"], reference / entry["derived_from"],
                                         reference / entry["palette"], 0))
    _check(len(rows) == 14, "Expected GI plus 13 retained SHP-derived cameos")
    return {"schema": "classic-expansion-native-pixel-evidence/v1",
            "passed": all(row["passed"] for row in rows), "checks": len(rows),
            "normalColorOnly": True, "paletteExpansion": "PAL << 2; index 0 alpha 0 with palette RGB retained",
            "decoderProtocolChecks": _decoder_self_checks(), "results": rows}


def conversion_snapshot(previous=None):
    """Verify available expansion outputs; retain prior passing checks on continuation."""
    reference = ROOT / "Assets/Reference/ClassicExpansion"
    generated = ROOT / "Assets/Generated/ClassicExpansion"
    plan = json.loads((reference / "plan.json").read_text())
    inventory = json.loads((reference / "reuse-inventory.json").read_text())
    palette = ROOT / plan["assets"]["unittem.pal"]["path"]
    cameo_palette = ROOT / plan["assets"]["cameo.pal"]["path"]
    palette_hash = hashlib.sha256(palette.read_bytes()).hexdigest()
    cameo_palette_hash = hashlib.sha256(cameo_palette.read_bytes()).hexdigest()
    old_by_hash = {}
    for item in inventory["sprites"]:
        if item.get("manifestFormat") == "shp-ts-ra2" and item.get("shadowLayout") == "second-half":
            old_by_hash.setdefault((item["sourceSha256"], item["paletteSha256"]), []).append(item)
    groups = {}
    for group in plan["groups"]:
        if group.get("asset") and not group.get("voxel"):
            groups[group["asset"]] = group
    prior = {item["checkKey"]: item for item in (previous or {}).get("results", [])
             if item.get("passed") and item.get("checkKey")}
    results, pending_sprites, pending_cameos = [], [], []
    reused_checks = expected_sprites = expected_cameos = 0
    for filename, group in sorted(groups.items()):
        asset = plan["assets"][filename]
        source = ROOT / asset["path"]
        if group["role"] == "cameo":
            expected_cameos += 1
            icon = generated / "Cameos" / (Path(filename).stem + ".png")
            key = "cameo/" + filename + "/" + asset["sha256"] + "/" + cameo_palette_hash
            if key in prior:
                results.append(prior[key]); reused_checks += 1
                continue
            if not icon.is_file():
                pending_cameos.append({"sourceFile": filename, "png": str(icon.relative_to(ROOT))})
                continue
            try:
                _check(icon.read_bytes()[25] == 6, "New cameo must be RGBA, not a legacy indexed PNG")
                row = verify_png_frame(icon, source, cameo_palette, 0)
            except (OSError, ValueError, IndexError, KeyError) as error:
                row = {"kind": "full-canvas-png", "passed": False, "error": str(error), "png": str(icon)}
            row.update(checkKey=key, sourceFile=filename, sourceSha256=asset["sha256"],
                       paletteSha256=cameo_palette_hash)
            results.append(row)
            continue
        expected_sprites += 1
        new_manifest = generated / "Sprites" / Path(filename).stem / "manifest.json"
        candidates = [ROOT / item["manifest"] for item in old_by_hash.get((asset["sha256"], palette_hash), [])]
        candidates.append(new_manifest)
        manifest = next((path for path in candidates if path.is_file()), None)
        if manifest is None:
            pending_sprites.append({"sourceFile": filename, "manifest": str(new_manifest.relative_to(ROOT))})
            continue
        try:
            document = json.loads(manifest.read_text())
        except (OSError, json.JSONDecodeError):
            pending_sprites.append({"sourceFile": filename, "manifest": str(manifest.relative_to(ROOT)),
                                    "reason": "Manifest not completely written at snapshot"})
            continue
        data = _read_source(source)
        count = struct.unpack_from("<H", data, 6)[0]
        _check(count >= 2 and count % 2 == 0, "Expected an even color/shadow SHP frame count")
        color_count = count // 2
        first_nonempty = 0
        all_color_frames_empty = True
        for index in range(color_count):
            _, _, header, offsets = _headers(data, index)
            if any(_crop_indices(data, header, offsets)):
                first_nonempty = index
                all_color_frames_empty = False
                break
        indices = {first_nonempty}
        if "yard" in Path(filename).stem:
            indices.update((0, color_count - 1))
        for index in sorted(indices):
            key = "sprite/" + filename + "/" + str(index) + "/" + asset["sha256"] + "/" + palette_hash
            if key in prior:
                results.append(prior[key]); reused_checks += 1
                continue
            try:
                _check(document.get("shadowLayout") == "second-half", "Expected second-half shadow layout")
                row = verify_manifest_frame(manifest, source, palette, index)
            except (OSError, ValueError, IndexError, KeyError) as error:
                row = {"kind": "manifest-frame", "passed": False, "error": str(error),
                       "manifest": str(manifest), "frameIndex": index}
            row.update(checkKey=key, sourceFile=filename, sourceSha256=asset["sha256"],
                       paletteSha256=palette_hash, reusedManifest=manifest != new_manifest,
                       sourceAllColorFramesEmpty=all_color_frames_empty,
                       sampleReason=("valid-blank-color-frame" if all_color_frames_empty else
                                     "first-nonempty-color" if index == first_nonempty else "yard-boundary-color"))
            results.append(row)
    failures = [row for row in results if not row["passed"]]
    sprite_rows = [row for row in results if row["kind"] == "manifest-frame"]
    cameo_rows = [row for row in results if row["kind"] == "full-canvas-png"]
    return {"schema": "classic-expansion-conversion-pixels/v1", "passed": not failures,
            "complete": not (pending_sprites or pending_cameos),
            "scope": "One original first-nonempty color frame per expected SHP, yard first/last color frames, every new cameo. Voxels excluded.",
            "expectedSpriteSources": expected_sprites, "expectedCameos": expected_cameos,
            "checkedSpriteSources": len({row["sourceFile"] for row in sprite_rows}),
            "checkedSpriteFrames": len(sprite_rows), "checkedCameos": len(cameo_rows),
            "previousPassingChecksRetained": reused_checks, "failureCount": len(failures),
            "pendingSprites": pending_sprites, "pendingCameos": pending_cameos,
            "failures": failures, "results": results}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    output = parser.add_mutually_exclusive_group()
    output.add_argument("--manifest", type=Path)
    output.add_argument("--png", type=Path)
    output.add_argument("--conversion-snapshot", action="store_true",
                        help="Verify available expansion outputs; retain passing checks from --report")
    parser.add_argument("--source", type=Path)
    parser.add_argument("--palette", type=Path)
    parser.add_argument("--frame", type=int, default=0)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args(argv)
    if args.conversion_snapshot:
        if args.source or args.palette:
            parser.error("--conversion-snapshot reads sources and palettes from the expansion plan")
        previous = json.loads(args.report.read_text()) if args.report and args.report.is_file() else None
        report = conversion_snapshot(previous)
    elif args.manifest or args.png:
        if not (args.source and args.palette):
            parser.error("--source and --palette are required with --manifest or --png")
        verifier = verify_manifest_frame if args.manifest else verify_png_frame
        report = verifier(args.manifest or args.png, args.source, args.palette, args.frame)
    else:
        if args.source or args.palette:
            parser.error("--source and --palette require --manifest or --png")
        report = _baseline()
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
