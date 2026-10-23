#!/usr/bin/env python3
"""Fetch original RA2 building details with exact MIX byte ranges, never archives.

Reuses the fixed-revision MIX reader already retained with the reference samples.
The manifest records every HTTP request (including encrypted MIX indices) and
SHA-256 fingerprints. Existing samples are reused after checking their manifest.
"""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
REFERENCE = ROOT / 'Assets/Reference'
DETAILS = REFERENCE / 'Details'
MANIFEST = REFERENCE / 'manifest-details.json'


def reader():
    spec = importlib.util.spec_from_file_location('ra2_sample_mix', REFERENCE / 'Tools/fetch_ra2_samples.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    mix = reader()
    requests = []
    original_fetch = mix.fetch

    def fetch(archive, offset, length):
        data = original_fetch(archive, offset, length)
        remaining, cursor = length, offset
        while remaining:
            part, start = divmod(cursor, 70_000_000) if archive == 'ra2.mix' else (None, cursor)
            size = min(remaining, 70_000_000 - start) if part is not None else remaining
            url = mix.BASE + (f'ra2.mix.part{part:02d}' if part is not None else archive)
            requests.append(dict(source_url=url, http_range=f'bytes={start}-{start+size-1}', bytes=size,
                                 sha256=hashlib.sha256(data[length-remaining:length-remaining+size]).hexdigest()))
            cursor += size
            remaining -= size
        return data

    mix.fetch = fetch
    root = mix.mix_index('ra2.mix')
    indices = {}
    old = json.loads(MANIFEST.read_text()) if MANIFEST.exists() else {}
    existing = {a['original_filename']: a for a in json.loads((REFERENCE / 'manifest.json').read_text())['assets'] if 'original_filename' in a}
    existing.update({a['original_filename']: a for a in old.get('assets', [])})
    assets = []
    missing = []

    def checkpoint():
        DETAILS.mkdir(parents=True, exist_ok=True)
        manifest = dict(schema=1, source_revision=mix.REV, source_base=mix.BASE,
                        assets=assets, last_range_download_bytes=mix.transferred,
                        http_requests=old.get('http_requests', []) + requests, missing_optional=missing,
                        note='Original binary assets and original art.ini only; fixed commit, exact HTTP 206 ranges.')
        MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')

    def get(name, archives=('generic.mix', 'isotemp.mix', 'temperat.mix', 'cache.mix', 'local.mix'), optional=False):
        previous = existing.get(name)
        if previous and (REFERENCE / previous['path']).exists():
            raw = (REFERENCE / previous['path']).read_bytes()
            if hashlib.sha256(raw).hexdigest() != previous['sha256']:
                raise RuntimeError(f'Changed source: {name}')
            assets.append(previous)
            print('REUSED', name, flush=True)
            return raw
        for nested in archives:
            if nested not in indices:
                pos, _ = root[mix.filename_hash(nested)]
                indices[nested] = mix.mix_index('ra2.mix', pos)
            if mix.extract('ra2.mix', nested, indices[nested], name, 'Details'):
                assets.append(mix.records[-1])
                checkpoint()
                return (DETAILS / name).read_bytes()
        if optional:
            missing.append(name)
            print('MISSING_OPTIONAL', name, flush=True)
            return None
        raise RuntimeError(f'Original asset not found: {name}')

    get('art.ini', ('local.mix',))
    # Names are original art.ini references with RA2's generic/temperate theater substitution.
    names = [
        'ggpowr_a', 'ngpowr_a', 'ggpile_a',
        'ggcnst_a', 'ggcnst_b', 'ngcnst_a', 'ngcnst_b', 'ngcnst_c',
        *(f'{side}grefnl{number}' for side in ('g', 'n') for number in range(1, 5)),
        'ggrefnor', 'ngrefnor',
        'ggweap_1', 'ggweap_2', 'ggweap_a', 'ggweap_b',
        'ngweap_1', 'ngweap_2', 'ngweap_a',
        'gtcnstmk', 'ntcnstmk', 'gtpowrmk', 'ntpowrmk', 'gtpilemk', 'nthandmk',
        'gtrefnmk', 'ntrefnmk', 'gtweapmk', 'ntweapmk',
        'gtgcanmk', 'nttslamk', 'gtpillmk', 'gtaircmk', 'ntradrmk', 'gttechmk', 'nttechmk',
    ]
    for name in names:
        get(name + '.shp', optional=name.endswith('refnl4'))
    checkpoint()
    print('TRANSFERRED_BYTES', mix.transferred, flush=True)


if __name__ == '__main__':
    main()
