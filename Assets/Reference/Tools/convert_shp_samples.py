#!/usr/bin/env python3
"""Decode original SHP data to Godot-ready PNGs without GUI or image packages.
This is an independently written implementation of the documented SHP format:
https://github.com/tcrs/ra2ff/blob/master/ra2ff.txt
Pixels come only from original SHP/PAL inputs; no generated replacement artwork.
"""
import hashlib
import json
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]


def decode(path):
    data = path.read_bytes()
    zero, canvas_w, canvas_h, count = struct.unpack_from('<4H', data)
    assert zero == 0 and count > 0
    frames = []
    for frame in range(count):
        start = 8 + frame * 24
        x, y, width, height = struct.unpack_from('<4H', data, start)
        compression = data[start + 8]
        offset = struct.unpack_from('<I', data, start + 20)[0]
        canvas = bytearray(canvas_w * canvas_h)
        if width == 0 or height == 0 or offset == 0:
            frames.append(canvas)
            continue
        assert x + width <= canvas_w and y + height <= canvas_h
        if compression in (0, 1):
            pixels = data[offset:offset + width * height]
            assert len(pixels) == width * height
            for row in range(height):
                canvas[(y + row) * canvas_w + x:(y + row) * canvas_w + x + width] = pixels[row * width:(row + 1) * width]
        elif compression == 2:
            row_length = struct.unpack_from('<H', data, offset)[0] - 2
            pos = offset + 2
            for row in range(height):
                pixels = data[pos:pos + row_length]
                pos += row_length
                assert len(pixels) == row_length
                dest = (y + row) * canvas_w + x
                canvas[dest:dest + min(width, row_length)] = pixels[:width]
        elif compression == 3:
            pos = offset
            for row in range(height):
                size = struct.unpack_from('<H', data, pos)[0]
                end = pos + size
                pos += 2
                pixels = bytearray()
                while pos < end:
                    value = data[pos]
                    pos += 1
                    if value == 0:
                        assert pos < end
                        pixels.extend(b'\0' * data[pos])
                        pos += 1
                    else:
                        pixels.append(value)
                # Some retail rows terminate with a zero run one pixel beyond
                # the advertised rectangle. The engine clips this padding.
                assert not any(pixels[width:]), (path.name, frame, row)
                pixels = pixels[:width]
                dest = (y + row) * canvas_w + x
                canvas[dest:dest + len(pixels)] = pixels
        else:
            raise ValueError(f'Unsupported compression {compression}')
        frames.append(canvas)
    return canvas_w, canvas_h, frames


def png_bytes(width, height, pixels, palette, transparent):
    assert len(palette) == 768 and len(pixels) == width * height
    # Westwood PAL components use 6 bits; expand by shifting left two bits.
    palette = bytes(value * 4 for value in palette)
    def chunk(kind, body):
        return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body))
    scanlines = b''.join(b'\0' + pixels[y * width:(y + 1) * width] for y in range(height))
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 3, 0, 0, 0))
    data += chunk(b'PLTE', palette)
    if transparent:
        # Index zero is transparent. Index one is the game's shadow color.
        data += chunk(b'tRNS', bytes([0, 128]) + bytes([255]) * 254)
    return data + chunk(b'IDAT', zlib.compress(scanlines, 9)) + chunk(b'IEND', b'')


def save(source, output, width, height, pixels, palette_path, transparent, usage, **extra):
    blob = png_bytes(width, height, pixels, palette_path.read_bytes(), transparent)
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists() and output.read_bytes() != blob:
        raise RuntimeError(f'Refusing to replace modified PNG {output}')
    output.write_bytes(blob)
    return dict(path=output.relative_to(ROOT).as_posix(), bytes=len(blob),
        sha256=hashlib.sha256(blob).hexdigest(), actual_type='PNG',
        width=width, height=height, derived_from=source.relative_to(ROOT).as_posix(),
        palette=palette_path.relative_to(ROOT).as_posix(), usage=usage, **extra)


def main():
    manifest_path = ROOT / 'manifest.json'
    manifest = json.loads(manifest_path.read_text())
    derived = []
    unit_palette = ROOT / 'Formats/unittem.pal'
    cameo_palette = ROOT / 'Formats/cameo.pal'
    for source in sorted((ROOT / 'Formats/Cameos').glob('*.shp')):
        width, height, frames = decode(source)
        derived.append(save(source, ROOT / 'Cameos' / (source.stem + '.png'),
            width, height, frames[0], cameo_palette, False, 'sidebar_cameo'))
    for source in sorted((ROOT / 'Battlefield/Buildings').glob('*.shp')):
        width, height, frames = decode(source)
        derived.append(save(source, ROOT / 'Exported/Buildings' / (source.stem + '.png'),
            width, height, frames[0], unit_palette, True, 'battlefield_building_frame', source_frame=0))
    for source in sorted((ROOT / 'Battlefield/Infantry').glob('*.shp')):
        width, height, frames = decode(source)
        columns = 16
        rows = (len(frames) + columns - 1) // columns
        atlas_w = columns * width
        atlas_h = rows * height
        atlas = bytearray(atlas_w * atlas_h)
        for frame, pixels in enumerate(frames):
            tile_x, tile_y = frame % columns * width, frame // columns * height
            for row in range(height):
                pos = (tile_y + row) * atlas_w + tile_x
                atlas[pos:pos + width] = pixels[row * width:(row + 1) * width]
        derived.append(save(source, ROOT / 'Exported/Infantry' / (source.stem + '.png'),
            atlas_w, atlas_h, atlas, unit_palette, True, 'battlefield_infantry_atlas',
            frame_width=width, frame_height=height, frame_count=len(frames), columns=columns))
    known = {item['path'] for item in derived}
    manifest['assets'] = [item for item in manifest['assets'] if item['path'] not in known] + derived
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    print('Decoded PNG assets', len(derived), 'bytes', sum(item['bytes'] for item in derived))
    print(json.dumps([dict(path=item['path'], width=item['width'], height=item['height']) for item in derived]))


if __name__ == '__main__':
    main()
