#!/usr/bin/env python3
"""Extract original map DATA by bounded HTTP ranges, never engine source."""
from concurrent.futures import ThreadPoolExecutor
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import threading

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Reference/Maps'
REV = 'acfa5b834899e7fab4c8499ccb4b49c402927b44'
BASE = f'https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/{REV}/game-res/'
CACHE = Path(os.environ['XDG_CACHE_HOME']) / 'redalert-godot-csharp/classic-map-ranges'
BUDGET = 18_000_000
transferred = 0
lock = threading.Lock()


def ranged(archive, offset, count):
    global transferred
    if archive == 'ra2.mix':
        part, position = divmod(offset, 70_000_000)
        if position + count > 70_000_000:
            first = 70_000_000 - position
            return ranged(archive, offset, first) + ranged(archive, offset + first, count - first)
        filename = f'ra2.mix.part{part:02d}'
    else:
        filename, position = archive, offset
    url = BASE + filename
    key = hashlib.sha256(f'{url}:{position}:{count}'.encode()).hexdigest()
    cache = CACHE / key
    if cache.exists():
        data = cache.read_bytes()
        if len(data) != count:
            raise RuntimeError(f'Incomplete cached range {key}')
        return data
    with lock:
        transferred += count
        if transferred > BUDGET:
            raise RuntimeError('Map extraction transfer budget exceeded')
    result = subprocess.run(['curl', '--fail', '--silent', '--show-error',
        '--proxy', 'http://127.0.0.1:10808', '--max-time', '25', '--retry', '4',
        '--retry-all-errors', '--retry-delay', '0', '--max-filesize', str(count),
        '--range', f'{position}-{position+count-1}', f'{url}?range={position}-{count}'], capture_output=True)
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors='replace'))
    data = result.stdout
    if len(data) != count:
        raise RuntimeError(f'Expected {count} range bytes, got {len(data)}')
    CACHE.mkdir(parents=True, exist_ok=True)
    cache.write_bytes(data)
    return data


def reader():
    spec = importlib.util.spec_from_file_location('classic_maps_mix_reader', ROOT / 'Assets/Reference/Tools/fetch_ra2_samples.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.fetch = ranged
    return module


def retain(module, archive, nested, index, filename, folder='Original'):
    offset, size = index[module.filename_hash(filename)]
    data = ranged(archive, offset, size)
    destination = OUT / folder / filename.lower()
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists() and destination.read_bytes() != data:
        raise RuntimeError(f'Refusing changed original data: {destination}')
    destination.write_bytes(data)
    if archive == 'ra2.mix':
        part, begin = divmod(offset, 70_000_000); url = BASE + f'ra2.mix.part{part:02d}'
    else:
        begin, url = offset, BASE + archive
    return {'path': destination.relative_to(ROOT).as_posix(), 'sourceUrl': url,
        'sourceRepository': 'toarrntnz/ts-redalert2-assets', 'sourceCommit': REV,
        'archive': archive, 'nestedArchive': nested, 'originalFilename': filename,
        'archiveOffset': offset, 'httpRange': f'bytes={begin}-{begin+size-1}',
        'bytes': size, 'sha256': hashlib.sha256(data).hexdigest()}


def save_sources(records):
    path = OUT / 'sources.json'
    prior = json.loads(path.read_text()) if path.exists() else {'schema': 1, 'assets': []}
    merged = {item['path']: item for item in prior['assets']}
    merged.update({item['path']: item for item in records})
    path.write_text(json.dumps({'schema': 1, 'assets': sorted(merged.values(), key=lambda a: a['path'])}, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--probe', action='store_true')
    parser.add_argument('--maps', nargs='*', default=[])
    parser.add_argument('--metadata', action='store_true')
    parser.add_argument('--tiles', action='store_true')
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    module = reader(); multi = module.mix_index('multi.mix')
    if args.probe:
        candidates = [f'mp{number:02d}t{players}.{ext}' for number in range(1, 81) for players in range(2, 9) for ext in ('map', 'mpr', 'mmx')]
        candidates += ['missions.pkt', 'missions.ini', 'ra2.csf', 'local.mix', 'maps01.mix']
        matches = [{'name': name, 'offset': multi[module.filename_hash(name)][0], 'bytes': multi[module.filename_hash(name)][1]}
                   for name in candidates if module.filename_hash(name) in multi]
        (OUT / 'original-multi-index.json').write_text(json.dumps({'archive': 'multi.mix', 'entryCount': len(multi), 'matches': matches}, indent=2) + '\n')
        print(json.dumps({'multiEntryCount': len(multi), 'matches': matches}, indent=2), flush=True)
    records = []
    if args.maps:
        with ThreadPoolExecutor(max_workers=4) as executor:
            records.extend(executor.map(lambda name: retain(module, 'multi.mix', '', multi, name), args.maps))
    if args.metadata:
        ra2 = module.mix_index('ra2.mix')
        for nested in ('local.mix', 'isotemp.mix', 'isosnow.mix'):
            key = module.filename_hash(nested)
            if key not in ra2:
                continue
            index = module.mix_index('ra2.mix', ra2[key][0])
            for name in ('missions.pkt', 'missions.ini', 'rules.ini', 'ra2.csf', 'temperat.ini', 'snow.ini', 'isotem.pal', 'isosno.pal'):
                if module.filename_hash(name) in index:
                    records.append(retain(module, 'ra2.mix', nested, index, name, 'Metadata'))
        language = module.mix_index('language.mix')
        for name in ('ra2.csf', 'ra2md.csf'):
            if module.filename_hash(name) in language:
                records.append(retain(module, 'language.mix', '', language, name, 'Metadata'))
        for name in ('missions.pkt', 'missions.ini'):
            if module.filename_hash(name) in multi:
                records.append(retain(module, 'multi.mix', '', multi, name, 'Metadata'))
    if args.tiles:
        from map_data import decode, tilesets
        table = tilesets(OUT / 'Metadata/temperat.ini')
        used = set()
        for source_map in sorted((OUT / 'Original').glob('*.map')):
            doc, cells, _, _, _ = decode(source_map)
            if doc['map']['theater'].upper() != 'TEMPERATE':
                raise RuntimeError('This exact import batch requires temperate assets')
            used.update(0 if cell[2] in (-1, 65535) else cell[2] for cell in cells)
        ra2 = module.mix_index('ra2.mix')
        terrain_index = module.mix_index('ra2.mix', ra2[module.filename_hash('isotemp.mix')][0])
        sources = {}; missing = []
        for ident in sorted(used):
            item = table[ident]; name = item['filename']
            possible = [ROOT / 'Assets/Reference/Visual/Terrain' / name,
                ROOT / 'Assets/Reference/Terrain/Original' / name, OUT / 'Tiles/Temperate' / name]
            existing = next((path for path in possible if path.exists()), None)
            if existing:
                sources[ident] = {**item, 'sourcePath': existing.relative_to(ROOT).as_posix(),
                    'sourceSha256': hashlib.sha256(existing.read_bytes()).hexdigest()}
            else:
                if module.filename_hash(name) not in terrain_index:
                    raise RuntimeError(f'Official template is missing from original theater: {ident} {name}')
                missing.append(item)
        def one(item):
            record = retain(module, 'ra2.mix', 'isotemp.mix', terrain_index, item['filename'], 'Tiles/Temperate')
            return item, record
        print(json.dumps({'usedTemplates': len(used), 'existingTemplates': len(sources), 'fetchTemplates': len(missing)}), flush=True)
        with ThreadPoolExecutor(max_workers=8) as executor:
            for item, record in executor.map(one, missing):
                records.append(record)
                sources[item['tileIndex']] = {**item, 'sourcePath': record['path'], 'sourceSha256': record['sha256']}
        (OUT / 'tile-sources.json').write_text(json.dumps({'schema': 1, 'theater': 'TEMPERATE',
            'tiles': [sources[key] for key in sorted(sources)]}, indent=2) + '\n')
    if records:
        save_sources(records)
        print(json.dumps(records, indent=2), flush=True)
    print(json.dumps({'rangeBytesThisRun': transferred}), flush=True)


if __name__ == '__main__':
    main()
