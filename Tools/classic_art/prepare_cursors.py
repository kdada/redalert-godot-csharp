#!/usr/bin/env python3
"""Extract original RA2 cursor data by bounded HTTP Range; never game code.

Inputs are the original MIX/SHP/PAL bytes. Source frames retain palette indices,
canvas, crop and alpha. This tool does not build or launch the game.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import zlib

ROOT = Path(__file__).resolve().parents[2]
REFERENCE = ROOT / 'Assets/Reference/Cursors'
OUTPUT = ROOT / 'Assets/Generated/Cursors'


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def save_original(path, data):
    if path.exists():
        if path.read_bytes() != data:
            raise RuntimeError(f'Refusing to overwrite changed original: {path}')
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


def fetch():
    reader = load('original_cursor_mix_reader', ROOT / 'Assets/Reference/Tools/fetch_ra2_samples.py')
    reader.ROOT = REFERENCE
    reader.MAX_BYTES = 3_000_000
    def exact_range(archive, offset, length):
        result = bytearray()
        while length:
            if archive == 'ra2.mix':
                part, position = divmod(offset, 70_000_000)
                filename = f'ra2.mix.part{part:02d}'
                count = min(length, 70_000_000 - position)
            else:
                filename, position, count = archive, offset, length
            if reader.transferred + count > reader.MAX_BYTES:
                raise RuntimeError('cursor download exceeded 3 MB budget')
            reader.transferred += count
            url = reader.BASE + filename + f'?range={position}-{count}'
            command = ['curl', '--fail', '--silent', '--show-error', '--proxy', 'http://127.0.0.1:10808',
                '--max-time', '25', '--retry', '4', '--retry-all-errors', '--retry-delay', '0',
                '--max-filesize', str(count), '--range', f'{position}-{position + count - 1}', '--write-out', '%{http_code}', url]
            raw = subprocess.run(command, check=True, capture_output=True).stdout
            if raw[-3:] != b'206' or len(raw) != count + 3:
                raise RuntimeError(f'Exact HTTP range not honored: {filename} {position}+{count}')
            result.extend(raw[:-3]); offset += count; length -= count
        return bytes(result)
    reader.fetch = exact_range
    root = reader.mix_index('ra2.mix')
    names = ['mouse.sha', 'mouse.shp', 'mousepal.pal', 'mouse.ini', 'cursor.ini', 'mousecur.ini']
    found = {}
    for nested in ('cache.mix', 'local.mix', 'conquer.mix', 'generic.mix'):
        location = root.get(reader.filename_hash(nested))
        if not location:
            continue
        index = reader.mix_index('ra2.mix', location[0])
        available = [name for name in names if reader.filename_hash(name) in index]
        found[nested] = available
        print(json.dumps({'archive': nested, 'found': available}), flush=True)
        for name in available:
            reader.extract('ra2.mix', nested, index, name, 'Original')
    path = REFERENCE / 'sources.json'
    old = json.loads(path.read_text()) if path.exists() else {}
    assets = {item['path']: item for item in old.get('assets', [])}
    assets.update({item['path']: item for item in reader.records})
    REFERENCE.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({'schema': 1, 'sourceRevision': reader.REV, 'referenceRoot': 'Assets/Reference/Cursors',
        'rangeBytesThisRun': reader.transferred, 'found': found, 'assets': list(assets.values())}, indent=2) + '\n')
    print(json.dumps({'rangeBytes': reader.transferred, 'assets': len(assets)}, indent=2))


def png_write(path, width, height, pixels):
    def chunk(kind, value):
        return struct.pack('>I', len(value)) + kind + value + struct.pack('>I', zlib.crc32(kind + value) & 0xffffffff)
    raw = b''.join(b'\0' + pixels[y * width * 4:(y + 1) * width * 4] for y in range(height))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
        + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def decode():
    source = REFERENCE / 'Original/mouse.shp'
    data = source.read_bytes(); palette = (REFERENCE / 'Original/mousepal.pal').read_bytes()
    magic, cw, ch, count = struct.unpack_from('<4H', data)
    assert magic == 0 and (cw, ch, count) == (55, 43, 450) and len(palette) == 768
    assert len(data) >= 8 + 24 * count
    result = []
    for i in range(count):
        pos = 8 + i * 24
        x, y, width, height = struct.unpack_from('<4H', data, pos)
        compression = data[pos + 8]; offset = struct.unpack_from('<I', data, pos + 20)[0]
        assert x + width <= cw and y + height <= ch
        indices = bytes(width * height)
        if offset:
            # Vanilla mouse.shp uses native uncompressed formats 0/1 (the
            # thin SellUnit frame 144 is format 0). Zeros are palette indices.
            assert compression in (0, 1) and offset >= 8 + 24 * count and offset + width * height <= len(data)
            indices = data[offset:offset + width * height]
        rgba = bytearray(cw * ch * 4)
        for row in range(height):
            for col in range(width):
                value = indices[row * width + col]
                if not value:
                    continue
                pixel = ((y + row) * cw + x + col) * 4
                rgba[pixel:pixel + 4] = bytes((palette[value * 3] << 2, palette[value * 3 + 1] << 2, palette[value * 3 + 2] << 2, 255))
        result.append(({'index': i, 'sourceRect': {'x': x, 'y': y, 'width': width, 'height': height},
            'compression': compression, 'dataOffset': offset, 'hasData': bool(offset),
            'indicesSha256': hashlib.sha256(indices).hexdigest()}, rgba, indices))
    return cw, ch, result


def cursor_sequences(frames, cw, ch):
    # DATA transcription: the documented YR table's selected prefix is present
    # and visually checked in the original 450-frame vanilla RA2 mouse.shp.
    definitions = {
        'arrow': (0, 1, 0, 'Left', 'Top', 'Default'),
        'attack': (53, 5, 4, 'Center', 'Middle', 'Attack'),
        'attack_out_of_range': (58, 5, 4, 'Center', 'Middle', 'AttackOutOfRange'),
        'deploy': (110, 9, 4, 'Center', 'Middle', 'Deploy'),
        'deploy_invalid': (119, 1, 0, 'Center', 'Middle', 'NoDeploy'),
        'repair': (150, 20, 4, 'Center', 'Middle', 'Repair'),
        'repair_invalid': (190, 1, 0, 'Center', 'Middle', 'NoRepair'),
        'sell': (129, 10, 4, 'Center', 'Middle', 'Sell'),
        'sell_invalid': (149, 1, 0, 'Center', 'Middle', 'NoSell'),
    }
    directions = [('n', 'N', 'Center', 'Top'), ('ne', 'NE', 'Right', 'Top'),
        ('e', 'E', 'Right', 'Middle'), ('se', 'SE', 'Right', 'Bottom'),
        ('s', 'S', 'Center', 'Bottom'), ('sw', 'SW', 'Left', 'Bottom'),
        ('w', 'W', 'Left', 'Middle'), ('nw', 'NW', 'Left', 'Top')]
    for offset, (key, native, hx, hy) in enumerate(directions):
        definitions['scroll_' + key] = (2 + offset, 1, 0, hx, hy, 'Move' + native)
        definitions['scroll_' + key + '_blocked'] = (10 + offset, 1, 0, hx, hy, 'NoMove' + native)
    cursors = {}
    for name, (start, count, interval, hx, hy, native_name) in definitions.items():
        assert count > 0 and start >= 0 and start + count <= len(frames)
        assert all(frames[i]['hasData'] for i in range(start, start + count)), name
        cursors[name] = {'nativeName': native_name, 'frames': list(range(start, start + count)),
            'hotspot': {'x': {'Left': 0, 'Center': cw // 2, 'Right': cw - 1}[hx],
                'y': {'Top': 0, 'Middle': ch // 2, 'Bottom': ch - 1}[hy]},
            'hotspotAlignment': {'x': hx, 'y': hy}, 'intervalTicks': interval,
            'nominalClockHz': 60, 'frameDurationSeconds': interval / 60 if interval else 0,
            'loop': count > 1, 'preview': frames[start]['png']}
    return cursors


def catalog_only():
    path = OUTPUT / 'catalog.json'
    catalog = json.loads(path.read_text())
    cw, ch, decoded = decode()
    assert catalog['sourceFrameCount'] == len(decoded) == 450
    assert catalog['source']['spriteSha256'] == hashlib.sha256((REFERENCE / 'Original/mouse.shp').read_bytes()).hexdigest()
    assert catalog['source']['paletteSha256'] == hashlib.sha256((REFERENCE / 'Original/mousepal.pal').read_bytes()).hexdigest()
    for frame, (source, _, _) in zip(catalog['frames'], decoded):
        assert frame['index'] == source['index'] and frame['indicesSha256'] == source['indicesSha256']
        assert frame['sourceRect'] == source['sourceRect'] and frame['hasData'] == source['hasData']
    catalog['cursors'] = cursor_sequences(catalog['frames'], cw, ch)
    catalog['documentation']['hotspots'] = 'Documented full-canvas alignment: Left/Top=0, Center/Middle=floor(size/2), Right/Bottom=size-1. SHP/PAL do not encode click hotspots.'
    path.write_text(json.dumps(catalog, indent=2) + '\n')
    print(json.dumps({'sourceFrames': len(decoded), 'sequences': len(catalog['cursors']), 'onlyCatalogUpdated': True}))


def prepare():
    cw, ch, decoded = decode(); columns = 16; padding = 2
    cell_w, cell_h = cw + padding * 2, ch + padding * 2
    aw, ah = columns * cell_w, math.ceil(len(decoded) / columns) * cell_h
    atlas = bytearray(aw * ah * 4); frames = []
    for i, (frame, rgba, indices) in enumerate(decoded):
        ax = i % columns * cell_w + padding; ay = i // columns * cell_h + padding
        for row in range(ch):
            atlas[((ay + row) * aw + ax) * 4:((ay + row) * aw + ax + cw) * 4] = rgba[row * cw * 4:(row + 1) * cw * 4]
        frame['region'] = {'x': ax, 'y': ay, 'width': cw, 'height': ch}
        frame['png'] = f'Frames/{i:03d}.png'
        png_write(OUTPUT / frame['png'], cw, ch, rgba)
        indices_path = OUTPUT / 'Indices' / f'{i:03d}.bin'; indices_path.parent.mkdir(parents=True, exist_ok=True)
        indices_path.write_bytes(indices)
        frames.append(frame)
    png_write(OUTPUT / 'atlas.png', aw, ah, atlas)
    # Documentation is a transcription of implicit native defaults, not engine
    # implementation. Its YR version is disclosed; selected prefix frames are
    # independently checked against the original vanilla RA2 raster.
    defaults_url = 'http://ares-developers.github.io/Ares-docs/_downloads/MouseCursors.txt'
    cursors = cursor_sequences(frames, cw, ch)
    catalog = {'schema': 1, 'game': 'Red Alert 2 vanilla', 'sourceFrameCount': len(frames),
        'source': {'sprite': 'Assets/Reference/Cursors/Original/mouse.shp',
            'spriteSha256': hashlib.sha256((REFERENCE / 'Original/mouse.shp').read_bytes()).hexdigest(),
            'palette': 'Assets/Reference/Cursors/Original/mousepal.pal',
            'paletteSha256': hashlib.sha256((REFERENCE / 'Original/mousepal.pal').read_bytes()).hexdigest()},
        'atlas': 'atlas.png', 'atlasWidth': aw, 'atlasHeight': ah, 'canvasWidth': cw, 'canvasHeight': ch,
        'frameLayout': 'full-native-canvas', 'frames': frames, 'cursors': cursors,
        'aliases': {'default': 'arrow', 'repair_valid': 'repair', 'sell_valid': 'sell', 'no_repair': 'repair_invalid', 'no_sell': 'sell_invalid'},
        'drawing': 'Each region already includes sourceRect placement within the original 55x43 canvas. Draw at pointerPosition-hotspot*scale. Do not apply sourceRect a second time. Use nearest sampling.',
        'documentation': {'defaults': defaults_url, 'schema': 'https://ares-developers.github.io/Ares-docs/new/mousecursors.html',
            'naming': 'https://modenc.renegadeprojects.com/Cursor',
            'limitation': 'Default table documentation targets YR; selected ranges are checked against original RA2 pixels. Source SHP/PAL contain no action names, animation clock or click-hotspot table.',
            'timing': 'Interval 4 is documented. Nominal 60 Hz conversion (4/60 seconds) is an explicit inferred render-clock choice, not a timing value encoded in the original SHP.',
            'hotspots': 'Documented full-canvas alignment: Left/Top=0, Center/Middle=floor(size/2), Right/Bottom=size-1. SHP/PAL do not encode click hotspots.'},
        'pixelPolicy': 'Original palette indices, 6-bit PAL<<2 colors, canvas, frame crop and transparency retained. No redrawn pointer, synthesized invalid sign, recoloring or resampling.'}
    (OUTPUT / 'catalog.json').write_text(json.dumps(catalog, indent=2) + '\n')
    print(json.dumps({'frames': len(frames), 'atlas': [aw, ah], 'cursors': cursors}, indent=2))


def validate():
    png = load('cursor_png_validator', ROOT / 'Tools/validate_art.py').png
    catalog = json.loads((OUTPUT / 'catalog.json').read_text());cw, ch, decoded = decode()
    aw, ah, pixels = png(OUTPUT / catalog['atlas'], True);checked = 0
    assert (aw, ah) == (catalog['atlasWidth'], catalog['atlasHeight'])
    for i, (_, expected, indices) in enumerate(decoded):
        frame = catalog['frames'][i];region = frame['region'];actual = bytearray()
        for row in range(ch):
            a = ((region['y'] + row) * aw + region['x']) * 4
            actual.extend(pixels[a:a + cw * 4])
        assert actual == expected, f'cursor RGBA mismatch: {i}'
        assert (OUTPUT / 'Indices' / f'{i:03d}.bin').read_bytes() == indices
        checked += 1
    expected_sequences = cursor_sequences(catalog['frames'], cw, ch)
    assert catalog['cursors'] == expected_sequences, 'cursor sequence range/alignment/interval mismatch'
    for name, item in catalog['cursors'].items():
        assert all(catalog['frames'][i]['hasData'] for i in item['frames']), name
    result = {'pass': True, 'framesChecked': checked, 'allNativeCanvasPixelsMatch': True,
        'allPaletteIndicesMatch': True, 'selectedRangesExist': True,
        'sequenceCount': len(expected_sequences),
        'sequenceRanges': {name: {'first': item['frames'][0], 'last': item['frames'][-1], 'hotspot': item['hotspot']} for name, item in expected_sequences.items()},
        'nativeGameLaunched': False, 'hotspotAndTimingLimitations': catalog['documentation']['limitation'],
        'visualInspection': 'Source atlas and selected original direction/deploy previews inspected separately with read_image; native runtime captures remain a separate check.'}
    (OUTPUT / 'validation.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--fetch', action='store_true')
    parser.add_argument('--prepare', action='store_true')
    parser.add_argument('--validate', action='store_true')
    parser.add_argument('--catalog-only', action='store_true', help='Update sequence metadata without rewriting original pixel artifacts')
    args = parser.parse_args()
    if args.fetch:
        fetch()
    if args.prepare:
        prepare()
    if args.catalog_only:
        catalog_only()
    if args.validate:
        validate()


if __name__ == '__main__':
    main()
