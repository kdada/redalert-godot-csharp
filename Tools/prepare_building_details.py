#!/usr/bin/env python3
"""Convert original building detail SHPs without changing any source frame index.

Uses the existing independent C# AssetPipeline DLL. The full manifests retain
source rectangles, compression/data offsets, empty frames, original index hashes,
remap masks, and second-half shadow pairing. No game code is imported.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import zlib

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
REFERENCE = ROOT / 'Assets/Reference'
OUTPUT = ROOT / 'Assets/Generated/Details'
DLL = Path(os.environ['XDG_CACHE_HOME']) / 'redalert-godot-csharp/asset-pipeline/bin/AssetPipeline/debug/Ra2AssetPipeline.dll'
PALETTE = REFERENCE / 'Formats/unittem.pal'


def layer(name, length=1, damaged=0, ticks=2, powered=True, condition='always', inactive='first', source=''):
    return dict(name=name, start=0, length=length, damagedStart=damaged, ticks=ticks,
                powered=powered, condition=condition, inactive=inactive, artSection=source)


# Ordered back to front; bib -> building -> ramp -> roof -> flags/fans.
# Only numeric sequence/coordinate facts are used from the public references.
# Original art.ini has several off-by-one LoopEnd values; a sequence must stay
# within the retained SHP color half. No frames are removed to fix that metadata.
BUILDINGS = {
    'ggpowr': dict(buildup='gtpowrmk', layers=[layer('ggpowr_a', 8, 8, 3, False, source='GAPOWR_A/GAPOWR_AD')]),
    'ngpowr': dict(buildup='ntpowrmk', layers=[layer('ngpowr_a', 19, 19, 3, False, source='NAPOWR_A/NAPOWR_AD')]),
    'ggpile': dict(buildup='gtpilemk', layers=[layer('ggpile_a', 16, 16, 2, False, source='GAPILE_A/GAPILE_AD; GAPILE.ActiveAnimPowered=no')]),
    'nghand': dict(buildup='nthandmk', layers=[]),
    'ggcnst': dict(buildup='gtcnstmk', layers=[
        layer('ggcnst_a', 3, 3, 3, source='GACNST_A/GACNST_AD'),
        layer('ggcnst_b', 20, 20, 3, condition='construction', source='GACNST_B/GACNST_BD')]),
    'ngcnst': dict(buildup='ntcnstmk', layers=[
        layer('ngcnst_a', 11, 11, 4, source='NACNST_A/NACNST_AD'),
        layer('ngcnst_c', damaged=1, condition='idleConstruction', inactive='hide', source='NACNST_C/NACNST_CD'),
        layer('ngcnst_b', 21, 21, 3, condition='construction', inactive='hide', source='NACNST_B/NACNST_BD')]),
    'ggrefn': dict(buildup='gtrefnmk', bib='ggrefnbb', layers=[
        *(layer(f'ggrefnl{i}', 6, ticks=4, source=f'GAREFNL{i}') for i in range(1, 4)),
        layer('ggrefnor', 20, ticks=2, condition='docking', inactive='hide', source='GAREFNOR')]),
    'ngrefn': dict(buildup='ntrefnmk', bib='ngrefnbb', layers=[
        *(layer(f'ngrefnl{i}', 3, 3, 4, source=f'NAREFNL{i}/NAREFNL{i}D') for i in range(1, 5)),
        layer('ngrefnor', 20, ticks=2, condition='docking', inactive='hide', source='NAREFNOR')]),
    'ggweap': dict(buildup='gtweapmk', bib='ggweapbb', layers=[
        layer('ggweap_1', damaged=1, powered=False, source='GAWEAP.UnderDoorAnim'),
        layer('ggweap_2', damaged=1, powered=False, source='GAWEAP.DeployingAnim'),
        layer('ggweap_a', 15, 15, 2, False, source='GAWEAP_A/GAWEAP_AD'),
        layer('ggweap_b', 6, ticks=2, source='GAWEAP_B/GAWEAP_BD')]),
    'ngweap': dict(buildup='ntweapmk', bib='ngweapbb', layers=[
        layer('ngweap_1', damaged=1, powered=False, source='NAWEAP.UnderDoorAnim'),
        layer('ngweap_2', damaged=1, powered=False, source='NAWEAP.DeployingAnim'),
        layer('ngweap_a', 13, ticks=3, source='NAWEAP_A; original has no damaged variant')]),
    'gggcan': dict(buildup='gtgcanmk', layers=[]),
    'ngtsla': dict(buildup='nttslamk', layers=[]),
    'ggpill': dict(buildup='gtpillmk', layers=[]),
    'ggairc': dict(buildup='gtaircmk', layers=[]),
    'ngradr': dict(buildup='ntradrmk', layers=[]),
    'ggtech': dict(buildup='gttechmk', layers=[]),
    'ngtech': dict(buildup='nttechmk', layers=[]),
}


def preview(png):
    """Pixel-level composition proof: Allied/Soviet intact, damaged, then half-built."""
    width, height = 1600, 1440
    pixels = bytearray(bytes((37, 45, 40, 255)) * (width * height))
    offsets = json.loads((ROOT / 'Assets/Generated/Sprites/layout.json').read_text())['offsets']
    cached = {}

    def sprite(name):
        if name not in cached:
            folder = OUTPUT / name
            if not (folder / 'manifest.json').exists():
                folder = ROOT / 'Assets/Generated/Sprites' / name
            doc = json.loads((folder / 'manifest.json').read_text())
            w, h, rgba = png(folder / doc['images'][0]['color'], True)
            cached[name] = (doc, w, rgba)
        return cached[name]

    def draw(name, frame, base, column, row):
        doc, iw, rgba = sprite(name)
        meta = doc['frames'][frame]
        if not meta['hasData']:
            return
        rect, source = meta['imageRect'], meta['sourceRect']
        off = offsets[base]
        left = column*320+160+source['x']-doc['canvasWidth']//2+off['x']
        top = row*240+213+source['y']-doc['canvasHeight']//2+off['y']
        for y in range(rect['height']):
            for x in range(rect['width']):
                dx, dy = left+x, top+y
                if not (column*320 <= dx < (column+1)*320 and row*240 <= dy < (row+1)*240):
                    continue
                src = ((rect['y']+y)*iw+rect['x']+x)*4
                dst = (dy*width+dx)*4
                alpha = rgba[src+3]
                if alpha:
                    for channel in range(3):
                        pixels[dst+channel] = (rgba[src+channel]*alpha+pixels[dst+channel]*(255-alpha))//255

    for row in range(6):
        side = 'gg' if row % 2 == 0 else 'ng'
        for column, kind in enumerate(('cnst', 'powr', 'pile' if side == 'gg' else 'hand', 'refn', 'weap')):
            name = side+kind
            definition = BUILDINGS[name]
            damaged = row in (2, 3)
            layers = []
            if definition.get('bib'):
                layers.append((definition['bib'], 1 if damaged else 0))
            if row >= 4:
                make = definition['buildup']
                layers.append((make, len(sprite(make)[0]['frames'])//4))
            else:
                layers.append((name, 1 if damaged else 0))
                for sequence in definition['layers']:
                    if sequence['condition'] == 'docking' or (sequence['condition'] == 'construction' and sequence['inactive'] == 'hide'):
                        continue
                    start = sequence['damagedStart'] if damaged else sequence['start']
                    phase = 0 if sequence['condition'] == 'construction' else min(2, sequence['length']-1)
                    layers.append((sequence['name'], start+phase))
            for layer_name, frame in layers:
                shadow = sprite(layer_name)[0]['frames'][frame]['pairedFrame']
                if shadow is not None:
                    draw(layer_name, shadow, name, column, row)
            for layer_name, frame in layers:
                draw(layer_name, frame, name, column, row)
    def chunk(tag, data):
        return struct.pack('>I', len(data))+tag+data+struct.pack('>I', zlib.crc32(tag+data)&0xffffffff)
    raw = b''.join(b'\0'+pixels[y*width*4:(y+1)*width*4] for y in range(height))
    data = b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))+chunk(b'IDAT', zlib.compress(raw, 9))+chunk(b'IEND', b'')
    (OUTPUT / 'building-details-preview.png').write_bytes(data)


def main():
    if not DLL.is_file():
        raise RuntimeError('Existing AssetPipeline DLL is required; this script never builds the game project.')
    spec = importlib.util.spec_from_file_location('detail_png_integrity', ROOT / 'Tools/validate_art.py')
    integrity = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(integrity)
    if sys.argv[1:] == ['--preview-only']:
        preview(integrity.png)
        return
    if sys.argv[1:]:
        raise RuntimeError('Usage: prepare_building_details.py [--preview-only]')
    retained = json.loads((REFERENCE / 'manifest-details.json').read_text())
    sources = {Path(a['original_filename']).stem: a for a in retained['assets'] if a['actual_type'] == 'SHP'}
    catalog = []
    png_count = frame_count = shadow_pixels = remap_pixels = 0
    palette_hash = hashlib.sha256(PALETTE.read_bytes()).hexdigest()
    for name, entry in sources.items():
        source = REFERENCE / entry['path']
        raw = source.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        assert digest == entry['sha256'], source
        frames = struct.unpack_from('<H', raw, 6)[0]
        assert frames % 2 == 0, source
        destination = OUTPUT / name
        manifest = destination / 'manifest.json'
        if not manifest.exists():
            command = ['dotnet', str(DLL), 'convert', '--shp', str(source), '--pal', str(PALETTE),
                       '--out', str(destination), '--layout', 'atlas', '--columns', str(min(12, frames)),
                       '--shadow-layout', 'second-half']
            result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
            if result.returncode:
                raise RuntimeError(f'{name}: {result.stderr}\n{result.stdout}')
        doc = json.loads(manifest.read_text())
        assert doc['sourceSha256'] == digest and doc['palette']['sha256'] == palette_hash, manifest
        assert doc['shadowLayout'] == 'second-half' and len(doc['frames']) == frames, manifest
        inspect = subprocess.run(['dotnet', str(DLL), 'inspect', '--shp', str(source)], cwd=ROOT,
                                 check=True, capture_output=True, text=True)
        original = json.loads(inspect.stdout)
        original_frames = original['frames']
        image = doc['images'][0]
        w, h, indices = integrity.png(destination / image['indices'], True)
        rw, rh, rgba = integrity.png(destination / image['color'], True)
        mw, mh, mask = integrity.png(destination / image['teamMask'], True)
        assert (w, h) == (rw, rh) == (mw, mh) == (image['width'], image['height']), manifest
        png_count += 3
        for index, frame in enumerate(doc['frames']):
            assert frame['index'] == index and frame['pairedFrame'] == (index + frames//2) % frames, manifest
            expected_role = 'color' if index < frames//2 else 'shadow'
            assert frame['role'] == expected_role, manifest
            original_frame = original_frames[index]
            assert frame['sourceRect'] == {key: original_frame[key] for key in ('x', 'y', 'width', 'height')}, manifest
            for key in ('hasData', 'compression', 'dataOffset', 'encodedLength', 'reservedHeaderBase64', 'indicesSha256'):
                assert frame[key] == original_frame[key], (manifest, index, key)
            rect = frame['imageRect']
            x, y, fw, fh = (rect[k] for k in ('x', 'y', 'width', 'height'))
            assert 0 <= x <= x+fw <= w and 0 <= y <= y+fh <= h, manifest
            crop = b''.join(indices[(y+row)*w+x:(y+row)*w+x+fw] for row in range(fh))
            assert hashlib.sha256(crop).hexdigest() == frame['indicesSha256'], (manifest, index)
            for row in range(fh):
                origin = (y+row)*w+x
                for column in range(fw):
                    pixel = origin+column
                    sample = indices[pixel]
                    assert mask[pixel] == (sample-15 if 16 <= sample <= 31 else 0), (name, index)
                    if index >= frames//2 and sample:
                        assert rgba[pixel*4:pixel*4+4] == bytes((0, 0, 0, 128)), (name, index)
                        shadow_pixels += 1
                    if index < frames//2 and 16 <= sample <= 31:
                        remap_pixels += 1
            frame_count += 1
        catalog.append(dict(name=name, source=entry['path'], sourceSha256=digest, frames=frames,
                            colorFrames=frames//2, manifest=f'{name}/manifest.json'))
        print(f'VERIFIED {name}: {frames} original frames', flush=True)
    available = {a['name']: a for a in catalog}
    for name, definition in BUILDINGS.items():
        assert definition['buildup'] in available, name
        for sequence in definition['layers']:
            color_frames = available[sequence['name']]['colorFrames']
            assert sequence['length'] > 0 and sequence['ticks'] > 0, sequence
            for start in (sequence['start'], sequence['damagedStart']):
                assert 0 <= start < start+sequence['length'] <= color_frames, sequence
    coordinates = json.loads((ROOT / 'Assets/Generated/Sprites/layout.json').read_text())
    art = next(a for a in retained['assets'] if a['original_filename'] == 'art.ini')
    output = dict(schema=1, sources=[dict(url=art['source_url'], httpRange=art['http_range'], sha256=art['sha256'],
                                        original='Details/art.ini'), *coordinates['sources']],
                  note='All frames retain their original SHP indices; source art.ini and published numeric sequence facts only. '
                       'Animation periods are presentation ticks, using world.Tick (20 Hz). Mechanical clocks pause on low power; '
                       'flags and generating power plants keep moving. Closed factory ramps/roofs are separate layers. '
                       'Aircraft roof-opening sequences are not used by the current ground-unit simulation.',
                  buildings=BUILDINGS, sprites=catalog)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / 'catalog.json').write_text(json.dumps(output, ensure_ascii=False, indent=2) + '\n')
    report = dict(passed=True, sourceShpFiles=len(sources), generatedAtlases=len(catalog), pngFiles=png_count,
                  originalFrames=frame_count, buildingTypes=len(BUILDINGS), originalShadowPixels=shadow_pixels,
                  originalRemapPixels=remap_pixels, paletteSha256=palette_hash,
                  missingOptional=retained.get('missing_optional', []))
    (OUTPUT / 'verification.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report), flush=True)
    preview(integrity.png)


if __name__ == '__main__':
    main()
