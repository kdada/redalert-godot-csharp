#!/usr/bin/env python3
"""Acquire pinned original RA2 title-screen Bink data with exact HTTP 206 ranges.

Writes project data only to Assets/Reference/AuthenticShell and its new manifest.
Transient downloads/build caches use TMPDIR/XDG_CACHE_HOME. Never downloads a
whole MIX, executable, speech, or unrelated movie. Default: full 7.2 MB L movie;
--probe only reads the two Bink headers and frame tables. Uses the existing MIX
reader with a read-only, SHA-verified cache for the encrypted 164-byte index.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import struct
import sys
import tempfile
import time
import urllib.error
import urllib.request

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'Assets/Reference/AuthenticShell'
MANIFEST = ROOT / 'Assets/Reference/manifest-authentic-shell.json'
REV = 'acfa5b834899e7fab4c8499ccb4b49c402927b44'
URL = f'https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/{REV}/game-res/language.mix'
ARCHIVE_BYTES = 53_116_040
MAX_TRANSFER = 40_000_000
KNOWN = {'ra2ts_l.bik': (37_942_628, 7_184_828), 'ra2ts_s.bik': (45_127_460, 7_184_988)}
EXPECTED_SHA256 = {'ra2ts_l.bik': '4c73d2b9c5dc1dccab156edf7a48bda9ae11928fa97907c4b20e304d2da7d2a6'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def save_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n')


def retain_original(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if path.read_bytes() != data:
            raise ValueError(f'Refusing to overwrite differing original: {path}')
    else:
        path.write_bytes(data)


def bink_header(data, expected_size):
    if len(data) < 44 or data[:3] != b'BIK':
        raise ValueError('Expected original Bink 1 signature')
    size, frames, largest, duplicate, width, height, num, den, flags, audio = struct.unpack_from('<10I', data, 4)
    if size + 8 != expected_size or duplicate != frames or not (0 < frames < 100_000 and 0 < width <= 4096 and 0 < height <= 4096 and num and den and audio < 16):
        raise ValueError('Unexpected Bink header bounds or MIX/Bink size disagreement')
    index_start = 44 + 12 * audio
    index_end = index_start + 4 * (frames + 1)
    doc = dict(signature=data[:4].decode('ascii'), declared_file_bytes=size + 8,
        frames=frames, duplicate_frames=duplicate, largest_frame_bytes=largest,
        width=width, height=height, fps_numerator=num, fps_denominator=den,
        fps=num / den, duration_seconds=frames * den / num, flags=flags,
        audio_tracks=audio, frame_index_offset=index_start, frame_index_end=index_end)
    if len(data) >= index_end:
        raw_offsets = struct.unpack_from(f'<{frames+1}I', data, index_start)
        offsets = [value & ~1 for value in raw_offsets]
        if not (index_end <= offsets[0] and offsets[-1] == expected_size and
                all(a < b for a, b in zip(offsets, offsets[1:]))):
            raise ValueError('Invalid Bink frame index boundaries')
        if max(b - a for a, b in zip(offsets, offsets[1:])) != largest:
            raise ValueError('Largest Bink frame does not match header')
        doc['frame_offsets'] = offsets
        doc['keyframe_indices'] = [i for i, value in enumerate(raw_offsets[:-1]) if value & 1]
        doc['first_frame_bytes'] = offsets[1] - offsets[0]
        doc['first_keyframe_index'] = next(iter(doc['keyframe_indices']), None)
    return doc


class Acquisition:
    def __init__(self):
        cache_root = Path(os.environ.get('XDG_CACHE_HOME', Path.home() / '.cache'))
        self.cache = cache_root / 'redalert-authentic-shell' / REV / 'ranges'
        self.cache.mkdir(parents=True, exist_ok=True)
        self.transferred = 0
        self.used = {}
        self.doc = json.loads(MANIFEST.read_text()) if MANIFEST.exists() else {
            'schema': 1, 'source_revision': REV, 'source_archive_url': URL,
            'source_archive_bytes': ARCHIVE_BYTES,
            'method': 'Exact validated HTTP 206 asset/index ranges only; no whole archive or game implementation.',
            'maximum_transfer_bytes_per_run': MAX_TRANSFER, 'ranges': [], 'assets': [], 'probes': {}}
        if self.doc['source_revision'] != REV:
            raise ValueError('Manifest source pin differs')
        old = ROOT / 'Assets/Reference/manifest-menu.json'
        self.old = json.loads(old.read_text()) if old.exists() else {}
        if self.old and self.old.get('source_revision') != REV:
            raise ValueError('Existing menu manifest source pin differs')

    def save(self):
        previous = {r['http_range']: r for r in self.doc.get('ranges', [])}
        previous.update(self.used)
        self.doc['ranges'] = list(previous.values())
        self.doc['last_run_transferred_bytes'] = self.transferred
        save_json(MANIFEST, self.doc)

    def fetch(self, archive, offset, count):
        if archive != 'language.mix' or not (0 <= offset < offset + count <= ARCHIVE_BYTES) or count > 1_048_576:
            raise ValueError('Only bounded language.mix ranges <=1 MiB permitted')
        range_value = f'bytes={offset}-{offset+count-1}'
        for record in self.old.get('ranges', []):
            if (record.get('source_archive') == archive and record.get('archive_offset') == offset and
                    record['bytes'] == count and (ROOT / record['cache_path']).is_file()):
                data = (ROOT / record['cache_path']).read_bytes()
                if len(data) != count or sha(data) != record['sha256']:
                    raise ValueError('Existing exact range hash mismatch')
                copied = dict(record)
                copied['reused_from_manifest'] = 'Assets/Reference/manifest-menu.json'
                self.used[range_value] = copied
                return data
        cached = self.cache / f'{offset}.{count}.bin'
        metadata = self.cache / f'{offset}.{count}.json'
        if cached.exists() and metadata.exists():
            record = json.loads(metadata.read_text())
            data = cached.read_bytes()
            if (len(data) != count or sha(data) != record['sha256'] or
                    record.get('source_url') != URL or record.get('http_range') != range_value or
                    record.get('content_range') != f'bytes {offset}-{offset+count-1}/{ARCHIVE_BYTES}' or
                    record.get('http_status') != 206):
                raise ValueError('Cached exact range metadata/hash mismatch')
        else:
            if self.transferred + count > MAX_TRANSFER:
                raise ValueError('40 MB maximum transfer budget would be exceeded')
            request_url = URL + f'?authentic-shell-range={offset}-{count}'
            request = urllib.request.Request(request_url, headers={'Range': range_value, 'Accept-Encoding': 'identity'})
            for attempt in range(3):
                try:
                    with urllib.request.urlopen(request, timeout=45) as response:
                        content_range = response.headers.get('Content-Range', '')
                        if response.status != 206 or content_range != f'bytes {offset}-{offset+count-1}/{ARCHIVE_BYTES}':
                            raise RuntimeError('Server did not honor the exact HTTP 206 range; no fallback download')
                        data = response.read(count + 1)
                        if len(data) != count:
                            raise RuntimeError('Incorrect response size')
                        etag = response.headers.get('ETag')
                    break
                except (urllib.error.URLError, TimeoutError):
                    if attempt == 2:
                        raise
                    time.sleep(attempt + 1)
            self.transferred += count
            record = dict(source_archive=archive, source_url=URL, request_url=request_url,
                archive_offset=offset, bytes=count, http_range=range_value,
                content_range=content_range, http_status=206, etag=etag, sha256=sha(data),
                cache_path=str(cached))
            # OS temp storage is explicitly outside the HOME/hmdfs workspace.
            with tempfile.TemporaryDirectory(prefix='ra2-shell-', dir=os.environ.get('TMPDIR')) as work:
                temp = Path(work) / 'range.bin'
                temp.write_bytes(data)
                cached.write_bytes(temp.read_bytes())
            save_json(metadata, record)
        self.used[range_value] = record
        self.save()
        return data

    def probe(self, name, offset, size):
        prefix = self.fetch('language.mix', offset, 44)
        header = bink_header(prefix, size)
        rest = self.fetch('language.mix', offset + 44, header['frame_index_end'] - 44)
        raw = prefix + rest
        header = bink_header(raw, size)
        relative = f'Assets/Reference/AuthenticShell/Metadata/{name}.header-and-index.bin'
        retain_original(ROOT / relative, raw)
        save_json(DEST / 'Metadata' / f'{name}.frames.json', header)
        self.doc['probes'][name] = dict(archive_offset=offset, bytes=size,
            bounded_first_frame_prefix_bytes=header['frame_offsets'][1],
            bounded_first_frame_http_range=f"bytes={offset}-{offset+header['frame_offsets'][1]-1}",
            filename_crc32=f'{reader.filename_hash(name):08x}', header_path=relative,
            header_sha256=sha(raw), **header)
        self.save()
        print(name, size, 'bytes;', header['width'], 'x', header['height'],
              header['frames'], 'frames;', header['fps'], 'fps;', header['duration_seconds'], 'seconds', flush=True)
        return header

    def acquire(self, name, offset, size):
        if size > 40_000_000:
            raise ValueError('Original movie exceeds the authorized full-asset size cap')
        path = DEST / 'Original' / name
        old = next((a for a in self.doc['assets'] if a['original_filename'] == name), None)
        if path.exists() and old:
            raw = path.read_bytes()
            if len(raw) != size or sha(raw) != old['sha256']:
                raise ValueError('Existing original movie hash mismatch')
        else:
            chunks = [self.fetch('language.mix', position, min(1_048_576, offset + size - position))
                      for position in range(offset, offset + size, 1_048_576)]
            raw = b''.join(chunks)
            bink_header(raw, size)
            if name in EXPECTED_SHA256 and sha(raw) != EXPECTED_SHA256[name]:
                raise ValueError('Original payload differs from independently acquired pinned source SHA-256')
            retain_original(path, raw)
        if name in EXPECTED_SHA256 and sha(raw) != EXPECTED_SHA256[name]:
            raise ValueError('Existing original differs from pinned source SHA-256')
        asset = dict(path=path.relative_to(ROOT).as_posix(), original_filename=name,
            source_archive='language.mix', source_url=URL, source_revision=REV,
            archive_offset=offset, http_range=f'bytes={offset}-{offset+size-1}',
            bytes=size, sha256=sha(raw), complete_original=True, actual_type='Bink 1 video')
        self.doc['assets'] = [a for a in self.doc['assets'] if a['original_filename'] != name] + [asset]
        self.save()
        print('ACQUIRED', asset['path'], asset['sha256'], flush=True)


SPEC = importlib.util.spec_from_file_location('authentic_shell_mix_reader', ROOT / 'Assets/Reference/Tools/fetch_ra2_samples.py')
reader = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(reader)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probe', action='store_true', help='read Bink headers/frame tables only, without movie payloads')
    parser.add_argument('--variant', choices=('large', 'small', 'both'), default='large')
    args = parser.parse_args()
    acq = Acquisition()
    reader.fetch = acq.fetch
    index = reader.mix_index('language.mix')
    for name, expected in KNOWN.items():
        if index.get(reader.filename_hash(name)) != expected:
            raise ValueError(f'Original MIX index differs from audited offset: {name}')
        acq.probe(name, *expected)
    acq.doc['verified_mix_index'] = {'encrypted': True, 'entries': len(index),
        'evidence': 'SHA-verified exact ranges 0..91 and 84..163; reused read-only if present, otherwise fetched with strict HTTP 206; decrypted by original read-only MIX reader.'}
    if not args.probe:
        selected = {'large': ['ra2ts_l.bik'], 'small': ['ra2ts_s.bik'], 'both': list(KNOWN)}[args.variant]
        for name in selected:
            acq.acquire(name, *KNOWN[name])
    acq.save()
    print('Transferred this run:', acq.transferred, 'bytes', flush=True)


if __name__ == '__main__':
    main()
