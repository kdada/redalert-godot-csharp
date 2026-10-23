#!/usr/bin/env python3
"""Decode original menu PCX/SHP art without repainting or synthesizing imagery.

PCX and PNG handling uses the Python standard library. SHP decoding delegates to
the already-built managed AssetPipeline DLL; this script never builds the game.
Every image is validated numerically; no visual inspection is claimed.
"""
import collections
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import zlib

ROOT = Path(__file__).resolve().parents[1]
REFERENCE = ROOT / 'Assets/Reference/Menu'
OUT = ROOT / 'Assets/Generated/Menu'
MANIFEST = ROOT / 'Assets/Reference/manifest-menu.json'
MENU_RECOLOR_POLICY = 'Use original menu palette colors directly. The generic SHP decoder emits index-16..31 masks as diagnostics; those masks do not establish house/team remapping for menu art.'
DLL = Path(os.environ['XDG_CACHE_HOME']) / 'redalert-godot-csharp/asset-pipeline/bin/AssetPipeline/debug/Ra2AssetPipeline.dll'


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def write_json(path, doc):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(doc, indent=2) + '\n')


def write_png(path, width, height, pixels, channels):
    assert channels in (1, 4) and len(pixels) == width * height * channels
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    stride = width * channels
    raw = b''.join(b'\0' + pixels[y*stride:(y+1)*stride] for y in range(height))
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 0 if channels == 1 else 6, 0, 0, 0))
    data += chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


def read_png(path):
    data = path.read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', path
    position, compressed, end = 8, bytearray(), False
    header = None
    while position < len(data):
        length = struct.unpack_from('>I', data, position)[0]
        kind = data[position+4:position+8]
        payload = data[position+8:position+8+length]
        assert len(payload) == length
        assert zlib.crc32(kind + payload) & 0xffffffff == struct.unpack_from('>I', data, position+8+length)[0], path
        if kind == b'IHDR':
            assert header is None
            header = struct.unpack('>IIBBBBB', payload)
        elif kind == b'IDAT':
            compressed.extend(payload)
        position += length + 12
        if kind == b'IEND':
            assert not payload
            end = True
            break
    assert end and position == len(data) and header
    width, height, bits, color, compression, filtering, interlace = header
    assert bits == 8 and color in (0, 6) and (compression, filtering, interlace) == (0, 0, 0), path
    channels = 1 if color == 0 else 4
    stride = width * channels
    raw = zlib.decompress(compressed)
    assert len(raw) == height * (stride + 1), path
    rows = []
    for y in range(height):
        start = y * (stride + 1)
        assert raw[start] == 0, path
        rows.append(raw[start+1:start+1+stride])
    return width, height, channels, b''.join(rows)


def decode_pcx(data):
    assert len(data) >= 128 and data[0] == 10, 'Invalid PCX manufacturer'
    version, encoding, bits = data[1:4]
    xmin, ymin, xmax, ymax = struct.unpack_from('<4H', data, 4)
    width, height = xmax-xmin+1, ymax-ymin+1
    planes = data[65]
    bytes_per_line, palette_info = struct.unpack_from('<2H', data, 66)
    assert 0 < width <= 4096 and 0 < height <= 4096
    assert bits == 8 and planes in (1, 3) and encoding in (0, 1), (bits, planes, encoding)
    assert bytes_per_line >= width
    palette = data[-768:] if planes == 1 and len(data) >= 897 and data[-769] == 12 else None
    if planes == 1:
        assert palette is not None, 'Indexed PCX requires its original 256-color trailer palette'
    limit = len(data) - 769 if palette is not None else len(data)
    target = height * planes * bytes_per_line
    decoded = bytearray()
    cursor = 128
    while len(decoded) < target:
        assert cursor < limit, 'Truncated PCX pixels'
        value = data[cursor]
        cursor += 1
        count = 1
        if encoding and value >= 192:
            count = value & 63
            assert count and cursor < limit
            value = data[cursor]
            cursor += 1
        assert len(decoded) + count <= target, 'PCX RLE exceeds declared scanlines'
        decoded.extend(bytes([value]) * count)
    indices, rgba = bytearray(), bytearray()
    for y in range(height):
        row = y * bytes_per_line * planes
        if planes == 1:
            samples = decoded[row:row+width]
            indices.extend(samples)
            for sample in samples:
                rgba.extend(palette[sample*3:sample*3+3])
                rgba.append(255)
        else:
            for x in range(width):
                rgba.extend([decoded[row+x], decoded[row+bytes_per_line+x], decoded[row+2*bytes_per_line+x], 255])
    return width, height, bytes(rgba), bytes(indices), palette, dict(
        pcx_version=version, encoding='rle' if encoding else 'raw', bits_per_plane=bits,
        planes=planes, bytes_per_line=bytes_per_line, palette_info=palette_info,
        original_header_origin=[xmin, ymin], encoded_pixels_end=cursor,
        bytes_before_palette_after_pixels=limit-cursor)


def pixel_stats(width, height, rgba):
    colors = collections.Counter(tuple(rgba[i:i+4]) for i in range(0, len(rgba), 4))
    alpha = collections.Counter(rgba[3::4])
    xs, ys = [], []
    for y in range(height):
        for x in range(width):
            r, g, b, a = rgba[(y*width+x)*4:(y*width+x+1)*4]
            if a and (r or g or b):
                xs.append(x)
                ys.append(y)
    return dict(unique_rgba=len(colors), alpha_histogram=dict(sorted(alpha.items())),
        rgba_sha256=sha(rgba), most_common_rgba=[{'rgba': list(color), 'pixels': count} for color, count in colors.most_common(6)],
        nonblack_bounds=[min(xs), min(ys), max(xs)-min(xs)+1, max(ys)-min(ys)+1] if xs else None)


def provenance(asset):
    return {key: asset[key] for key in ('path', 'original_filename', 'sha256', 'source_archive', 'nested_archives', 'archive_offset', 'ranges')}


def parse_ui(path):
    text = path.read_bytes().decode('cp1252')
    sections = {}
    section = None
    comments = []
    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.strip()
        if line.startswith(';'):
            comments.append({'line': number, 'text': line[1:].strip()})
            continue
        if not line:
            continue
        if line.startswith('[') and ']' in line:
            section = line[1:line.index(']')]
            sections.setdefault(section, [])
        elif section is not None and '=' in line:
            key, value = line.split('=', 1)
            # Preserve the literal value and source line, including comments.
            sections[section].append({'key': key.strip(), 'value': value.strip(), 'line': number})
    return {'source': path.relative_to(ROOT).as_posix(), 'sha256': sha(path.read_bytes()),
            'coordinate_policy': 'Only literal original INI values are facts; no screen positions are inferred from artwork.',
            'sections': sections, 'comments': comments}


def main():
    document = json.loads(MANIFEST.read_text())
    assets = document['assets']
    for asset in assets:
        original = ROOT / asset['path']
        assert original.is_file() and sha(original.read_bytes()) == asset['sha256'], original
    OUT.mkdir(parents=True, exist_ok=True)
    catalog = {'schema': 1, 'source_revision': document['source_revision'],
               'provenance_manifest': 'res://Assets/Reference/manifest-menu.json',
               'menu_recolor_policy': MENU_RECOLOR_POLICY,
               'validation_policy': 'Numerical validation only: original SHA-256, PCX RLE/palettes, PNG CRC/structure and pixel equality. No visual inspection performed.',
               'images': [], 'palettes': [], 'shp': [], 'audit': []}
    palettes = {a['original_filename']: ROOT / a['path'] for a in assets if a['actual_type'] == 'PAL'}
    for name, path in sorted(palettes.items()):
        raw = path.read_bytes()
        assert len(raw) == 768 and max(raw) <= 63
        catalog['palettes'].append({'original_filename': name, 'source': path.relative_to(ROOT).as_posix(),
            'sha256': sha(raw), 'colors': 256, 'channel_bits': 6, 'max_original_component': max(raw),
            'expansion': 'channel << 2 (0..252)'})
    for asset in assets:
        if asset['actual_type'] != 'PCX':
            continue
        original = ROOT / asset['path']
        width, height, rgba, indices, palette, details = decode_pcx(original.read_bytes())
        color_file = OUT / 'Pcx' / (original.stem + '.png')
        write_png(color_file, width, height, rgba, 4)
        assert read_png(color_file) == (width, height, 4, rgba)
        entry = {'name': original.stem, 'image': 'res://' + color_file.relative_to(ROOT).as_posix(),
                 'width': width, 'height': height, 'source': provenance(asset),
                 'alpha_policy': 'Fully opaque: original PCX contains no alpha channel; black is preserved.',
                 'pcx': details, 'pixels': pixel_stats(width, height, rgba),
                 'png_sha256': sha(color_file.read_bytes())}
        if palette is not None:
            indices_file = color_file.with_name(original.stem + '.indices.png')
            write_png(indices_file, width, height, indices, 1)
            assert read_png(indices_file) == (width, height, 1, indices)
            # Independently verify every decoded output pixel from retained source indices and embedded RGB8 palette.
            for i, sample in enumerate(indices):
                assert rgba[i*4:i*4+4] == palette[sample*3:sample*3+3] + b'\xff'
            entry.update(indices_image='res://' + indices_file.relative_to(ROOT).as_posix(),
                indices_sha256=sha(indices), unique_indices=len(set(indices)),
                embedded_palette_sha256=sha(palette), palette_component_bits=8,
                palette_matching_external_files=[name for name, path in palettes.items() if palette == bytes(v << 2 for v in path.read_bytes())])
        catalog['images'].append(entry)
        print('PCX', original.name, width, height, 'colors', entry['pixels']['unique_rgba'], flush=True)
    # Dedicated original filename pairs provide palette candidates. Keep the
    # evidence explicit: the INI may not establish a palette for legacy SHPs.
    assignments = {'mnbttn.shp': 'mainbttn.pal', 'glsl.shp': 'gls.pal', 'glss.shp': 'gls.pal',
                   'pudlgbga.shp': 'dialog.pal', 'pudlgbgn.shp': 'dialog.pal', 'pudlgbgs.shp': 'dialog.pal'}
    for asset in assets:
        if asset['actual_type'] != 'SHP':
            continue
        original = ROOT / asset['path']
        palette_name = assignments.get(original.name)
        if palette_name is None:
            catalog['audit'].append({'source': provenance(asset), 'status': 'Not converted: no palette evidence'})
            continue
        palette = palettes[palette_name]
        destination = OUT / 'Shp' / original.stem
        output_manifest = destination / 'manifest.json'
        if not output_manifest.exists():
            assert DLL.is_file(), 'Existing managed AssetPipeline DLL is required; no build is performed'
            command = ['dotnet', str(DLL), 'convert', '--shp', str(original), '--pal', str(palette),
                       '--out', str(destination), '--layout', 'atlas', '--shadow-layout', 'unspecified']
            result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
            if result.returncode:
                raise RuntimeError(f'{original.name}: {result.stderr}\n{result.stdout}')
        shp = json.loads(output_manifest.read_text())
        assert shp['sourceSha256'] == asset['sha256'] and shp['palette']['sha256'] == sha(palette.read_bytes())
        shp_raw = original.read_bytes()
        magic, canvas_w, canvas_h, count = struct.unpack_from('<4H', shp_raw)
        assert magic == 0 and (canvas_w, canvas_h, count) == (shp['canvasWidth'], shp['canvasHeight'], len(shp['frames']))
        images = []
        colors = shp['palette']['colors']
        for image in shp['images']:
            w, h, c, indices = read_png(destination / image['indices'])
            cw, ch, cc, rgba = read_png(destination / image['color'])
            assert (w, h, c) == (image['width'], image['height'], 1) and (cw, ch, cc) == (w, h, 4)
            read_png(destination / image['teamMask'])
            images.append((w, h, indices, rgba))
        for frame in shp['frames']:
            w, h, samples, rgba = images[frame['imageSet']]
            rect = frame['imageRect']
            x, y, fw, fh = (rect[key] for key in ('x', 'y', 'width', 'height'))
            assert 0 <= x <= x+fw <= w and 0 <= y <= y+fh <= h
            crop = b''.join(samples[(y+row)*w+x:(y+row)*w+x+fw] for row in range(fh))
            assert sha(crop) == frame['indicesSha256']
            header = 8 + frame['index'] * 24
            source_x, source_y, source_w, source_h = struct.unpack_from('<4H', shp_raw, header)
            source_format = shp_raw[header+8]
            source_offset = struct.unpack_from('<I', shp_raw, header+20)[0]
            source_rect = frame['sourceRect']
            assert (source_x, source_y, source_w, source_h) == tuple(source_rect[key] for key in ('x', 'y', 'width', 'height'))
            assert (source_offset, source_format) == (frame['dataOffset'], frame['compression'])
            # All six selected menu SHPs store raw pixels (format 0 or 1).
            # Compare source bytes independently of the managed decoder/hash.
            assert source_format in (0, 1), 'A compressed source needs an independent source-pixel check'
            if source_offset:
                assert crop == shp_raw[source_offset:source_offset+fw*fh]
            else:
                assert not any(crop)
            if frame['hasData']:
                for row in range(fh):
                    for column in range(fw):
                        sample = samples[(y+row)*w+x+column]
                        color = colors[sample]
                        pixel = bytes([color['r'], color['g'], color['b'], 0 if sample == shp['transparentIndex'] else 255])
                        pos = ((y+row)*w+x+column)*4
                        assert rgba[pos:pos+4] == pixel
        catalog['shp'].append({'name': original.stem, 'source': provenance(asset),
            'manifest': 'res://' + output_manifest.relative_to(ROOT).as_posix(),
            'image': 'res://' + (destination / shp['images'][0]['color']).relative_to(ROOT).as_posix(),
            'width': shp['canvasWidth'], 'height': shp['canvasHeight'], 'frames': len(shp['frames']),
            'palette': palette_name,
            'atlas_width': images[0][0], 'atlas_height': images[0][1],
            'frame_regions': [{key: frame[key] for key in ('index', 'sourceRect', 'centerOffset', 'imageRect', 'indicesSha256')} for frame in shp['frames']],
            'pixels': pixel_stats(images[0][0], images[0][1], images[0][3]),
            'palette_evidence': 'Candidate original palette selected by dedicated stem/function; this is not an explicit per-image UI.INI palette binding.',
            'alpha_policy': 'Original SHP palette index 0 is transparent; no shadow inference or recoloring.'})
        print('SHP', original.name, shp['canvasWidth'], shp['canvasHeight'], len(shp['frames']), flush=True)
    ui = next(a for a in assets if a['original_filename'] == 'ui.ini')
    ui_facts = parse_ui(ROOT / ui['path'])
    write_json(OUT / 'ui-facts.json', ui_facts)
    catalog['original_ui_facts'] = 'res://Assets/Generated/Menu/ui-facts.json'
    coordinates = {'schema': 1,
        'units': 'Pixels; top-left origin within the original image canvas.',
        'menu_screen_positions_available': False,
        'menu_screen_positions_evidence': 'The retained original UI.INI contains two command-bar button lists only, with no menu/dialog rectangles or coordinates. No game executable resources were acquired.',
        'pcx_original_canvas_facts': [{'original_filename': item['source']['original_filename'],
            'image': item['image'], 'width': item['width'], 'height': item['height'],
            'header_origin': item['pcx']['original_header_origin'],
            'source_sha256': item['source']['sha256']} for item in catalog['images']],
        'shp_original_canvas_and_frame_facts': [{'original_filename': item['source']['original_filename'],
            'image': item['image'], 'canvas_width': item['width'], 'canvas_height': item['height'],
            'atlas_width': item['atlas_width'], 'atlas_height': item['atlas_height'],
            'frames': item['frame_regions'], 'source_sha256': item['source']['sha256']} for item in catalog['shp']],
        'frame_policy': 'SHP sourceRect and centerOffset are original source geometry. imageRect is a generated atlas packing coordinate. Frame meanings/state order are not inferred.'}
    write_json(OUT / 'coordinate-facts.json', coordinates)
    catalog['original_coordinate_facts'] = 'res://Assets/Generated/Menu/coordinate-facts.json'
    catalog['confirmed_shipped_ra2_main_menu_background'] = False
    catalog['confirmed_shipped_ra2_main_menu_logo'] = False
    catalog['audit'] += [
        {'finding': 'gmenu.mix and gmenumd.mix are absent as entries in the probed language.mix and ra2.mix roots; available original root filenames and IDs are recorded in the reference manifest.'},
        {'finding': 'Original PCXs retain their own RGB8 palettes and are exported opaque. SHPs retain indices plus separate original VGA6 palettes. No colors, textures, logos or menu backgrounds are painted or synthesized.'},
        {'finding': 'A filename in the original archive does not by itself establish that the shipped RA2 main menu used it. Resource roles require original UI.INI evidence or should remain unclassified.'}]
    write_json(OUT / 'catalog.json', catalog)
    resource_audit = {'schema': 1, 'source_revision': document['source_revision'],
        'probed_containers': {key: {field: audit[field] for field in ('entries', 'absolute_archive_offset', 'available_names', 'cross_database_name_matches')}
                              for key, audit in document['audit'].items()},
        'source_repository_tree_url': 'https://api.github.com/repos/toarrntnz/ts-redalert2-assets/git/trees/' + document['source_revision'] + '?recursive=1',
        'source_repository_tree_observation': 'The pinned non-truncated tree contains no standalone gmenu.mix or gmenumd.mix. Its root data files include language.mix, multi.mix, ra2.mix.part00..04, glsl.png, manifest.json and ra2ts_l.webm. The converted PNG and video were not downloaded.',
        'identified_excluded_payloads': document['excluded_identified_payloads'],
        'notes': document['audit_notes'],
        'main_menu_background_and_logo_status': 'No recovered static image is asserted to be the shipped RA2 main-menu background or logo; filenames alone do not prove usage.',
        'pcx_color_status': 'Exact original embedded RGB8 palettes; fully opaque original source pixels.',
        'shp_color_status': 'Original VGA6 palettes selected by filename/function as explicit candidates; palette choice remains reviewable. Original grayscale indices are retained independently.',
        'coordinate_status': coordinates['menu_screen_positions_evidence'],
        'useful_original_resources': {
            'shell_button_source_names': ['shellbttnoff.pcx', 'shellbttnon.pcx', 'shellbttnpress.pcx'],
            'shell_button_canvas': [163, 37],
            'mnbttn_canvas_and_frames': [126, 25, 3],
            'pudlgbg_canvas': [451, 326],
            'glsl_canvas': [800, 600], 'glss_canvas': [640, 480],
            'dbak6440_canvas': [640, 400],
            'additional_controls': 'Original button edge/middle segments, tab borders, scrollbar and arrow PCXs are listed individually in catalog.json.'},
        'visual_inspection_performed': False}
    write_json(OUT / 'resource-audit.json', resource_audit)
    png_files = sorted(OUT.rglob('*.png'))
    for path in png_files:
        read_png(path)
    report = {'passed': True, 'original_asset_hashes_verified': len(assets),
              'pcx_images': len(catalog['images']), 'shp_sets': len(catalog['shp']),
              'shp_frames_original_bytes_verified': sum(item['frames'] for item in catalog['shp']),
              'png_files_crc_structure_pixels_verified': len(png_files),
              'ui_sections': len(ui_facts['sections']),
              'retained_exact_range_bytes': document['retained_range_bytes'],
              'visual_inspection': False}
    write_json(OUT / 'validation.json', report)
    print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
