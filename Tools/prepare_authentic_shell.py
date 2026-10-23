#!/usr/bin/env python3
"""Decode verified authentic RA2 Bink into native PNG stills and Godot Ogg Theora.

Requires independent FFmpeg (Bink decoder, rawvideo/pipe output, swscale; optional
libtheora/Ogg encoder). No game build, Godot control, or Python image libraries.
Outputs are confined to Assets/Generated/AuthenticShell. Config/cache/temp remain
in XDG_CACHE_HOME/TMPDIR. A complete BIK must first be acquired with the companion
fetch_authentic_shell.py. Never crops/repaints/restores the original imagery.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import zlib

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Generated/AuthenticShell'
SOURCE_MANIFEST = ROOT / 'Assets/Reference/manifest-authentic-shell.json'
REV = 'acfa5b834899e7fab4c8499ccb4b49c402927b44'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write_json(path, doc):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(doc, indent=2) + '\n')


def write_png(path, width, height, rgb):
    if len(rgb) != width * height * 3:
        raise ValueError('Incorrect decoded pixel buffer size')
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    stride = width * 3
    rows = b''.join(b'\0' + rgb[y*stride:(y+1)*stride] for y in range(height))
    raw = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
    raw += chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b'')
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(raw)
    return dict(path=path.relative_to(ROOT).as_posix(), bytes=len(raw), sha256=sha(raw),
                width=width, height=height, decoded_rgb_sha256=sha(rgb))


def read_exact(stream, count):
    chunks = []
    remaining = count
    while remaining:
        chunk = stream.read(remaining)
        if not chunk:
            raise ValueError(f'Decoder ended with {remaining} missing RGB bytes')
        chunks.append(chunk)
        remaining -= len(chunk)
    return b''.join(chunks)


def find_ffmpeg(explicit):
    if explicit:
        result = Path(explicit).expanduser().resolve()
        if not result.is_file():
            raise ValueError(f'FFmpeg not found: {result}')
        return str(result)
    env = os.environ.get('FFMPEG')
    if env:
        return find_ffmpeg(env)
    result = shutil.which('ffmpeg')
    if result:
        return result
    cache = Path(os.environ.get('XDG_CACHE_HOME', Path.home() / '.cache'))
    for relative in ('redalert-authentic-shell/ffmpeg/bin/ffmpeg',
                     'redalert-authentic-shell/ffmpeg-minimal/bin/ffmpeg',
                     'redalert-authentic-shell/ffmpeg-build/ffmpeg'):
        candidate = cache / relative
        if candidate.is_file():
            return str(candidate)
    raise ValueError('No FFmpeg Bink decoder found. Supply --ffmpeg /absolute/path/to/ffmpeg; build outside the project in XDG_CACHE_HOME.')


def decode(ffmpeg, asset, header):
    path = ROOT / asset['path']
    raw = path.read_bytes()
    if not asset.get('complete_original') or len(raw) != asset['bytes'] or sha(raw) != asset['sha256']:
        raise ValueError('Complete original Bink hash verification failed')
    stem = path.stem
    destination = OUT / stem
    width, height, count = header['width'], header['height'], header['frames']
    rate = header['fps_numerator'] / header['fps_denominator']
    samples = sorted(set([0, count // 4, count // 2, 3 * count // 4, count - 1]))
    command = [ffmpeg, '-hide_banner', '-loglevel', 'error', '-nostdin', '-threads', '1',
               '-i', str(path), '-map', '0:v:0', '-an', '-vsync', '0',
               '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1']
    images, saved, frame_hashes = [], {}, []
    with tempfile.TemporaryDirectory(prefix='ra2-bink-decode-', dir=os.environ.get('TMPDIR')) as work:
        with (Path(work) / 'decoder.stderr').open('wb+') as error:
            process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=error)
            try:
                for frame in range(count):
                    rgb = read_exact(process.stdout, width * height * 3)
                    frame_hashes.append(sha(rgb))
                    if frame in samples:
                        filename = 'firstframe.png' if frame == 0 else f'frame-{frame:04d}.png'
                        image = write_png(destination / filename, width, height, rgb)
                        image.update(frame_index=frame, timestamp_seconds=frame/rate)
                        images.append(image)
                        saved[frame] = rgb
                if process.stdout.read(1):
                    raise ValueError('Decoder produced more frames than original Bink table')
                returncode = process.wait(timeout=30)
                error.seek(0)
                stderr = error.read().decode(errors='replace')
                if returncode:
                    raise ValueError(f'FFmpeg decode failed ({returncode}): {stderr}')
            except BaseException:
                process.kill()
                process.wait()
                error.seek(0)
                diagnostic = error.read().decode(errors='replace')
                if diagnostic:
                    print(diagnostic)
                raise
            finally:
                process.stdout.close()
    # Five native-resolution frames in chronological order. Empty sixth cell is
    # black and has no asserted source provenance. Original stills stay separate.
    columns, rows = 3, 2
    sheet = bytearray(width * columns * height * rows * 3)
    for cell, frame in enumerate(samples):
        x, y = cell % columns * width, cell // columns * height
        rgb = saved[frame]
        for row in range(height):
            start = ((y + row) * width * columns + x) * 3
            sheet[start:start+width*3] = rgb[row*width*3:(row+1)*width*3]
    contact = write_png(destination / 'contact-sheet.png', width * columns, height * rows, sheet)
    contact['cells'] = [{'column': i % columns, 'row': i // columns, 'frame_index': frame,
                         'timestamp_seconds': frame/rate} for i, frame in enumerate(samples)]
    write_json(destination / 'decoded-frame-hashes.json', {'format': 'rgb24', 'width': width, 'height': height,
        'frames': count, 'fps_numerator': header['fps_numerator'], 'fps_denominator': header['fps_denominator'],
        'sha256_per_frame': frame_hashes})
    return dict(source=asset, width=width, height=height, frame_count=count,
        fps_numerator=header['fps_numerator'], fps_denominator=header['fps_denominator'],
        duration_seconds=header['duration_seconds'], audio_tracks=header['audio_tracks'],
        decode_command=command, decoder_diagnostics=stderr, decoded_frame_count=len(frame_hashes),
        unique_decoded_frames=len(set(frame_hashes)), first_and_last_frame_identical=frame_hashes[0] == frame_hashes[-1],
        stills=images, contact_sheet=contact)


def probe_loop(ffmpeg, path, header):
    sibling = Path(ffmpeg).with_name('ffprobe')
    binary = str(sibling) if sibling.is_file() else shutil.which('ffprobe')
    if not binary:
        return dict(available=False, reason='ffprobe not installed beside FFmpeg or on PATH')
    command = [binary, '-v', 'error', '-count_frames', '-select_streams', 'v:0',
               '-show_streams', '-show_format', '-of', 'json', str(path)]
    result = subprocess.run(command, capture_output=True, timeout=180)
    if result.returncode:
        return dict(available=False, reason=result.stderr.decode(errors='replace'), command=command)
    metadata = json.loads(result.stdout)
    stream = metadata['streams'][0]
    if (stream.get('codec_name') != 'theora' or stream.get('width') != header['width'] or
            stream.get('height') != header['height'] or int(stream['nb_read_frames']) != header['frames']):
        raise ValueError('Independent Ogg/Theora decode differs from original dimensions/frame count')
    rate = stream['r_frame_rate'].split('/')
    if int(rate[0]) * header['fps_denominator'] != int(rate[1]) * header['fps_numerator']:
        raise ValueError('Independent Ogg/Theora frame rate differs from original')
    return dict(available=True, verified=True, command=command, metadata=metadata)


def encode_loop(ffmpeg, asset, header, quality):
    source = ROOT / asset['path']
    target = OUT / source.stem / 'menu-loop.ogv'
    command = [ffmpeg, '-hide_banner', '-loglevel', 'error', '-nostdin', '-threads', '1',
        '-i', str(source), '-map', '0:v:0', '-an', '-c:v', 'libtheora', '-q:v', str(quality),
        '-pix_fmt', 'yuv420p', '-f', 'ogg']
    with tempfile.TemporaryDirectory(prefix='ra2-theora-', dir=os.environ.get('TMPDIR')) as work:
        encoded = Path(work) / 'menu-loop.ogv'
        result = subprocess.run(command + [str(encoded)], capture_output=True, timeout=300)
        if result.returncode:
            return dict(available=False, reason=result.stderr.decode(errors='replace').strip(), attempted_command=command)
        raw = encoded.read_bytes()
        if not raw.startswith(b'OggS') or b'\x80theora' not in raw[:512]:
            raise ValueError('Encoded loop does not have Ogg/Theora signatures')
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
    return dict(available=True, path=target.relative_to(ROOT).as_posix(), bytes=len(raw), sha256=sha(raw),
        format='Ogg Theora', encoder='libtheora', quality=quality,
        encoding='Lossy transcode of all original Bink frames; native resolution/timing; no audio or visual overlays.',
        frame_count=header['frames'], command=command + [str(target)],
        independent_probe=probe_loop(ffmpeg, target, header),
        godot_usage='VideoStreamPlayer.stream = load("res://' + target.relative_to(ROOT).as_posix() + '"); loop = true; autoplay = true. Set expand=true and use aspect-preserving layout.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ffmpeg', help='independent native FFmpeg binary')
    parser.add_argument('--variant', choices=('large', 'small', 'both'), default='large')
    parser.add_argument('--no-loop', action='store_true', help='only decode verified PNG stills')
    parser.add_argument('--theora-quality', type=int, choices=range(0, 11), default=9)
    args = parser.parse_args()
    ffmpeg = find_ffmpeg(args.ffmpeg)
    version = subprocess.run([ffmpeg, '-version'], check=True, capture_output=True).stdout.decode()
    source = json.loads(SOURCE_MANIFEST.read_text())
    if source['source_revision'] != REV:
        raise ValueError('Source revision mismatch')
    selected = {'large': ['ra2ts_l.bik'], 'small': ['ra2ts_s.bik'], 'both': ['ra2ts_l.bik', 'ra2ts_s.bik']}[args.variant]
    outputs = []
    for name in selected:
        asset = next((a for a in source['assets'] if a['original_filename'] == name), None)
        if asset is None:
            raise ValueError(f'{name} is not acquired. Run fetch_authentic_shell.py --variant {args.variant}')
        header = source['probes'][name]
        output = decode(ffmpeg, asset, header)
        if not args.no_loop:
            output['godot_loop'] = encode_loop(ffmpeg, asset, header, args.theora_quality)
            loop = output['godot_loop']
            if loop['available']:
                print('OGV', loop['path'], loop['bytes'], 'bytes;', loop['sha256'], flush=True)
            else:
                print('OGV unavailable:', loop['reason'], flush=True)
        outputs.append(output)
        print(name, output['decoded_frame_count'], 'decoded frames;', len(output['stills']), 'native stills', flush=True)
    doc = dict(schema=1, source_revision=REV, source_manifest=SOURCE_MANIFEST.relative_to(ROOT).as_posix(),
        decoder=dict(path=ffmpeg, binary_sha256=sha(Path(ffmpeg).read_bytes()), version=version), outputs=outputs,
        visual_policy='Authentic original Bink imagery decoded independently. No PCX substitutes, repainting, scaling, button overlays, or generated art.',
        visual_inspection='See inspection.json/README.md produced after reading actual decoded images.',
        loop_policy='All original frames in order. VideoStreamPlayer looping repeats the complete original movie; no seam smoothing or retiming is applied.')
    write_json(OUT / 'manifest.json', doc)
    print('Output manifest:', (OUT / 'manifest.json').relative_to(ROOT), flush=True)


if __name__ == '__main__':
    main()
