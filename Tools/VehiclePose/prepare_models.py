#!/usr/bin/env python3
"""Generate retained, unprojected vehicle faces with the Python standard library.

Port of this repository's Tools/VoxelBaker/Formats.cs and Renderer.BuildFaces.
Only Tools/VehiclePose and Assets/Generated/VehiclePoses may be written. No C#,
Godot, native program, network access or third party module is required.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from functools import lru_cache
import gzip
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys
import time
from typing import BinaryIO, Iterator

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Assets/Generated/VehiclePoses"
MAGIC = b"RAVP1\0\0\0"
HEADER = struct.Struct("<8si3f")
PART_HEADER = struct.Struct("<Bi")
FACE = struct.Struct("<15fB")
FLOAT = struct.Struct("<f")
IDENTITY = (1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0)
ROLES = {"body": 0, "turret": 1, "barrel": 2}
MAX_INPUT = 128 * 1024 * 1024
MAX_CELLS = 32_000_000
MAX_FACES = 3_000_000
SOURCE_HASHES = {
    "Tools/VoxelBaker/Formats.cs": "af275f426fd04e33f043dec54cc6e4119c854e063ea4bab41b2b44234af2530f",
    "Tools/VoxelBaker/Renderer.cs": "0c16c42c083bd774bf434c984ebc4b11af5bb8119026e96e54309974c653f0d8",
}
# Exact side order, corner order and winding from Renderer.Sides.
SIDES = (
    ((-1, 0, 0), ((0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0))),
    ((1, 0, 0), ((1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1))),
    ((0, -1, 0), ((0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1))),
    ((0, 1, 0), ((0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0))),
    ((0, 0, -1), ((0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0))),
    ((0, 0, 1), ((0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1))),
)


class InvalidData(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise InvalidData(message)


def f32(value: float) -> float:
    """Round at arithmetic boundaries, matching System.Single coordinates."""
    try:
        result = FLOAT.unpack(FLOAT.pack(value))[0]
    except OverflowError as error:
        raise InvalidData("Float32 overflow") from error
    require(math.isfinite(result), "Non-finite Float32")
    return result


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def file_hash(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def load_bytes(path: Path) -> bytes:
    require(path.stat().st_size <= MAX_INPUT, f"Input exceeds 128 MiB: {path}")
    data = path.read_bytes()
    require(len(data) <= MAX_INPUT, f"Input exceeds 128 MiB: {path}")
    return data


class Bytes:
    def __init__(self, data: bytes):
        self.data = data

    def check(self, offset: int, count: int) -> None:
        require(0 <= offset <= len(self.data) and 0 <= count <= len(self.data) - offset,
                f"Range {offset}+{count} exceeds file length {len(self.data)}")

    def i32(self, offset: int) -> int:
        self.check(offset, 4)
        return struct.unpack_from("<i", self.data, offset)[0]

    def u32(self, offset: int) -> int:
        self.check(offset, 4)
        return struct.unpack_from("<I", self.data, offset)[0]

    def f32(self, offset: int) -> float:
        self.check(offset, 4)
        value = FLOAT.unpack_from(self.data, offset)[0]
        require(math.isfinite(value), "Non-finite input float")
        return value

    def u8(self, offset: int) -> int:
        self.check(offset, 1)
        return self.data[offset]

    def text(self, offset: int, count: int) -> str:
        self.check(offset, count)
        return self.data[offset:offset + count].split(b"\0", 1)[0].decode("ascii", "replace")


@dataclass(slots=True)
class Limb:
    name: str
    size: tuple[int, int, int]
    scale: float
    minimum: tuple[float, float, float]
    maximum: tuple[float, float, float]
    normal_type: int
    colors: bytearray
    occupied: bytearray
    count: int = 0

    @property
    def step(self) -> tuple[float, float, float]:
        return tuple(f32(f32(high - low) / size)
                     for low, high, size in zip(self.minimum, self.maximum, self.size))

    def metadata(self) -> dict:
        return {"name": self.name, "size": list(self.size), "scale": self.scale,
                "min": list(self.minimum), "max": list(self.maximum),
                "normalType": self.normal_type, "voxels": self.count}


def read_vxl(data: bytes) -> list[Limb]:
    """Checked original span decoder; normal-index bytes are consumed, not lit."""
    b = Bytes(data)
    b.check(0, 802)
    require(b.text(0, 16) == "Voxel Animation", "Invalid VXL signature")
    count, tails, body_size = b.u32(20), b.u32(24), b.u32(28)
    require(0 < count <= 256 and tails == count, "Invalid VXL limb counts")
    body = 802 + 28 * count
    footer = body + body_size
    b.check(802, 28 * count)
    b.check(body, body_size)
    b.check(footer, 92 * count)
    limbs = []
    total_cells = 0

    def body_range(offset: int, length: int) -> None:
        require(0 <= offset <= body_size and 0 <= length <= body_size - offset,
                "VXL limb offset exceeds body")

    for section in range(count):
        f = footer + 92 * section
        start_table, end_table, span_data = b.u32(f), b.u32(f + 4), b.u32(f + 8)
        sx, sy, sz = (b.u8(f + i) for i in (88, 89, 90))
        require(sx > 0 and sy > 0 and sz > 0, "Empty VXL dimensions")
        columns = sx * sy
        cells = columns * sz
        total_cells += cells
        require(total_cells <= MAX_CELLS, "VXL exceeds 32 million cell budget")
        body_range(start_table, columns * 4)
        body_range(end_table, columns * 4)
        body_range(span_data, 0)
        minimum = tuple(b.f32(f + i) for i in (64, 68, 72))
        maximum = tuple(b.f32(f + i) for i in (76, 80, 84))
        scale = b.f32(f + 12)
        require(0 < scale <= 1000 and all(high > low for low, high in zip(minimum, maximum)),
                "Invalid VXL bounds or scale")
        for t in range(12):
            b.f32(f + 16 + t * 4)  # Embedded transform validated, HVA supplies pose.
        limb = Limb(b.text(802 + section * 28, 16), (sx, sy, sz), scale,
                    minimum, maximum, b.u8(f + 91), bytearray(cells), bytearray(cells))
        for c in range(columns):
            start = b.i32(body + start_table + c * 4)
            end = b.i32(body + end_table + c * 4)
            if start == -1 and end == -1:
                continue
            require(start >= 0 and end >= start, "Invalid VXL column offsets")
            body_range(span_data + start, end - start + 1)
            p = body + span_data + start
            limit = body + span_data + end + 1
            z = 0
            while z < sz:
                require(p + 2 <= limit, "Truncated VXL span header")
                skip, run = b.u8(p), b.u8(p + 1)
                p += 2
                require(skip + run > 0 and z + skip + run <= sz and p + 2 * run + 1 <= limit,
                        "Invalid VXL span length")
                z += skip
                for _ in range(run):
                    index = z * columns + c
                    limb.colors[index] = b.u8(p)
                    b.u8(p + 1)  # Source normal index is not part of the face stream.
                    limb.occupied[index] = 1
                    limb.count += 1
                    p += 2
                    z += 1
                require(b.u8(p) == run, "VXL span duplicate count mismatch")
                p += 1
        limbs.append(limb)
    return limbs


def determinant(m: tuple[float, ...]) -> float:
    a = f32(f32(m[5] * m[10]) - f32(m[6] * m[9]))
    b = f32(f32(m[4] * m[10]) - f32(m[6] * m[8]))
    c = f32(f32(m[4] * m[9]) - f32(m[5] * m[8]))
    return f32(f32(f32(m[0] * a) - f32(m[1] * b)) + f32(m[2] * c))


def read_hva(data: bytes, sections: int) -> tuple[tuple[float, ...], ...]:
    """Validate every frame, retain frame zero, bind sections by position."""
    b = Bytes(data)
    b.check(0, 24)
    frames, limbs = b.u32(16), b.u32(20)
    require(0 < frames <= 4096 and 0 < limbs <= 256, "Invalid HVA counts")
    require(limbs == sections, "VXL/HVA limb counts differ")
    start = 24 + limbs * 16
    b.check(24, limbs * 16)
    b.check(start, frames * limbs * 48)
    frame_zero = []
    for i in range(frames * limbs):
        matrix = tuple(b.f32(start + i * 48 + j * 4) for j in range(12))
        require(abs(determinant(matrix)) >= 1e-8, "Singular HVA transform")
        if i < limbs:
            frame_zero.append(matrix)
    return tuple(frame_zero)


def exposed(limb: Limb) -> Iterator[tuple[int, int, int, int, int]]:
    """Same z/y/x and six-side traversal as BuildFaces; zero still occludes."""
    sx, sy, sz = limb.size
    plane = sx * sy
    occupied, colors = limb.occupied, limb.colors
    for z in range(sz):
        for y in range(sy):
            row = z * plane + y * sx
            for x in range(sx):
                i = row + x
                if not occupied[i] or colors[i] == 0:
                    continue
                neighbors = (x > 0 and occupied[i - 1], x + 1 < sx and occupied[i + 1],
                             y > 0 and occupied[i - sx], y + 1 < sy and occupied[i + sx],
                             z > 0 and occupied[i - plane], z + 1 < sz and occupied[i + plane])
                for side, neighbor in enumerate(neighbors):
                    if not neighbor:
                        yield x, y, z, side, colors[i]


def count_faces(limbs: list[Limb]) -> int:
    count = 0
    for limb in limbs:
        for _ in exposed(limb):
            count += 1
            require(count <= MAX_FACES, "Surface exceeds three million face budget")
    require(count > 0, "VXL has no nonzero visible colors")
    return count


def normal_vectors(m: tuple[float, ...]) -> tuple[tuple[float, float, float], ...]:
    """Normalize inverse-transpose columns of reflectY * HVA.linear."""
    a, b, c = m[0:3]
    d, e, f = (-m[4], -m[5], -m[6])
    g, h, i = m[8:11]

    def minor(v: float, w: float, x: float, y: float) -> float:
        return f32(f32(v * w) - f32(x * y))

    cofactor = ((minor(e, i, f, h), minor(f, g, d, i), minor(d, h, e, g)),
                (minor(c, h, b, i), minor(a, i, c, g), minor(b, g, a, h)),
                (minor(b, f, c, e), minor(c, d, a, f), minor(a, e, b, d)))
    det = f32(f32(f32(a * cofactor[0][0]) + f32(b * cofactor[0][1])) + f32(c * cofactor[0][2]))
    require(det != 0, "Singular normal transform")
    reciprocal = f32(1.0 / det)
    axes = []
    for axis in range(3):
        v = tuple(f32(cofactor[row][axis] * reciprocal) for row in range(3))
        length2 = f32(f32(f32(v[0] * v[0]) + f32(v[1] * v[1])) + f32(v[2] * v[2]))
        length = f32(math.sqrt(length2))
        require(length > 0, "Degenerate normal transform")
        axes.append(tuple(f32(value / length) for value in v))
    return tuple(tuple(f32(sign * value) for value in axes[axis])
                 for axis in range(3) for sign in (-1, 1))


def face_stream(limbs: list[Limb], pose: tuple[tuple[float, ...], ...] | None) -> Iterator[bytes]:
    """Yield one 61-byte face; retain at most 8192 transformed corners per limb."""
    if pose is not None:
        require(len(limbs) == len(pose), "VXL/HVA limb counts differ")
    emitted = 0
    for section, limb in enumerate(limbs):
        m = IDENTITY if pose is None else pose[section]
        step = limb.step
        scale = limb.scale
        coordinates = tuple(tuple(f32(low + f32(grid * delta)) for grid in range(size + 1))
                            for low, delta, size in zip(limb.minimum, step, limb.size))
        translation = tuple(f32(f32(m[k] * step[axis]) * scale)
                            for axis, k in enumerate((3, 7, 11)))
        normals = normal_vectors(m)

        @lru_cache(maxsize=8192)
        def transform(x: int, y: int, z: int) -> tuple[float, float, float]:
            p = (coordinates[0][x], coordinates[1][y], coordinates[2][z])
            result = []
            for axis in range(3):
                j = axis * 4
                value = f32(f32(f32(m[j] * p[0]) + f32(m[j + 1] * p[1])) + f32(m[j + 2] * p[2]))
                value = f32(value + translation[axis])
                result.append(f32((-value if axis == 1 else value) * scale))
            return tuple(result)

        for x, y, z, side, color in exposed(limb):
            values = []
            for cx, cy, cz in SIDES[side][1]:
                values.extend(transform(x + cx, y + cy, z + cz))
            values.extend(normals[side])
            emitted += 1
            require(emitted <= MAX_FACES, "Surface exceeds three million face budget")
            yield FACE.pack(*values, color)
        transform.cache_clear()
    require(emitted > 0, "VXL has no nonzero visible colors")


class ReferenceLookup:
    def __init__(self, root: Path):
        self.root = root
        self.index: dict[tuple[str, str], list[Path]] = {}
        self.records = []
        self.verified_references = 0
        self.used: set[tuple[str, str]] = set()
        for path in sorted((root / "Assets/Reference").rglob("*")):
            if not path.is_file() or path.suffix.lower() not in (".vxl", ".hva", ".pal"):
                continue
            digest = file_hash(path)
            key = (path.name.casefold(), digest)
            self.index.setdefault(key, []).append(path)
            self.records.append({"file": path.relative_to(root).as_posix(), "basename": path.name,
                                 "sha256": digest, "bytes": path.stat().st_size})

    def resolve(self, basename: str, expected: str) -> Path:
        require(isinstance(basename, str) and Path(basename).name == basename, "Expected source basename")
        require(isinstance(expected, str) and re.fullmatch(r"[0-9a-fA-F]{64}", expected) is not None,
                f"Missing/invalid SHA256 for {basename}")
        key = (basename.casefold(), expected.lower())
        require(key in self.index, f"No reference matches basename + SHA256: {basename} {expected}")
        self.verified_references += 1
        self.used.add(key)
        return self.index[key][0]

    def verified_bytes(self, basename: str, expected: str) -> bytes:
        path = self.resolve(basename, expected)
        data = load_bytes(path)
        require(sha256(data) == expected.lower(), f"Source changed after lookup: {path}")
        return data


def verify_port_sources(root: Path) -> dict:
    records = {}
    for relative, expected in SOURCE_HASHES.items():
        data = (root / relative).read_bytes()
        # Explicitly read our own source; stop if its decoder/face semantics drift.
        text = data.decode("utf-8")
        require("namespace VoxelBaker;" in text and sha256(data) == expected,
                f"Port source changed; review Python port before generating: {relative}")
        records[relative] = expected
    return records


def manifests(root: Path) -> list[tuple[Path, dict, str]]:
    paths = list((root / "Assets/Generated/Vehicles").glob("*.json"))
    paths.extend((root / "Assets/Generated/ClassicExpansion").rglob("*.json"))
    result = []
    names = set()
    for path in sorted(paths):
        raw = path.read_bytes()
        data = json.loads(raw)
        if not isinstance(data, dict) or data.get("schema") != "westwood-voxel-atlas/v1":
            continue
        name = data.get("name")
        require(isinstance(name, str) and re.fullmatch(r"[A-Za-z0-9_-]+", name) is not None,
                f"Unsafe model name in {path}")
        require(name.casefold() not in names, f"Duplicate manifest model name: {name}")
        names.add(name.casefold())
        require(data.get("animationFrame", 0) == 0, f"Only HVA frame zero is supported: {path}")
        parts = data.get("parts")
        require(isinstance(parts, list) and 0 < len(parts) <= 3, f"Invalid parts in {path}")
        require(len({part["name"] for part in parts}) == len(parts), f"Duplicate role in {path}")
        require(all(part["name"] in ROLES for part in parts), f"Unknown role in {path}")
        projection = data["projection"]
        values = [f32(projection[key]) for key in ("pixelsPerWorldUnit", "elevationDegrees", "azimuthDegrees")]
        require(values[0] > 0, f"Invalid projection scale: {path}")
        result.append((path, data, sha256(raw)))
    require(bool(result), "No voxel manifests found")
    return sorted(result, key=lambda item: item[1]["name"])


def palette_bytes(manifest: dict, references: ReferenceLookup) -> bytes:
    palette = manifest["palette"]
    expanded = bytes.fromhex(palette["expandedRgb"])
    require(len(expanded) == 768, "Expanded palette must contain 768 bytes")
    raw = references.verified_bytes(palette["source"], palette["sha256"])
    require(len(raw) == 768, "Source PAL must contain 768 bytes")
    bits = palette["inputChannelBits"]
    require(isinstance(bits, int) and 1 <= bits <= 8, "Invalid palette channel bits")
    maximum = (1 << bits) - 1
    require(all(value <= maximum for value in raw), "PAL channel exceeds bit range")
    expected = bytes((value * 255 + maximum // 2) // maximum for value in raw)
    require(expanded == expected, "expandedRgb does not match verified source PAL")
    return expanded


def pose_for(part: dict, limbs: list[Limb], references: ReferenceLookup):
    hva = part.get("hva")
    # Absence and explicit identityPose both select Pose.Identity. If an HVA is
    # still declared its provenance is checked in preflight, but it is not bound.
    if part.get("identityPose", False) or not hva:
        return None
    return read_hva(references.verified_bytes(hva, part["hvaSha256"]), len(limbs))


class HashedWriter:
    def __init__(self, stream: BinaryIO):
        self.stream = stream
        self.digest = hashlib.sha256()
        self.count = 0

    def write(self, data: bytes | bytearray) -> None:
        self.stream.write(data)
        self.digest.update(data)
        self.count += len(data)


def verify_model(path: Path, expected: dict, palette: bytes) -> None:
    """Read every record and gzip CRC, with a bounded decompression buffer."""
    digest = hashlib.sha256()
    byte_count = 0

    with gzip.open(path, "rb") as stream:
        def read_exact(count: int) -> bytes:
            nonlocal byte_count
            data = stream.read(count)
            require(len(data) == count, f"Truncated VPM: {path.name}")
            digest.update(data)
            byte_count += count
            return data

        magic, parts, scale, elevation, azimuth = HEADER.unpack(read_exact(HEADER.size))
        require(magic == MAGIC and parts == expected["parts"], "Invalid VPM header")
        require((scale, elevation, azimuth) == tuple(expected[key] for key in ("scale", "elevation", "azimuth")),
                "VPM projection mismatch")
        require(read_exact(768) == palette, "VPM palette mismatch")
        for part in expected["partDetails"]:
            role, faces = PART_HEADER.unpack(read_exact(PART_HEADER.size))
            require(role == part["role"] and faces == part["faces"], "VPM part header mismatch")
            remaining = faces
            while remaining:
                records = min(1024, remaining)
                for face in FACE.iter_unpack(read_exact(records * FACE.size)):
                    require(all(math.isfinite(value) for value in face[:15]), "Non-finite VPM face")
                    require(face[15] != 0, "Transparent face in VPM")
                    length2 = sum(value * value for value in face[12:15])
                    require(abs(length2 - 1) < 1e-5, "Non-unit VPM normal")
                remaining -= records
        require(stream.read(1) == b"", "Unexpected VPM trailing payload")
    require(byte_count == expected["rawBytes"] and digest.hexdigest() == expected["rawSha256"],
            "VPM raw length/hash mismatch")
    require(path.stat().st_size == expected["compressedBytes"] and file_hash(path) == expected["sha256"],
            "VPM compressed length/hash mismatch")


def write_model(path: Path, manifest: dict, manifest_path: Path, manifest_hash: str,
                references: ReferenceLookup, root: Path) -> dict:
    palette = palette_bytes(manifest, references)
    projection = manifest["projection"]
    values = [f32(projection[key]) for key in ("pixelsPerWorldUnit", "elevationDegrees", "azimuthDegrees")]
    result = {"file": path.name, "faces": 0, "parts": len(manifest["parts"]),
              "scale": values[0], "elevation": values[1], "azimuth": values[2],
              "animationFrame": 0, "manifest": manifest_path.relative_to(root).as_posix(),
              "manifestSha256": manifest_hash, "sourceHashes": {}, "partDetails": [],
              "teamColor": {"startIndex": manifest["teamColor"]["startIndex"],
                            "count": manifest["teamColor"]["count"]},
              "origin": "original VXL/HVA model origin retained"}
    result["sourceHashes"][manifest["palette"]["source"]] = manifest["palette"]["sha256"]
    with path.open("wb") as output, gzip.GzipFile(filename="", mode="wb", fileobj=output,
                                                compresslevel=9, mtime=0) as compressed:
        writer = HashedWriter(compressed)
        writer.write(HEADER.pack(MAGIC, result["parts"], *values))
        writer.write(palette)
        for part in manifest["parts"]:
            vxl_path = references.resolve(part["source"], part["sourceSha256"])
            limbs = read_vxl(references.verified_bytes(part["source"], part["sourceSha256"]))
            pose = pose_for(part, limbs, references)
            faces = count_faces(limbs)
            details = {"name": part["name"], "role": ROLES[part["name"]], "faces": faces,
                       "source": vxl_path.relative_to(root).as_posix(), "sourceSha256": part["sourceSha256"],
                       "identityPose": pose is None, "limbs": [limb.metadata() for limb in limbs]}
            result["sourceHashes"][part["source"]] = part["sourceSha256"]
            if part.get("hva"):
                hva_path = references.resolve(part["hva"], part["hvaSha256"])
                details.update({"hva": hva_path.relative_to(root).as_posix(), "hvaSha256": part["hvaSha256"]})
                result["sourceHashes"][part["hva"]] = part["hvaSha256"]
            writer.write(PART_HEADER.pack(details["role"], faces))
            buffer = bytearray()
            written = 0
            for face in face_stream(limbs, pose):
                buffer.extend(face)
                written += 1
                if len(buffer) >= 65536:
                    writer.write(buffer)
                    buffer.clear()
            if buffer:
                writer.write(buffer)
            require(written == faces, "Face counting/streaming mismatch")
            result["faces"] += faces
            result["partDetails"].append(details)
            del limbs, pose
        result["rawBytes"] = writer.count
        result["rawSha256"] = writer.digest.hexdigest()
    result["compressedBytes"] = path.stat().st_size
    result["sha256"] = file_hash(path)
    require(result["rawBytes"] == HEADER.size + 768 + PART_HEADER.size * result["parts"] + FACE.size * result["faces"],
            "Unexpected raw record size")
    verify_model(path, result, palette)
    return result


def generate(root: Path) -> dict:
    started = time.perf_counter()
    source_hashes = verify_port_sources(root)
    references = ReferenceLookup(root)
    discovered = manifests(root)
    selected = []
    skipped = []
    # Resolve hashes for ALL discovered voxel manifests, including turret-only
    # buildings. No geometry is written before the entire lookup is verified.
    references_before = references.verified_references
    for path, manifest, digest in discovered:
        palette_bytes(manifest, references)
        for part in manifest["parts"]:
            references.resolve(part["source"], part["sourceSha256"])
            if part.get("hva"):
                references.resolve(part["hva"], part["hvaSha256"])
            else:
                require(not part.get("hvaSha256"), "HVA hash supplied without HVA basename")
        if any(part["name"] == "body" for part in manifest["parts"]):
            selected.append((path, manifest, digest))
        else:
            skipped.append({"name": manifest["name"], "manifest": path.relative_to(root).as_posix(),
                            "reason": "no body part (building turret/barrel)"})
    preflight_references = references.verified_references - references_before
    print(f"Verified {len(discovered)} manifests, {preflight_references} source hash references; "
          f"indexed {len(references.records)} retained VXL/HVA/PAL files. "
          f"Generating {len(selected)} body-bearing models, skipping {len(skipped)} turret-only buildings.", flush=True)
    output = root / "Assets/Generated/VehiclePoses"
    output.mkdir(parents=True, exist_ok=True)
    models = {}
    for path, manifest, digest in selected:
        model_started = time.perf_counter()
        name = manifest["name"]
        result = write_model(output / f"{name}.vpm.gz", manifest, path, digest, references, root)
        models[name] = result
        print(f"{name}: {result['parts']} parts, {result['faces']:,} faces, "
              f"{result['compressedBytes']:,}/{result['rawBytes']:,} compressed/raw bytes, "
              f"{time.perf_counter() - model_started:.2f}s; verified", flush=True)
    totals = {"models": len(models), **{key: sum(model[key] for model in models.values())
                                       for key in ("parts", "faces", "compressedBytes", "rawBytes")}}
    elapsed = round(time.perf_counter() - started, 3)
    catalog = {"schema": 1, "format": "RAVP1", "models": models, "totals": totals,
               "generationSeconds": elapsed, "generator": "Tools/VehiclePose/prepare_models.py",
               "generatorSha256": file_hash(root / "Tools/VehiclePose/prepare_models.py"),
               "portedSources": source_hashes, "skipped": skipped,
               "verification": {"allManifestHashesMatched": True, "voxelManifests": len(discovered),
                                "manifestSourceReferences": preflight_references,
                                "uniqueReferencedBasenameHashes": len(references.used),
                                "indexedReferenceFiles": len(references.records),
                                "indexedVxlFiles": sum(Path(item["file"]).suffix.lower() == ".vxl" for item in references.records),
                                "indexedHvaFiles": sum(Path(item["file"]).suffix.lower() == ".hva" for item in references.records),
                                "allGeneratedModelsReadBack": True,
                                "checks": ["source basename+sha256", "source PAL expansion equals expandedRgb",
                                           "checked VXL spans", "all HVA frames validated; frame0 positional binding",
                                           "gzip CRC", "all VPM headers/records", "finite coordinates/unit normals",
                                           "61-byte face records", "compressed/raw lengths+sha256"]},
               "sourceFiles": references.records}
    (output / "catalog.json").write_text(json.dumps(catalog, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
    print(json.dumps({"totals": totals, "generationSeconds": elapsed, "verification": catalog["verification"]}, indent=2))
    return catalog


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true", help="Run in-memory parser/geometry checks only")
    arguments = parser.parse_args()
    if arguments.self_test:
        from test_prepare_models import run_tests
        return run_tests()
    generate(ROOT)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (InvalidData, OSError, KeyError, TypeError, json.JSONDecodeError) as error:
        print(f"Vehicle pose generation failed: {error}", file=sys.stderr)
        sys.exit(1)
