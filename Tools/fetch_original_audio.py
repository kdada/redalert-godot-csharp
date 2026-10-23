#!/usr/bin/env python3
"""Acquire selected original RA2 audio from a pinned public XWIS asset mirror.

Only exact HTTP 206 byte ranges are read from MIX archives. No complete game,
executable, synthesized substitute, or fan remake is acquired. Original compressed
samples are retained together with precise source metadata under Reference/Audio.
"""
from __future__ import annotations
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys
import urllib.request

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'Assets/Reference/Audio'
REV = 'acfa5b834899e7fab4c8499ccb4b49c402927b44'
BASE = f'https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/{REV}/game-res/'
MAX_TRANSFER = 24_000_000
transferred = 0
ranges = []
assets = []


def sha(data):
    return hashlib.sha256(data).hexdigest()


def fetch(archive, offset, length):
    global transferred
    result = bytearray()
    while length:
        if archive == 'ra2.mix':
            part, begin = divmod(offset, 70_000_000)
            filename = f'ra2.mix.part{part:02d}'
            count = min(length, 70_000_000 - begin)
        else:
            filename, begin, count = archive, offset, length
        if transferred + count > MAX_TRANSFER:
            raise ValueError('Bounded audio transfer budget exceeded')
        url = filename if filename.startswith('https://') else BASE + filename
        request = urllib.request.Request(url + f'?original-audio-range={begin}-{count}',
            headers={'Range': f'bytes={begin}-{begin+count-1}', 'Accept-Encoding': 'identity'})
        with urllib.request.urlopen(request, timeout=35) as response:
            content_range = response.headers.get('Content-Range', '')
            if response.status != 206 or not content_range.startswith(f'bytes {begin}-{begin+count-1}/'):
                raise ValueError('Exact HTTP 206 range required; refusing a whole archive response')
            chunk = response.read(count + 1)
            if len(chunk) != count:
                raise ValueError('Incorrect source range length')
        transferred += count
        ranges.append(dict(source_url=url, archive_offset=offset, http_range=f'bytes={begin}-{begin+count-1}',
            content_range=content_range, bytes=count, sha256=sha(chunk)))
        result.extend(chunk)
        length -= count
        offset += count
    return bytes(result)


SPEC = importlib.util.spec_from_file_location('original_audio_mix_reader', ROOT / 'Assets/Reference/Tools/fetch_ra2_samples.py')
reader = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(reader)
reader.fetch = fetch


def retain(relative, data, **metadata):
    path = DEST / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_bytes() != data:
        raise ValueError(f'Refusing to overwrite changed original: {path}')
    if not path.exists():
        path.write_bytes(data)
    record = {**metadata, 'path': path.relative_to(ROOT).as_posix(), 'bytes': len(data), 'sha256': sha(data)}
    assets.append(record)
    return record


def read_idx(data):
    magic, version, count = struct.unpack_from('<4sII', data)
    if magic != b'GABA' or version not in (1, 2) or len(data) != 12 + 36 * count:
        raise ValueError(f'Unexpected RA2 IDX header: {magic!r} version={version} count={count}')
    result = {}
    for i in range(count):
        raw_name, offset, length, rate, flags, chunk = struct.unpack_from('<16sIIIII', data, 12 + 36 * i)
        name = raw_name.split(b'\0')[0].decode('ascii')
        result[name.lower()] = dict(name=name, offset=offset, bytes=length, sample_rate=rate, flags=flags, chunk_size=chunk)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probe', action='store_true', help='retain indexes and INI mappings only')
    args = parser.parse_args()
    DEST.mkdir(parents=True, exist_ok=True)
    # Keep source IMA ADPCM WAVs out of Godot's PCM-only import pipeline.
    # Install this before retaining any samples, including during --probe.
    ignore = DEST / '.gdignore'
    if not ignore.exists():
        ignore.write_text('# Source audio and provenance only; import Assets/Generated/Audio/Original instead.\n')
    language = reader.mix_index('language.mix')
    audio_offset, audio_size = language[reader.filename_hash('audio.mix')]
    audio = reader.mix_index('language.mix', audio_offset)
    idx_offset, idx_length = audio[reader.filename_hash('audio.idx')]
    bag_offset, bag_length = audio[reader.filename_hash('audio.bag')]
    idx_data = fetch('language.mix', idx_offset, idx_length)
    retain('Metadata/audio.idx', idx_data, original_filename='audio.idx', source_url=BASE+'language.mix', archive_offset=idx_offset)
    samples = read_idx(idx_data)
    (DEST / 'Metadata/audio-index.json').write_text(json.dumps(samples, indent=2)+'\n')
    root = reader.mix_index('ra2.mix')
    local_offset, _ = root[reader.filename_hash('local.mix')]
    local = reader.mix_index('ra2.mix', local_offset)
    for name in ['sound.ini', 'eva.ini', 'theme.ini', 'rules.ini']:
        entry = local.get(reader.filename_hash(name))
        if entry:
            offset, size = entry
            retain('Metadata/'+name, fetch('ra2.mix', offset, size), original_filename=name,
                source_archive='ra2.mix/local.mix', archive_offset=offset)
    doc = dict(schema=2, source_revision=REV, source_manifest_url=BASE+'manifest.json',
        source_description='Original RA2 sound-bank and EVA assets extracted from the Red-Alert-2-Multiplayer XWIS copy; active soundtrack MP3s downloaded unchanged from composer Frank Klepacki original soundtrack page.',
        rights='EA/Westwood and respective original rights holders. Mirror metadata license does not relicense game audio.',
        original_audio=True, synthetic_audio=False, audio_mix_offset=audio_offset, audio_mix_bytes=audio_size,
        bag_offset=bag_offset, bag_bytes=bag_length, bag_samples=len(samples), ranges=ranges, assets=assets)
    if not args.probe:
        mapping = json.loads((DEST/'Metadata/selection.json').read_text())
        def bag_item(item):
            cue, choice = item
            name = choice['sample'].lower()
            entry = samples[name]
            if entry['offset'] + entry['bytes'] > bag_length:
                raise ValueError('Sample exceeds original BAG')
            raw = fetch('language.mix', bag_offset + entry['offset'], entry['bytes'])
            return retain('Original/'+name+'.bag-sample', raw, cue=cue, original_filename=entry['name']+'.wav',
                source_url=BASE+'language.mix', source_archive='language.mix/audio.mix/audio.bag',
                archive_offset=bag_offset+entry['offset'], **entry)
        def wav_item(item):
            cue, choice = item
            name = choice['sample']
            offset, size = audio[reader.filename_hash(name)]
            raw = fetch('language.mix', offset, size)
            return retain('Original/'+name, raw, cue=cue, original_filename=name, source_url=BASE+'language.mix',
                source_archive='language.mix/audio.mix', archive_offset=offset)
        with ThreadPoolExecutor(max_workers=5) as pool:
            list(pool.map(bag_item, mapping['bag'].items()))
            list(pool.map(wav_item, mapping.get('wav', {}).items()))
        def music_item(item):
            cue, choice = item
            name = choice['sample']
            size = choice['bytes']
            url = choice.get('url', BASE+'music/'+name)
            raw = fetch(url, 0, size)
            return retain('Original/'+name, raw, cue=cue, original_filename=name, source_url=url,
                source_archive=choice.get('provenance', 'upstream extracted/transcoded music'), upstream_blob_sha1=choice.get('blob_sha1'))
        with ThreadPoolExecutor(max_workers=3) as pool:
            list(pool.map(music_item, mapping['music'].items()))
        doc['selection'] = mapping
    doc['last_transfer_bytes'] = transferred
    (DEST/'manifest.json').write_text(json.dumps(doc, indent=2)+'\n')
    print(json.dumps(dict(original_assets=len(assets), bag_samples=len(samples), transferred=transferred, probe=args.probe)), flush=True)


if __name__ == '__main__':
    main()
