#!/usr/bin/env python3
"""Build a small independent pixel-art effects atlas, using only Python's standard library.

No source game pixels are used. Original DOG/ADOG attack frames are reused separately
by ArtLibrary.Effects.cs; their retained SHP provenance stays in manifest-extra.json.
Only writes Assets/Generated/Effects/atlas.png and manifest.json.
"""
from pathlib import Path
import hashlib
import json
import math
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'Assets/Generated/Effects'
CELL, COLUMNS = 64, 12
SEQUENCES = [('muzzle', 6), ('rifle-impact', 8), ('explosion', 12),
             ('smoke', 12), ('dust', 8), ('electric-impact', 8),
             ('bite', 6), ('sniper-flash', 6)]
WIDTH, HEIGHT = CELL * COLUMNS, CELL * len(SEQUENCES)


def noise(x, y, seed):
    n = ((x * 374761393) ^ (y * 668265263) ^ (seed * 1442695041)) & 0xffffffff
    n = ((n ^ (n >> 13)) * 1274126177) & 0xffffffff
    return ((n ^ (n >> 16)) & 255) / 255


def blend(pixels, x, y, color, alpha):
    if not (0 <= x < WIDTH and 0 <= y < HEIGHT):
        return
    i = (y * WIDTH + x) * 4
    a = min(255, max(0, int(alpha * 255)))
    if a == 0:
        return
    old = pixels[i + 3]
    weight = a * 255 + old * (255 - a)
    for c in range(3):
        pixels[i + c] = (color[c] * a * 255 + pixels[i + c] * old * (255 - a)) // weight
    pixels[i + 3] = min(255, (weight + 127) // 255)


def blob(pixels, ox, oy, cx, cy, radius, color, alpha, seed, stretch=1):
    for y in range(max(0, int(cy - radius * stretch - 2)), min(CELL, int(cy + radius * stretch + 3))):
        for x in range(max(0, int(cx - radius - 2)), min(CELL, int(cx + radius + 3))):
            d = math.hypot(x - cx, (y - cy) / stretch)
            edge = radius * (.92 + noise(x // 2, y // 2, seed) * .16)
            if d > edge:
                continue
            shade = .82 + .18 * noise(x // 2, y // 2, seed + 1)
            blend(pixels, ox + x, oy + y, tuple(int(c * shade) for c in color),
                  alpha * min(1, max(0, edge - d)))


def stroke(pixels, ox, oy, x1, y1, x2, y2, color, alpha, width=1):
    steps = max(1, int(max(abs(x2 - x1), abs(y2 - y1))))
    for step in range(steps + 1):
        t = step / steps
        x, y = round(x1 + (x2 - x1) * t), round(y1 + (y2 - y1) * t)
        for dy in range(width):
            for dx in range(width):
                blend(pixels, ox + x + dx, oy + y + dy, color, alpha)


def frame(pixels, row, index, count):
    ox, oy = index * CELL, row * CELL
    t = index / max(1, count - 1)
    fade = max(0, 1 - t)
    seed = row * 41 + 7
    if row in (0, 7):
        # Radial flash is rotationally neutral: the renderer adds the aimed short flare.
        radius = (12 if row == 0 else 8) * (1 - .65 * t)
        for arm in range(8):
            angle = arm * math.tau / 8
            length = radius * (1 if arm % 2 == 0 else .55)
            stroke(pixels, ox, oy, 32, 32, 32 + math.cos(angle) * length,
                   32 + math.sin(angle) * length, (255, 182, 58), fade, 2 if row == 0 else 1)
        blob(pixels, ox, oy, 32, 32, radius * .42, (255, 242, 182), fade, seed)
        blob(pixels, ox, oy, 32, 32, radius * .19, (255, 255, 237), fade, seed + 1)
    elif row == 1:
        for arm in range(5):
            angle = arm * 2.399 + .2
            r = 2 + 14 * t
            stroke(pixels, ox, oy, 32 + math.cos(angle) * r * .6,
                   35 + math.sin(angle) * r * .4, 32 + math.cos(angle) * r,
                   35 + math.sin(angle) * r * .65 - 6 * math.sin(t * math.pi),
                   (244, 197, 108), fade)
        blob(pixels, ox, oy, 32, 35, 2 + 5 * t, (172, 153, 114), fade * .65, seed, .6)
    elif row == 2:
        # Irregular hot core expands into brown-grey smoke; no smooth vector circles.
        r = 3 + 19 * min(1, t * 2)
        for part in range(7):
            angle = part * 2.399
            cx = 32 + math.cos(angle) * r * .43
            cy = 34 + math.sin(angle) * r * .33 - t * 13
            smoke = t > .44
            color = (101, 96, 83) if smoke else (171, 66, 20)
            blob(pixels, ox, oy, cx, cy, r * (.6 if smoke else .56), color,
                 fade * (.8 if smoke else 1), seed + part)
        if t < .67:
            blob(pixels, ox, oy, 31, 33 - t * 10, r * (1 - t) * .7,
                 (252, 139, 38), fade, seed + 11)
            blob(pixels, ox, oy, 30, 32 - t * 8, r * (1 - t) * .36,
                 (255, 235, 146), fade, seed + 12)
    elif row in (3, 4):
        smoke = row == 3
        color = (112, 111, 102) if smoke else (161, 146, 111)
        for part in range(5):
            angle = part * 2.399
            radius = (4 + t * 10) * (1 if smoke else .8)
            cx = 32 + math.cos(angle) * (3 + t * 5) + t * 3
            cy = 35 + math.sin(angle) * (2 + t * 3) - (t * 13 if smoke else 0)
            blob(pixels, ox, oy, cx, cy, radius, color, fade * (.30 if smoke else .4),
                 seed + part, 1 if smoke else .5)
    elif row == 5:
        for arm in range(7):
            angle = arm * 2.399
            r = 4 + 17 * t
            x, y = 32 + math.cos(angle) * r, 32 + math.sin(angle) * r
            stroke(pixels, ox, oy, 32, 32, (x + 32) / 2 + (arm % 3 - 1) * 3,
                   (y + 32) / 2, (91, 164, 245), fade, 2)
            stroke(pixels, ox, oy, (x + 32) / 2 + (arm % 3 - 1) * 3,
                   (y + 32) / 2, x, y, (211, 244, 255), fade)
        blob(pixels, ox, oy, 32, 32, 4 * fade + 1, (231, 252, 255), fade, seed)
    elif row == 6:
        # Short paired claw/bite marks and disturbed dust; no ranged dog tracer.
        for mark in range(3):
            x = 26 + mark * 5 + t * 3
            stroke(pixels, ox, oy, x, 26 + t * 8, x - 4, 37 + t * 5,
                   (211, 198, 165), fade, 1)
        blob(pixels, ox, oy, 32, 39, 3 + 7 * t, (143, 127, 95), fade * .35, seed, .5)


def png(pixels):
    def chunk(kind, data):
        return (struct.pack('>I', len(data)) + kind + data
                + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff))
    scanlines = b''.join(b'\0' + pixels[y * WIDTH * 4:(y + 1) * WIDTH * 4] for y in range(HEIGHT))
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', WIDTH, HEIGHT, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(scanlines, 9)) + chunk(b'IEND', b''))


def main():
    assert OUTPUT.resolve() == (ROOT / 'Assets/Generated/Effects').resolve()
    pixels = bytearray(WIDTH * HEIGHT * 4)
    for row, (_, count) in enumerate(SEQUENCES):
        for index in range(count):
            frame(pixels, row, index, count)
    image = png(pixels)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / 'atlas.png').write_bytes(image)
    manifest = {
        'schema': 1, 'origin': 'independent procedural pixel artwork; no original game pixels',
        'generator': 'Tools/prepare_effects.py', 'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'atlas': 'atlas.png', 'sha256': hashlib.sha256(image).hexdigest(),
        'width': WIDTH, 'height': HEIGHT, 'cellSize': CELL, 'columns': COLUMNS,
        'anchor': [32, 32], 'filter': 'nearest',
        'sequences': {name: {'row': row, 'frames': count} for row, (name, count) in enumerate(SEQUENCES)},
        'limitations': ['Independent approximation of period pixel effects; not recovered retail weapon animations.',
                        'Cosmetic travel never changes simulation damage or hashes.',
                        'Original DOG/ADOG attack sprites are reused from existing Sprites atlases, not this image.']}
    (OUTPUT / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print(json.dumps({'atlas': str(OUTPUT / 'atlas.png'), 'bytes': len(image),
                      'size': [WIDTH, HEIGHT], 'frames': sum(n for _, n in SEQUENCES),
                      'sha256': manifest['sha256']}))


if __name__ == '__main__':
    main()
