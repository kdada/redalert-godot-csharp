#!/usr/bin/env python3
"""Retain original RA2 SHROUD/FOG bytes and prepare a documented alpha atlas.

Asset data only: no game/engine code download, source generator, build or launch.
Run with PYTHONDONTWRITEBYTECODE=1; imports also suppress bytecode explicitly.
The native numeric shroud palette is unavailable: the normalized alpha transfer
is an explicit inference. Raw indices and source records remain authoritative.
"""
import argparse
from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import struct
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
REFERENCE = ROOT / 'Assets/Reference/Shroud'
OUTPUT = ROOT / 'Assets/Generated/Shroud'
REVISION = 'acfa5b834899e7fab4c8499ccb4b49c402927b44'
ARCHIVE_URL = f'https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/{REVISION}/game-res/ra2.mix.part00'
PROXY = 'http://127.0.0.1:10808'
SOURCE_BYTES = 88712
SOURCES = {
    'shroud': {'offset': 13778928, 'sha256': '9818f7e2685fb1e8ed75ceff568aaf5d36350c747cbd4f30856458bba12e4964'},
    'fog': {'offset': 13867648, 'sha256': 'b8e3640435140041f0ac3d4ff18ad99b035a2292b685a2fd89c64bf1cc55e73f'},
}
YAML_DATA = {
    'palettes.yaml': {
        'url': 'https://raw.githubusercontent.com/OpenRA/ra2/master/mods/ra2/rules/palettes.yaml',
        'bytes': 3870, 'sha256': 'f8342f975a8d3921f51be3870d0e14d95496eb0c33416b21c9bf1de27fa92b31'},
    'misc.yaml': {
        'url': 'https://raw.githubusercontent.com/OpenRA/ra2/master/mods/ra2/sequences/misc.yaml',
        'bytes': 14337, 'sha256': '28f2182ffa03cf48d6177c572c887e83dc7a59cc317b81a9cdc7403feb46ace7'},
}
MASKS = [255, 16, 32, 48, 64, 80, 96, 112, 128, 144, 160, 176, 192, 208, 224, 240,
         20, 40, 56, 65, 97, 130, 148, 194, 24, 33, 66, 132, 28, 41, 67, 134,
         1, 2, 4, 8, 3, 6, 12, 9, 7, 14, 13, 11, 5, 10, 15, 255]
WIDTH, HEIGHT, FRAME_COUNT, REAL_COUNT = 60, 30, 96, 48
TABLE_END = 8 + FRAME_COUNT * 24
PADDING, COLUMNS, CELL_WIDTH, CELL_HEIGHT = 2, 8, 64, 34
ATLAS_WIDTH, ATLAS_HEIGHT = 512, 408
SAMPLE_POINTS = {'top': (30, 1), 'right': (58, 15), 'bottom': (30, 29), 'left': (1, 15),
                 'upperRight': (44, 8), 'lowerRight': (44, 22),
                 'lowerLeft': (15, 22), 'upperLeft': (15, 8), 'center': (30, 15)}
# These screen-space labels come from opacity-deficit centroids in the original
# pixels, independently of engine code. They do not name map/world axes.
ORIENTATION = [(16, 1, 'upperRight', 'edge'), (32, 2, 'lowerRight', 'edge'),
               (64, 4, 'lowerLeft', 'edge'), (128, 8, 'upperLeft', 'edge'),
               (1, 32, 'top', 'corner'), (2, 33, 'right', 'corner'),
               (4, 34, 'bottom', 'corner'), (8, 35, 'left', 'corner')]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def json_bytes(value):
    return (json.dumps(value, indent=2, ensure_ascii=False) + '\n').encode('utf-8')


def save_original(path, data):
    if path.exists():
        require(path.read_bytes() == data, f'Refusing to overwrite changed original: {path}')
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


def source_manifest():
    assets = []
    for name, entry in SOURCES.items():
        offset = entry['offset']
        assets.append({'path': name + '.shp', 'bytes': SOURCE_BYTES, 'sha256': entry['sha256'],
            'source_url': ARCHIVE_URL, 'request_url': ARCHIVE_URL + f'?range={offset}-{SOURCE_BYTES}',
            'source_archive': 'ra2.mix', 'nested_archive': 'conquer.mix',
            'archive_offset': offset, 'http_range': f'bytes={offset}-{offset + SOURCE_BYTES - 1}',
            'original_filename': name.upper() + '.SHP', 'actual_type': 'SHP',
            'header': {'magic': 0, 'width': WIDTH, 'height': HEIGHT, 'frames': FRAME_COUNT}})
    for name, entry in YAML_DATA.items():
        assets.append({'path': name, 'bytes': entry['bytes'], 'sha256': entry['sha256'],
            'source_url': entry['url'], 'actual_type': 'YAML',
            'purpose': 'DATA only; shroud palette declaration and extended-neighbor frame map'})
    return {'schema': 1, 'sourceRevision': REVISION, 'referenceRoot': 'Assets/Reference/Shroud',
        'archiveCatalog': 'Offsets/lengths/SHA256 supplied by parent after original MIX catalog verification.',
        'fetchPolicy': 'Only the two exact 88712-byte SHP ranges and two digest-pinned YAML data files. Existing originals must match; changed originals are never overwritten.',
        'yamlRevisionLimitation': 'OpenRA master URLs are mutable; retained snapshot sizes and SHA256 are pinned here and in the tool. Upstream drift fails closed.',
        'sourceArchiveBytes': SOURCE_BYTES * 2, 'assets': assets}


def curl_bytes(url, count, byte_range=None):
    command = ['curl', '--fail', '--silent', '--show-error', '--proxy', PROXY,
        '--max-time', '30', '--retry', '2', '--retry-all-errors', '--retry-delay', '0',
        '--max-filesize', str(count)]
    if byte_range is not None:
        command += ['--range', byte_range]
    command += ['--write-out', '%{http_code}', url]
    raw = subprocess.run(command, check=True, capture_output=True).stdout
    status = b'206' if byte_range is not None else b'200'
    require(raw[-3:] == status and len(raw) == count + 3,
        f'Unexpected HTTP status/length: {url}; got {raw[-3:]!r}/{len(raw) - 3}')
    return raw[:-3]


def fetch():
    transferred = 0
    for asset in source_manifest()['assets']:
        target = REFERENCE / asset['path']
        if target.exists():
            data = target.read_bytes()
        else:
            data = curl_bytes(asset.get('request_url', asset['source_url']), asset['bytes'],
                asset.get('http_range', '').removeprefix('bytes=') or None)
            transferred += len(data)
        require(len(data) == asset['bytes'] and sha256(data) == asset['sha256'],
            f'Source digest/length mismatch: {asset["path"]}')
        save_original(target, data)
    check_yaml_data()
    save_original(REFERENCE / 'sources.json', json_bytes(source_manifest()))
    print(json.dumps({'operation': 'fetch', 'sourceFiles': 4, 'downloadBytes': transferred}))


def check_yaml_data():
    for name, entry in YAML_DATA.items():
        data = (REFERENCE / name).read_bytes()
        require(len(data) == entry['bytes'] and sha256(data) == entry['sha256'], f'YAML data changed: {name}')
    palettes = (REFERENCE / 'palettes.yaml').read_text()
    match = re.search(r'^\s*Index: ([0-9, ]+)$', palettes, re.MULTILINE)
    require(match and [int(value.strip()) for value in match[1].split(',')] == MASKS, 'YAML frame map mismatch')
    require('TSShroudPalette@shroud:' in palettes and 'UseExtendedIndex: true' in palettes
        and 'ShroudPalette: shroud' in palettes and 'FogPalette: shroud' in palettes, 'YAML palette declaration mismatch')
    sequences = (REFERENCE / 'misc.yaml').read_text()
    match = re.search(r'^shroud:\n(.*?)(?=^\S|\Z)', sequences, re.MULTILINE | re.DOTALL)
    require(match and 'Filename: shroud.shp' in match[1] and 'Filename: fog.shp' in match[1]
        and 'Length: *' in match[1], 'YAML shroud/fog sequence declaration mismatch')


def decode_bytes(data, name):
    entry = SOURCES[name]
    require(len(data) == SOURCE_BYTES and sha256(data) == entry['sha256'], f'Original source changed: {name}')
    require(struct.unpack_from('<4H', data) == (0, WIDTH, HEIGHT, FRAME_COUNT), f'Invalid SHP header: {name}')
    frames = []
    for i in range(FRAME_COUNT):
        position = 8 + i * 24
        record = data[position:position + 24]
        x, y, width, height = struct.unpack_from('<4H', record)
        compression = record[8]
        offset = struct.unpack_from('<I', record, 20)[0]
        if i < REAL_COUNT:
            require((x, y, width, height, compression, offset) ==
                (0, 0, WIDTH, HEIGHT, 0, TABLE_END + i * WIDTH * HEIGHT), f'Invalid real frame: {name}/{i}')
            indices = data[offset:offset + width * height]
            require(len(indices) == 1800 and set(indices) <= set(range(128)) | {254}, f'Invalid raster: {name}/{i}')
        else:
            require((x, y, width, height, compression, offset) == (0, 0, 0, 0, 0, 0), f'Invalid placeholder: {name}/{i}')
            indices = b''
        metadata = {'index': i, 'mask': MASKS[i] if i < REAL_COUNT else None,
            'hasData': bool(offset), 'placeholder': i >= REAL_COUNT,
            'sourceRect': {'x': x, 'y': y, 'width': width, 'height': height},
            'compression': compression, 'dataOffset': offset,
            'recordHex': record.hex(), 'recordSha256': sha256(record),
            'indicesBytes': len(indices), 'indicesSha256': sha256(indices),
            'indexCounts': {str(k): v for k, v in sorted(Counter(indices).items())},
            'indicesRaw': f'Indices/{name}/{i:03d}.bin',
            'region': None, 'png': None}
        frames.append((metadata, indices))
    require(TABLE_END + REAL_COUNT * WIDTH * HEIGHT == len(data), 'Unaccounted SHP trailing data')
    return frames


def decode():
    check_yaml_data()
    return {name: decode_bytes((REFERENCE / (name + '.shp')).read_bytes(), name) for name in SOURCES}


def alpha(index):
    if index == 254:
        return 0
    require(0 <= index <= 127, f'Unsupported shroud intensity: {index}')
    return (index * 255 + 63) // 127


def rgba(indices):
    return b''.join(bytes((0, 0, 0, alpha(index))) for index in indices)


def alpha_mapping():
    return {'kind': 'inferred-normalized-original-intensity', 'rgb': [0, 0, 0],
        'transparentIndex': 254, 'fullOpacityIndex': 127, 'fullOpacityAlpha': 255,
        'formula': 'index == 254 ? 0 : (index * 255 + 63) // 127',
        'rounding': 'nearest integer, denominator 127',
        'directionEstablishedBy': 'Frame0 interior is a uniform 900-pixel diamond of127; exterior254. Single-neighbor frames reduce values toward2 on one edge; FOG retains64..127 in the same footprint. YAML declares a dedicated TSShroudPalette shared by shroud/fog.',
        'normalizationRationale': 'Assign the full-shroud frame0 diamond to full opacity and the unused exterior254 to transparent, preserving increasing source intensity and native gradients without synthesized shapes.',
        'exactNativeNumericPaletteProven': False,
        'limitations': 'Neither original SHP nor retained YAML contains a native numeric alpha LUT. Full-opacity endpoint and linear normalization are explicit presentation inferences, not a recovered native palette. No unittem/cameo palette is used. Raw indices remain exact; FOG opacity is encoded by its larger original intensities, with no extra alpha multiplier.',
        'observedIndexToAlpha': {str(i): alpha(i) for i in sorted({2, 127, 254} | set(range(5, 64, 2)) | set(range(64, 127, 2)))}}


def pixel_evidence(decoded):
    result = {}
    for name, frames in decoded.items():
        full, clear = frames[0][1], frames[47][1]
        require(Counter(full) == Counter({127: 900, 254: 900}), 'Frame0 evidence changed')
        require(clear == bytes([254]) * 1800, 'Frame47 evidence changed')
        inside = [i for i, value in enumerate(full) if value == 127]
        require(all(all(data[i] == 254 for i, value in enumerate(full) if value == 254)
            for _, data in frames[:47]), 'Original diamond footprint changed')
        orientation = []
        for bit, frame, label, kind in ORIENTATION:
            indices = frames[frame][1]
            deficit = sum(127 - indices[i] for i in inside)
            cx = sum((i % WIDTH) * (127 - indices[i]) for i in inside) / deficit
            cy = sum((i // WIDTH) * (127 - indices[i]) for i in inside) / deficit
            if label in ('upperRight', 'lowerRight', 'right'):
                require(cx > 29.5, 'Screen x orientation evidence failed')
            if label in ('upperLeft', 'lowerLeft', 'left'):
                require(cx < 29.5, 'Screen x orientation evidence failed')
            if label in ('upperRight', 'upperLeft', 'top'):
                require(cy < 15, 'Screen y orientation evidence failed')
            if label in ('lowerRight', 'lowerLeft', 'bottom'):
                require(cy > 15, 'Screen y orientation evidence failed')
            orientation.append({'bit': bit, 'frame': frame, 'screenDirection': label, 'type': kind,
                'opacityDeficitCentroid': [round(cx, 6), round(cy, 6)],
                'originalIndexProbes': {key: indices[y * WIDTH + x] for key, (x, y) in SAMPLE_POINTS.items()}})
        result[name] = {'allRealFrameIndexCounts': {str(k): v for k, v in sorted(Counter(b''.join(data for _, data in frames)).items())},
            'frame0': {'indexCounts': {'127': 900, '254': 900}, 'centerIndex': full[15 * WIDTH + 30],
                'cornerIndices': [full[i] for i in (0, 59, 29 * WIDTH, 1800 - 1)],
                'diamondRows': [{'y': y, 'left': next((x for x in range(WIDTH) if full[y * WIDTH + x] != 254), None),
                    'pixels': sum(index != 254 for index in full[y * WIDTH:(y + 1) * WIDTH])} for y in range(HEIGHT)]},
            'frame47': {'indexCounts': {'254': 1800}, 'hasStoredRaster': True, 'allTransparentAfterMapping': True},
            'duplicateMask255': {'frames': [0, 47], 'equalIndices': False,
                'differentPixels': sum(a != b for a, b in zip(full, clear)),
                'canonicalFrame': 0, 'clearVariantFrame': 47},
            'orientation': orientation}
    return result


def frame_metadata(name, number, source, base_y):
    value = dict(source)
    if number < REAL_COUNT:
        value['region'] = {'x': number % COLUMNS * CELL_WIDTH + PADDING,
            'y': base_y + number // COLUMNS * CELL_HEIGHT + PADDING, 'width': WIDTH, 'height': HEIGHT}
        value['png'] = f'Frames/{name}/{number:03d}.png'
    return value


def manifest(decoded):
    evidence = pixel_evidence(decoded)
    sequences = {}
    for sequence_index, (name, frames) in enumerate(decoded.items()):
        base_y = sequence_index * 6 * CELL_HEIGHT
        metadata = [frame_metadata(name, i, source, base_y) for i, (source, _) in enumerate(frames)]
        # First occurrence intentionally wins: 0 is full;47 is entirely clear.
        by_mask = {}
        for i, mask in enumerate(MASKS):
            by_mask.setdefault(str(mask), i)
        sequences[name] = {'source': f'Assets/Reference/Shroud/{name}.shp',
            'sourceSha256': SOURCES[name]['sha256'], 'sourceFrameCount': FRAME_COUNT,
            'storedRasterFrames': REAL_COUNT, 'emptyPlaceholderFrames': FRAME_COUNT - REAL_COUNT,
            'atlasBaseY': base_y, 'frames': metadata, 'frameByMask': by_mask,
            'fullMaskFrame': 0, 'clearVariantFrame': 47, 'mask255Variants': [0, 47]}
    return {'schema': 1, 'game': 'Red Alert 2 vanilla', 'atlas': 'atlas.png',
        'atlasWidth': ATLAS_WIDTH, 'atlasHeight': ATLAS_HEIGHT,
        'canvasWidth': WIDTH, 'canvasHeight': HEIGHT,
        'columns': COLUMNS, 'cellWidth': CELL_WIDTH, 'cellHeight': CELL_HEIGHT, 'padding': PADDING,
        'frameLayout': 'full-native-canvas; shroud rows0..5, fog rows6..11',
        'sequences': sequences, 'frameMap': MASKS, 'alphaMapping': alpha_mapping(),
        'screenOrientation': {str(bit): {'direction': label, 'type': kind, 'evidenceFrame': frame}
            for bit, frame, label, kind in ORIENTATION},
        'orientationLimitation': 'Directions name the PNG screen axes only: +x right,+y down. Bits name the side/corner where original opacity decreases. World/map neighbor offsets and mask construction belong to the consumer; no engine code was inspected.',
        'maskZeroPolicy': 'No frame carries mask0 in the supplied DATA map. For a clear mask0 consumer may explicitly select original clear frame47; this is a consumer policy, not another source frame-map entry.',
        'duplicateMaskPolicy': 'Preserve both255 variants. frameByMask chooses0 (full diamond),47 remains explicit clear variant. They differ at900 original pixels.',
        'drawing': 'Regions already contain the full original60x30 canvas. Preserve orientation and nearest sampling; do not apply sourceRect again. Empty records48..95 have no atlas regions or raster pixels.',
        'pixelPolicy': 'Every non-padding atlas pixel is the original indexed pixel at the original location transformed only to black with disclosed alpha. No redrawing, resampling, fabricated mask shape or placeholder pixels.',
        'dataProvenance': {'archiveSources': 'Assets/Reference/Shroud/sources.json',
            'frameMap': {'url': YAML_DATA['palettes.yaml']['url'], 'local': 'Assets/Reference/Shroud/palettes.yaml',
                'sha256': YAML_DATA['palettes.yaml']['sha256'], 'lines': [153, 159]},
            'sequenceNames': {'url': YAML_DATA['misc.yaml']['url'], 'local': 'Assets/Reference/Shroud/misc.yaml',
                'sha256': YAML_DATA['misc.yaml']['sha256'], 'lines': [609, 616]},
            'sourceOnly': True, 'engineCodeFetched': False},
        'pixelEvidence': evidence,
        'previews': {'contactSheet': 'contact-sheet.png', 'svgLegend': 'contact-sheet-legend.svg',
            'description': 'Contact sheet composites the source alpha pixels over a checkerboard for visibility. Row-major shroud0..47 then fog0..47. It is a preview only; atlas.png retains black RGBA with transparent padding.'},
        'reproduce': 'PYTHONDONTWRITEBYTECODE=1 python3 Tools/classic_art/prepare_shroud.py --fetch --prepare --validate'}


def contact_pixels(atlas):
    preview = bytearray(len(atlas))
    for y in range(ATLAS_HEIGHT):
        for x in range(ATLAS_WIDTH):
            offset = (y * ATLAS_WIDTH + x) * 4
            background = 208 if (x // 6 + y // 6) % 2 else 160
            value = (background * (255 - atlas[offset + 3]) + 127) // 255
            preview[offset:offset + 4] = bytes((value, value, value, 255))
    return preview


def legend_svg():
    labels = []
    for si, name in enumerate(SOURCES):
        labels.append(f'<text x="0" y="{si * 228 + 14}" font-size="13">{name.upper()}: source frame / extended-neighbor mask</text>')
        for i, mask in enumerate(MASKS):
            x, y = i % 8 * 64, si * 228 + 36 + i // 8 * 34
            labels.append(f'<text x="{x}" y="{y}" font-size="10">{i:02d} / {mask}</text>')
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="512" height="456" viewBox="0 0 512 456">'
        '<rect width="512" height="456" fill="white"/><g fill="black" font-family="monospace">'
        + ''.join(labels) + '</g></svg>\n').encode()


def prepare():
    decoded = decode()
    document = manifest(decoded)
    writer = load('shroud_cursor_png_writer', ROOT / 'Tools/classic_art/prepare_cursors.py').png_write
    atlas = bytearray(ATLAS_WIDTH * ATLAS_HEIGHT * 4)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name, frames in decoded.items():
        for i, (_, indices) in enumerate(frames):
            metadata = document['sequences'][name]['frames'][i]
            path = OUTPUT / metadata['indicesRaw']
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(indices)
            if not metadata['hasData']:
                continue
            pixels = rgba(indices)
            region = metadata['region']
            for y in range(HEIGHT):
                offset = ((region['y'] + y) * ATLAS_WIDTH + region['x']) * 4
                atlas[offset:offset + WIDTH * 4] = pixels[y * WIDTH * 4:(y + 1) * WIDTH * 4]
            writer(OUTPUT / metadata['png'], WIDTH, HEIGHT, pixels)
    writer(OUTPUT / 'atlas.png', ATLAS_WIDTH, ATLAS_HEIGHT, atlas)
    writer(OUTPUT / 'contact-sheet.png', ATLAS_WIDTH, ATLAS_HEIGHT, contact_pixels(atlas))
    (OUTPUT / 'contact-sheet-legend.svg').write_bytes(legend_svg())
    (OUTPUT / 'manifest.json').write_bytes(json_bytes(document))
    (REFERENCE / 'sources.json').write_bytes(json_bytes(source_manifest()))
    print(json.dumps({'operation': 'prepare', 'atlas': [ATLAS_WIDTH, ATLAS_HEIGHT],
        'storedFrames': REAL_COUNT * 2, 'emptyRecords': 96, 'mask255PixelDifferencesPerSource': 900}))


def validate():
    decoded = decode()
    document = manifest(decoded)
    require(json.loads((REFERENCE / 'sources.json').read_text()) == source_manifest(), 'Source provenance mismatch')
    require(json.loads((OUTPUT / 'manifest.json').read_text()) == document, 'Generated manifest mismatch')
    read_png = load('shroud_png_validator', ROOT / 'Tools/validate_art.py').png
    aw, ah, atlas = read_png(OUTPUT / 'atlas.png', True)
    require((aw, ah) == (ATLAS_WIDTH, ATLAS_HEIGHT), 'Atlas size mismatch')
    expected_atlas = bytearray(len(atlas))
    checked_pixels = 0
    for name, frames in decoded.items():
        for i, (_, indices) in enumerate(frames):
            metadata = document['sequences'][name]['frames'][i]
            raw = (OUTPUT / metadata['indicesRaw']).read_bytes()
            require(raw == indices and sha256(raw) == metadata['indicesSha256'], f'Indices mismatch: {name}/{i}')
            if metadata['placeholder']:
                require(metadata['region'] is None and metadata['png'] is None and not raw, 'Placeholder fabricated pixels')
                require(not (OUTPUT / f'Frames/{name}/{i:03d}.png').exists(), 'Placeholder PNG must not exist')
                continue
            expected = rgba(indices)
            fw, fh, pixels = read_png(OUTPUT / metadata['png'], True)
            require((fw, fh) == (WIDTH, HEIGHT) and pixels == expected, f'Frame RGBA mismatch: {name}/{i}')
            region = metadata['region']
            for y in range(HEIGHT):
                offset = ((region['y'] + y) * aw + region['x']) * 4
                row = expected[y * WIDTH * 4:(y + 1) * WIDTH * 4]
                require(atlas[offset:offset + WIDTH * 4] == row, f'Atlas pixels mismatch: {name}/{i}/{y}')
                expected_atlas[offset:offset + WIDTH * 4] = row
            checked_pixels += WIDTH * HEIGHT
    require(atlas == expected_atlas, 'Atlas padding or regions differ from original raster conversion')
    pw, ph, pixels = read_png(OUTPUT / 'contact-sheet.png', True)
    require((pw, ph) == (aw, ah) and pixels == contact_pixels(atlas), 'Contact preview mismatch')
    require((OUTPUT / 'contact-sheet-legend.svg').read_bytes() == legend_svg(), 'Contact legend mismatch')
    outputs = [path for path in sorted(OUTPUT.rglob('*')) if path.is_file()
        and path.name not in ('validation.json',) and not path.name.endswith('.import')]
    validation = {'schema': 1, 'pass': True,
        'sourceFilesChecked': len(SOURCES) + len(YAML_DATA),
        'sourceSha256': {name + '.shp': entry['sha256'] for name, entry in SOURCES.items()},
        'sequencesChecked': 2, 'sourceRecordsChecked': 192, 'realStoredFramesChecked': 96,
        'emptyPlaceholdersChecked': 96, 'pixelsChecked': checked_pixels,
        'allOriginalIndicesMatch': True, 'allOriginalRecordHashesMatch': True,
        'allAtlasAndIndividualPixelsMatchDeclaredMapping': True, 'allPaddingTransparentBlack': True,
        'allPlaceholdersHaveZeroByteIndicesAndNoRegion': True,
        'maskMapMatchesRetainedYaml': True, 'screenOrientationVerifiedFromOriginalPixels': True,
        'mask255Variants': {'differentPixelsPerSource': 900, 'fullFrame': 0, 'clearFrame': 47},
        'alphaMapping': document['alphaMapping'], 'nativeGameLaunched': False,
        'buildRun': False, 'engineCodeFetched': False,
        'visualInspection': 'Contact sheet, raw frame preview, and atlas are available; visual inspection is a separate human/agent observation, not claimed by this byte validator.',
        'artifacts': [{'path': path.relative_to(OUTPUT).as_posix(), 'bytes': path.stat().st_size,
            'sha256': sha256(path.read_bytes())} for path in outputs]}
    (OUTPUT / 'validation.json').write_bytes(json_bytes(validation))
    print(json.dumps({key: value for key, value in validation.items() if key not in ('artifacts', 'alphaMapping', 'visualInspection')}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--fetch', action='store_true', help='Fetch/check only digest-pinned source slices and YAML data')
    parser.add_argument('--prepare', action='store_true', help='Build original-index proofs, inferred-alpha atlas and previews')
    parser.add_argument('--validate', action='store_true', help='Check every source record, raw index and generated pixel')
    args = parser.parse_args()
    if not any((args.fetch, args.prepare, args.validate)):
        parser.error('choose --fetch, --prepare and/or --validate')
    if args.fetch:
        fetch()
    if args.prepare:
        prepare()
    if args.validate:
        validate()


if __name__ == '__main__':
    main()
