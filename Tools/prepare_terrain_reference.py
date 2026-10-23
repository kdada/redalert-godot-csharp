#!/usr/bin/env python3
"""Extract original bridge/cliff data and retain native image layers and anchors.

Only original binary game assets and original INI/map data are inputs. No game
implementation is downloaded or copied. Existing local independent converters
are invoked directly; this tool never builds or launches the game.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import threading
import zlib

ROOT = Path(__file__).resolve().parents[1]
REFERENCE = ROOT / 'Assets/Reference/Terrain'
OUTPUT = ROOT / 'Assets/Generated/Terrain/OriginalStructures'
CACHE = Path(os.environ['XDG_CACHE_HOME']) / 'redalert-godot-csharp'
PALETTE = ROOT / 'Assets/Reference/Visual/Terrain/isotem.pal'
REPORT = ROOT / 'Reports/terrain-reference'


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def fetch():
    reader = load('terrain_original_range_reader', ROOT / 'Assets/Reference/Tools/fetch_ra2_samples.py')
    reader.ROOT = REFERENCE
    reader.MAX_BYTES = 6_000_000
    lock = threading.Lock()

    def exact_range(archive, offset, length):
        result = bytearray()
        while length:
            if archive == 'ra2.mix':
                part, position = divmod(offset, 70_000_000)
                filename = f'ra2.mix.part{part:02d}'
                count = min(length, 70_000_000 - position)
            else:
                filename, position, count = archive, offset, length
            with lock:
                if reader.transferred + count > reader.MAX_BYTES:
                    raise RuntimeError('terrain reference download exceeded 6 MB budget')
                reader.transferred += count
            url = reader.BASE + filename + f'?range={position}-{count}'
            command = ['curl', '--fail', '--silent', '--show-error', '--proxy', 'http://127.0.0.1:10808',
                       '--max-time', '25', '--retry', '4', '--retry-all-errors', '--retry-delay', '0', '--max-filesize', str(count), '--range',
                       f'{position}-{position + count - 1}', '--write-out', '%{http_code}', url]
            raw = subprocess.run(command, check=True, capture_output=True).stdout
            if raw[-3:] != b'206' or len(raw) != count + 3:
                raise RuntimeError(f'Exact HTTP range not honored for {filename}: {len(raw)} bytes')
            result.extend(raw[:-3]); offset += count; length -= count
        return bytes(result)

    reader.fetch = exact_range
    root = reader.mix_index('ra2.mix')
    wanted_tmp = [f'{stem}{number:02d}.tem' for stem, count in [('ovrps', 16), ('wbrdge', 2), ('slope', 20), ('cliff', 8), ('wcliff', 8)] for number in range(1, count + 1)]
    wanted_sprites = [f'lobrdg{number:02d}{ext}' for number in range(1, 29) for ext in ('.tem', '.shp')]
    wanted_sprites += [name + ext for name in ['lobrdge1', 'lobrdge2', 'lobrdge3', 'lobrdge4', 'bridge', 'bridgb'] for ext in ('.tem', '.shp')]
    jobs = []; found = {}
    for nested in ('isotemp.mix', 'tem.mix', 'conquer.mix', 'generic.mix', 'local.mix'):
        position, _ = root[reader.filename_hash(nested)]
        index = reader.mix_index('ra2.mix', position)
        names = wanted_tmp + wanted_sprites if nested == 'isotemp.mix' else wanted_sprites
        available = [name for name in names if reader.filename_hash(name) in index]
        found[nested] = available
        for name in available:
            destination = REFERENCE / 'Original' / name
            if destination.exists():
                continue
            jobs.append((nested, index, name))
    def one(job):
        nested, index, name = job
        reader.extract('ra2.mix', nested, index, name, 'Original')
    with ThreadPoolExecutor(max_workers=5) as executor:
        list(executor.map(one, jobs))
    REFERENCE.mkdir(parents=True, exist_ok=True)
    manifest = REFERENCE / 'sources.json'
    old = json.loads(manifest.read_text()) if manifest.exists() else {}
    assets = {item['path']: item for item in old.get('assets', [])}
    assets.update({item['path']: item for item in reader.records})
    manifest.write_text(json.dumps({'schema': 1, 'sourceRevision': reader.REV, 'referenceRoot': 'Assets/Reference/Terrain',
        'rangeBytesThisRun': reader.transferred, 'found': found, 'assets': list(assets.values())}, indent=2) + '\n')
    print(json.dumps({'found': found, 'newAssets': len(reader.records), 'rangeBytes': reader.transferred}, indent=2))


def png_write(path, width, height, pixels):
    def chunk(kind, value):
        return struct.pack('>I', len(value)) + kind + value + struct.pack('>I', zlib.crc32(kind + value) & 0xffffffff)
    raw = b''.join(b'\0' + pixels[y * width * 4:(y + 1) * width * 4] for y in range(height))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def blit(destination, dw, source, sw, sh, x, y):
    for row in range(sh):
        for col in range(sw):
            a = (row * sw + col) * 4
            if source[a + 3] == 0:
                continue
            b = ((y + row) * dw + x + col) * 4
            destination[b:b + 4] = source[a:a + 4]


def convert_bridge_shp(source, directory):
    # Low-bridge SHPs contain unused zero-offset headers whose compression byte
    # is 4. Those empty headers are metadata, not compressed images. Preserve
    # them verbatim; decode only actual format-3 payloads, without altering input.
    data = source.read_bytes(); magic, cw, ch, count = struct.unpack_from('<4H', data)
    assert magic == 0 and 0 < cw <= 512 and 0 < ch <= 512 and 0 < count <= 64
    palette = PALETTE.read_bytes(); frames = []; decoded = []
    for i in range(count):
        p = 8 + i * 24
        x, y, width, height = struct.unpack_from('<4H', data, p)
        compression = data[p + 8]; offset = struct.unpack_from('<I', data, p + 20)[0]
        indices = bytearray(width * height)
        if offset:
            assert compression == 3 and width > 0 and height > 0 and x + width <= cw and y + height <= ch
            cursor = offset
            for row in range(height):
                length = struct.unpack_from('<H', data, cursor)[0]
                assert length >= 2 and cursor + length <= len(data)
                end = cursor + length; cursor += 2; col = 0
                while cursor < end:
                    value = data[cursor]; cursor += 1
                    if value:
                        assert col < width
                        indices[row * width + col] = value; col += 1
                    else:
                        assert cursor < end
                        run = data[cursor]; cursor += 1
                        assert run > 0 and (col + run <= width or cursor == end and col + run == width + 1)
                        col = min(width, col + run)
        rgba = bytearray(width * height * 4)
        for pixel, value in enumerate(indices):
            if value:
                rgba[pixel * 4:pixel * 4 + 4] = bytes((palette[value * 3] << 2, palette[value * 3 + 1] << 2, palette[value * 3 + 2] << 2, 255))
        frames.append({'index': i, 'sourceRect': {'x': x, 'y': y, 'width': width, 'height': height},
            'centerOffset': {'x': x + width / 2 - cw / 2, 'y': y + height / 2 - ch / 2},
            'compression': compression, 'dataOffset': offset, 'hasData': bool(offset), 'role': 'unspecified',
            'indicesSha256': hashlib.sha256(indices).hexdigest(), 'imageSet': 0})
        decoded.append((width, height, rgba, indices))
    cell_w = max(1, max(item[0] for item in decoded)); cell_h = max(1, max(item[1] for item in decoded))
    columns = max(1, math.ceil(math.sqrt(count))); aw = columns * cell_w; ah = math.ceil(count / columns) * cell_h
    atlas = bytearray(aw * ah * 4); directory.mkdir(parents=True, exist_ok=True)
    for i, (width, height, rgba, indices) in enumerate(decoded):
        ax = i % columns * cell_w; ay = i // columns * cell_h
        frames[i]['imageRect'] = {'x': ax, 'y': ay, 'width': width, 'height': height}
        blit(atlas, aw, rgba, width, height, ax, ay)
        if frames[i]['hasData']:
            (directory / f'frame-{i:03d}.indices.bin').write_bytes(indices)
    png_write(directory / 'atlas.png', aw, ah, atlas)
    doc = {'schemaVersion': 1, 'format': 'shp-ts-ra2', 'sourceSha256': hashlib.sha256(data).hexdigest(),
        'canvasWidth': cw, 'canvasHeight': ch, 'frames': frames,
        'images': [{'color': 'atlas.png', 'width': aw, 'height': ah}],
        'notes': ['Unused zero-offset frames retain their original headers, including compression 4; no pixels exist to decode.',
                  'Only populated format-3 frames are decoded, preserving palette indices and native canvas coordinates.']}
    (directory / 'manifest.json').write_text(json.dumps(doc, indent=2) + '\n')


def prepare():
    png = load('terrain_original_png_reader', ROOT / 'Tools/validate_art.py').png
    sources = sorted((REFERENCE / 'Original').glob('*.tem')) + sorted((REFERENCE / 'Original').glob('*.shp'))
    sources += [ROOT / f'Assets/Reference/Visual/Terrain/shore{i:02d}.tem' for i in range(1, 43)]
    sources += [ROOT / 'Assets/Reference/Visual/Terrain/water01.tem']
    layers = []; templates = []
    selection_path = REFERENCE / 'selection.json'
    selection = json.loads(selection_path.read_text()) if selection_path.exists() else {}
    low_axes = {}
    for axis, entries in selection.get('lowBridge', {}).items():
        for entry in [entries['start'], *entries['middle'], entries['end']]:
            low_axes[entry['template']] = axis
    for source in sources:
        raw = source.read_bytes(); is_shp = struct.unpack_from('<H', raw)[0] == 0
        kind = 'shp' if is_shp else 'tmp'
        directory = OUTPUT / 'Tiles' / source.stem
        manifest = directory / 'manifest.json'
        if not manifest.exists():
            if is_shp:
                convert_bridge_shp(source, directory)
            else:
                binary = CACHE / 'terrain-pipeline/bin/TerrainPipeline/debug/Ra2TerrainPipeline.dll'
                command = ['dotnet', str(binary), 'convert', '--tmp', str(source), '--pal', str(PALETTE), '--out', str(directory)]
                result = subprocess.run(command, capture_output=True, text=True)
                if result.returncode:
                    raise RuntimeError(f'{source.name}: {result.stderr}\n{result.stdout}')
        doc = json.loads(manifest.read_text())
        template = {'id': source.stem, 'format': kind, 'sourcePath': source.relative_to(ROOT).as_posix(),
                    'sourceSha256': hashlib.sha256(raw).hexdigest(), 'manifestPath': manifest.relative_to(ROOT).as_posix(), 'frames': []}
        if is_shp:
            # Preserve source-canvas coordinates separately from atlas packing.
            # Overlay canvas center is aligned to the projected tile center.
            template['canvasWidth'] = doc['canvasWidth']; template['canvasHeight'] = doc['canvasHeight']
            template['pixelFootAnchorFromTileOrigin'] = {'x': doc['canvasWidth'] / 2, 'y': doc['canvasHeight'] / 2 - 15}
            template['anchorConvention'] = 'Native canvas center aligned to tile center; tile origin is the top diamond vertex.'
            if source.stem in low_axes:
                axis = low_axes[source.stem]
                template['lowBridgeAxis'] = axis
                template['nativeCanvasCenterAnchor'] = dict(template['pixelFootAnchorFromTileOrigin'])
                template['pixelFootAnchorFromTileOrigin'] = {'x': 90, 'y': 35}
                template['deckPlaneCanvasQuad'] = ([{'x': 120, 'y': 20}, {'x': 150, 'y': 35}, {'x': 60, 'y': 80}, {'x': 30, 'y': 65}] if axis == 'X'
                    else [{'x': 60, 'y': 20}, {'x': 150, 'y': 65}, {'x': 120, 'y': 80}, {'x': 30, 'y': 35}])
                template['anchorConvention'] = 'Derived native deck-plane registration for canonical level-zero bridge: source deck center (90,50) aligns to projected tile center (0,15), giving (90,35). Original canvas-center convention (90,45) is retained separately; this is not a claim about original engine offsets.'
            template['spriteManifest'] = doc
            for frame in doc['frames']:
                if not frame['hasData']:
                    continue
                image = doc['images'][frame['imageSet']]
                path = directory / image['color']
                iw, ih, rgba = png(path, True)
                rect = frame['imageRect']; width = rect['width']; height = rect['height']
                if not width or not height:
                    continue
                cropped = bytearray(width * height * 4)
                for row in range(height):
                    start = ((rect['y'] + row) * iw + rect['x']) * 4
                    cropped[row * width * 4:(row + 1) * width * 4] = rgba[start:start + width * 4]
                source_rect = frame['sourceRect']
                detail = {'kind': 'sprite', 'size': {'width': width, 'height': height},
                    'offset': {'x': source_rect['x'], 'y': source_rect['y']}, 'sourceRegion': rect,
                    'pngPath': path.relative_to(ROOT).as_posix(), 'atlasIndex': len(layers)}
                layers.append((detail, width, height, cropped))
                item = {key: frame[key] for key in ('index', 'sourceRect', 'centerOffset', 'role', 'dataOffset', 'indicesSha256')}
                item['layers'] = [detail]
                if source.stem in low_axes:
                    axis = low_axes[source.stem]; foreground = bytearray(len(cropped))
                    for row in range(height):
                        for col in range(width):
                            px = source_rect['x'] + col + .5; py = source_rect['y'] + row + .5
                            # Partition only the near edge/downward timbers. The
                            # road-top interior is excluded, preventing a second
                            # ground pass from painting over vehicles again.
                            keep = (28 <= px <= 63 and py >= .5 * px + 49) if axis == 'X' else (118 <= px <= 153 and py >= -.5 * px + 139)
                            if keep:
                                p = (row * width + col) * 4
                                foreground[p:p + 4] = cropped[p:p + 4]
                    mask_path = OUTPUT / 'Masks' / (source.stem + f'-{frame["index"]:03d}.foreground.png')
                    png_write(mask_path, width, height, foreground)
                    fg = {'kind': 'front-rail', 'size': {'width': width, 'height': height},
                        'offset': dict(detail['offset']), 'sourceOffset': dict(detail['offset']),
                        'pngPath': mask_path.relative_to(ROOT).as_posix(), 'atlasIndex': len(layers),
                        'derivation': 'Original near-side edge and lower timber pixels only; no repainting. Excludes the deck interior. Geometric mask registered to deckPlaneCanvasQuad.'}
                    layers.append((fg, width, height, foreground))
                    item['foregroundLayer'] = fg
                template['frames'].append(item)
                canvas = bytearray(doc['canvasWidth'] * doc['canvasHeight'] * 4)
                blit(canvas, doc['canvasWidth'], cropped, width, height, source_rect['x'], source_rect['y'])
                png_write(OUTPUT / 'Templates' / (source.stem + f'-{frame["index"]:03d}.png'), doc['canvasWidth'], doc['canvasHeight'], canvas)
            templates.append(template)
            continue
        template.update(doc['template'])
        draw_layers = []
        for frame in doc['frames']:
            item = {key: frame[key] for key in ('index', 'cellX', 'cellY', 'offset', 'gridOffset', 'heightAdjustedOffset', 'height', 'terrainType', 'rampType', 'flags', 'extraOffset', 'zOffset', 'extraZOffset')}
            item['layers'] = []
            for layer_name in ('diamond', 'extra'):
                layer = frame.get(layer_name)
                if not layer or not layer['exported']:
                    continue
                path = directory / layer['rgbaPng']
                width, height, pixels = png(path, True)
                detail = dict(layer)
                detail['pngPath'] = path.relative_to(ROOT).as_posix()
                detail['indicesPath'] = (directory / layer['indicesFile']).relative_to(ROOT).as_posix()
                detail['zPath'] = (directory / layer['zFile']).relative_to(ROOT).as_posix() if layer['zFile'] else None
                detail['atlasIndex'] = len(layers)
                layers.append((detail, width, height, pixels))
                item['layers'].append(detail)
                adjusted_y = layer['offset']['y'] + (doc['template']['maxHeight'] - frame['height']) * 15
                draw_layers.append((layer['offset']['x'], adjusted_y, width, height, pixels, frame['cellX'] + frame['cellY'], layer_name))
            template['frames'].append(item)
        min_x = min(item[0] for item in draw_layers); min_y = min(item[1] for item in draw_layers)
        max_x = max(item[0] + item[2] for item in draw_layers); max_y = max(item[1] + item[3] for item in draw_layers)
        canvas = bytearray((max_x - min_x) * (max_y - min_y) * 4)
        for x, y, w, h, pixels, depth, layer_name in sorted(draw_layers, key=lambda item: (item[5], item[6] == 'extra')):
            blit(canvas, max_x - min_x, pixels, w, h, x - min_x, y - min_y)
        assembled = OUTPUT / 'Templates' / (source.stem + '.png')
        png_write(assembled, max_x - min_x, max_y - min_y, canvas)
        template['assembledPath'] = assembled.relative_to(ROOT).as_posix()
        template['assembledBounds'] = {'x': min_x, 'y': min_y, 'width': max_x - min_x, 'height': max_y - min_y}
        template['originInAssembled'] = {'x': -min_x, 'y': -min_y}
        template['drawOffsetFromTileOrigin'] = {'x': min_x - 30, 'y': min_y}
        templates.append(template)
    # Shelf packing preserves native layer dimensions and every original pixel.
    atlas_width = 2048; x = 2; y = 2; row_height = 0
    for detail, width, height, pixels in layers:
        if x + width + 2 > atlas_width:
            x = 2; y += row_height + 4; row_height = 0
        detail['region'] = {'x': x, 'y': y, 'width': width, 'height': height}
        x += width + 4; row_height = max(row_height, height)
    atlas_height = y + row_height + 2
    atlas = bytearray(atlas_width * atlas_height * 4)
    for detail, width, height, pixels in layers:
        rect = detail['region']; blit(atlas, atlas_width, pixels, width, height, rect['x'], rect['y'])
    png_write(OUTPUT / 'atlas.png', atlas_width, atlas_height, atlas)
    catalog = {'schema': 1, 'atlas': 'atlas.png', 'atlasWidth': atlas_width, 'atlasHeight': atlas_height,
        'heightPixelsPerOriginalLevel': 15, 'diamondSize': {'width': 60, 'height': 30},
        'conventions': {'offset': 'Original signed TMP top-left relative to template origin, not tile center.',
            'layerOrder': 'Retain diamond and extra separately. Extra may contain actual cliffs, rails and bridge structure.',
            'assembled': 'Native pixels with TMP maxHeight-relative Y adjustment; no resampling, rotation, synthesis or repainting.',
            'z': 'Original depth bytes retained; not interpreted as world elevation.',
            'shore': 'Original shore templates are complete 2x2 patches at the water/land boundary, not separate vertical cliffs.',
            'license': 'Original Westwood/EA game artwork; existing project authorization, no new redistribution license asserted.'},
        'selection': selection, 'templates': templates}
    (OUTPUT / 'catalog.json').write_text(json.dumps(catalog, indent=2) + '\n')
    REPORT.mkdir(parents=True, exist_ok=True)
    print(json.dumps({'templates': len(templates), 'tmpLayers': len(layers), 'atlas': [atlas_width, atlas_height],
                      'sourceBytes': sum(path.stat().st_size for path in sources)}, indent=2))


def validate():
    png = load('terrain_original_validation_png', ROOT / 'Tools/validate_art.py').png
    doc = json.loads((OUTPUT / 'catalog.json').read_text())
    aw, ah, atlas = png(OUTPUT / doc['atlas'], True)
    cached = {}; checked = 0
    for template in doc['templates']:
        assert hashlib.sha256((ROOT / template['sourcePath']).read_bytes()).hexdigest() == template['sourceSha256']
        for frame in template['frames']:
            frame_layers = frame['layers'] + ([frame['foregroundLayer']] if 'foregroundLayer' in frame else [])
            for layer in frame_layers:
                path = ROOT / layer['pngPath']
                if path not in cached:
                    cached[path] = png(path, True)
                sw, sh, pixels = cached[path]
                region = layer['region']; width = region['width']; height = region['height']
                source = layer.get('sourceRegion', {'x': 0, 'y': 0, 'width': sw, 'height': sh})
                assert region['x'] >= 0 and region['y'] >= 0 and region['x'] + width <= aw and region['y'] + height <= ah
                assert width == source['width'] and height == source['height']
                for row in range(height):
                    a = ((source['y'] + row) * sw + source['x']) * 4
                    b = ((region['y'] + row) * aw + region['x']) * 4
                    expected = pixels[a:a + width * 4]; actual = atlas[b:b + width * 4]
                    for col in range(width):
                        p = col * 4
                        assert expected[p + 3] == actual[p + 3]
                        if expected[p + 3]:
                            assert expected[p:p + 4] == actual[p:p + 4]
                checked += 1
    result = {'templates': len(doc['templates']), 'layersWithExactNativeOpaquePixelsAndAlpha': checked,
        'atlasDimensions': [aw, ah], 'builds': 0, 'gameLaunches': 0}
    REPORT.mkdir(parents=True, exist_ok=True)
    (REPORT / 'validation.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--fetch', action='store_true')
    parser.add_argument('--prepare', action='store_true')
    parser.add_argument('--validate', action='store_true')
    args = parser.parse_args()
    if args.fetch:
        fetch()
    if args.prepare:
        prepare()
    if args.validate:
        validate()


if __name__ == '__main__':
    main()
