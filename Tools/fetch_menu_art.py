#!/usr/bin/env python3
"""Acquire original RA2 menu data with audited HTTP 206 ranges, never archives.

The source revision is pinned. Every received range is cached with SHA-256 and
source headers. No executable files or video resources are selected.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys
import urllib.request

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'Assets/Reference/Menu'
MANIFEST = ROOT / 'Assets/Reference/manifest-menu.json'
REV = 'acfa5b834899e7fab4c8499ccb4b49c402927b44'
BASE = f'https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/{REV}/game-res/'
BUDGET = 20_000_000
SPEC = importlib.util.spec_from_file_location('menu_mix_reader', ROOT / 'Assets/Reference/Tools/fetch_ra2_samples.py')
reader = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(reader)


def sha(data):
    return hashlib.sha256(data).hexdigest()


class Acquisition:
    def __init__(self):
        self.doc = json.loads(MANIFEST.read_text()) if MANIFEST.exists() else {
            'schema': 1, 'source_revision': REV, 'source_base_url': BASE,
            'method': 'Exact HTTP 206 ranges only; no executable game code or videos.',
            'budget_bytes': BUDGET, 'ranges': [], 'assets': [], 'audit': {}}
        self.ranges = {r['cache_path']: r for r in self.doc['ranges']}
        self.transferred = 0
        self.indexes = {}
        reader.fetch = self.fetch

    def save(self):
        self.doc['ranges'] = list(self.ranges.values())
        self.doc['retained_range_bytes'] = sum(r['bytes'] for r in self.ranges.values())
        self.doc['last_run_transferred_bytes'] = self.transferred
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_text(json.dumps(self.doc, indent=2) + '\n')

    def fetch(self, archive, offset, length):
        parts = []
        while length:
            if archive == 'ra2.mix':
                part, po = divmod(offset, 70_000_000)
                filename = f'ra2.mix.part{part:02d}'
                size = min(length, 70_000_000 - po)
            else:
                filename, po, size = archive, offset, length
            path = DEST / 'Ranges' / f'{filename}.{po}.{size}.bin'
            relative = path.relative_to(ROOT).as_posix()
            record = self.ranges.get(relative)
            if record:
                data = path.read_bytes()
                if len(data) != size or sha(data) != record['sha256']:
                    raise ValueError(f'Cached range mismatch: {relative}')
            else:
                retained = sum(r['bytes'] for r in self.ranges.values())
                if retained + size > BUDGET:
                    raise RuntimeError('Menu 20 MB retained range budget exceeded')
                url = BASE + filename
                request_url = url + f'?range={po}-{size}'
                range_value = f'bytes={po}-{po + size - 1}'
                request = urllib.request.Request(request_url, headers={'Range': range_value})
                with urllib.request.urlopen(request, timeout=40) as response:
                    content_range = response.headers.get('Content-Range', '')
                    if response.status != 206 or not content_range.startswith(f'bytes {po}-{po + size - 1}/'):
                        raise RuntimeError('Server did not honor exact requested range')
                    data = response.read(size + 1)
                    if len(data) != size:
                        raise RuntimeError('Server returned incorrect range length')
                    etag = response.headers.get('ETag')
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
                record = dict(cache_path=relative, source_archive=archive,
                    source_url=url, request_url=request_url, archive_offset=offset,
                    http_range=range_value, content_range=content_range,
                    http_status=206, etag=etag, bytes=size, sha256=sha(data))
                self.ranges[relative] = record
                self.transferred += size
                self.save()
            parts.append(data)
            length -= size
            offset += size
        return b''.join(parts)

    def extract(self, archive, chain, index, name, category='Original'):
        entry = index.get(reader.filename_hash(name))
        if entry is None:
            return None
        offset, size = entry
        raw = self.fetch(archive, offset, size)
        folder = '-'.join(chain).replace('.mix', '') or archive.replace('.mix', '')
        destination = DEST / category / folder / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists() and destination.read_bytes() != raw:
            raise ValueError(f'Refusing to overwrite differing original: {destination}')
        destination.write_bytes(raw)
        segments = []
        pos, remaining = offset, size
        while remaining:
            if archive == 'ra2.mix':
                part, local = divmod(pos, 70_000_000)
                filename = f'ra2.mix.part{part:02d}'
                count = min(remaining, 70_000_000 - local)
            else:
                filename, local, count = archive, pos, remaining
            segments.append({'source_url': BASE + filename, 'http_range': f'bytes={local}-{local+count-1}',
                             'archive_offset': pos, 'bytes': count})
            pos += count
            remaining -= count
        item = dict(path=destination.relative_to(ROOT).as_posix(), original_filename=name,
                    bytes=size, sha256=sha(raw), source_archive=archive,
                    nested_archives=chain, archive_offset=offset, ranges=segments,
                    actual_type=name.rsplit('.', 1)[-1].upper())
        self.doc['assets'] = [r for r in self.doc['assets'] if r['path'] != item['path']] + [item]
        self.save()
        return raw, item

    def probe(self, archive, chain=(), offset=0, depth=0):
        key = '/'.join((archive,) + chain)
        index = reader.mix_index(archive, offset)
        self.indexes[key] = index
        names = []
        database = self.extract(archive, chain, index, 'local mix database.dat', 'Database')
        if database:
            # XCC filename database contains NUL-terminated ASCII names. Retain
            # the exact original database as well as the matching name inventory.
            candidates = re.findall(rb'[\x20-\x7e]{3,}', database[0])
            names = [x.decode('ascii') for x in candidates if re.fullmatch(rb'[A-Za-z0-9_ .!()#-]+\.[A-Za-z0-9]{2,4}', x)]
        guesses = ['local.mix', 'gmenu.mix', 'gmenumd.mix', 'cache.mix', 'generic.mix',
                   'audio.mix', 'ra2ts_l.bik', 'ra2ts_s.bik', 'ra2logo.bik', 'logo.bik',
                   'language.mix', 'expand.mix', 'ecache01.mix', 'globe.mix', 'cameo.mix',
                   'sidec01.mix', 'sidec02.mix', 'sidec03.mix', 'load.mix',
                   'ra2md.mix', 'conquer.mix', 'conquer.eng', 'ra2.csf', 'uimd.ini',
                   'ui.ini', 'dialog.ini', 'mainmenu.ini', 'mousepal.pal', 'mouse.pal',
                   'shell.pal', 'menu.pal', 'mainmenu.pcx', 'bkgd.pcx', 'ra2logo.pcx']
        available = sorted(set(n for n in names + guesses if reader.filename_hash(n) in index), key=str.lower)
        self.doc['audit'][key] = dict(entries=len(index), database_names=names,
            available_names=available, absolute_archive_offset=offset,
            candidate_names_absent=[n for n in guesses if reader.filename_hash(n) not in index],
            index_entries=[{'filename_crc32': f'{ident:08x}', 'archive_offset': pos, 'bytes': size,
                            'resolved_names': [n for n in available if reader.filename_hash(n) == ident]}
                           for ident, (pos, size) in index.items()])
        self.save()
        print(key, 'entries', len(index), 'resolved_names', len(available),
              'sample', json.dumps(available[:24]), flush=True)
        # Local and gmenu are the menu data containers. Other roots are recorded
        # by name but their gameplay, speech and video payload is never traversed.
        if depth < 3:
            for name in available:
                if name.lower() not in ('local.mix', 'gmenu.mix', 'gmenumd.mix', 'cache.mix', 'load.mix'):
                    continue
                position, _ = index[reader.filename_hash(name)]
                self.probe(archive, chain + (name,), position, depth + 1)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--probe', action='store_true', help='inventory original filenames and metadata only')
    args = parser.parse_args()
    acquisition = Acquisition()
    for archive in ('language.mix', 'ra2.mix'):
        acquisition.probe(archive)
    database_names = sorted({name for audit in acquisition.doc['audit'].values() for name in audit.get('database_names', [])})
    for key, index in acquisition.indexes.items():
        matches = [name for name in database_names if reader.filename_hash(name) in index]
        acquisition.doc['audit'][key]['cross_database_name_matches'] = matches
    language = acquisition.doc['audit']['language.mix']
    acquisition.doc['excluded_identified_payloads'] = [entry for entry in language['index_entries']
        if any(name.endswith(('.bik', '.vqa')) or name == 'audio.mix' for name in entry['resolved_names'])]
    acquisition.doc['audit_notes'] = [
        'Original XCC local mix database.dat filenames resolve ra2.mix and local.mix entries; databases are retained byte-for-byte.',
        'gmenu.mix and gmenumd.mix are absent as entries in all probed containers. load.mix has 83 entries but no local filename database.',
        'ra2ts_l.bik and ra2ts_s.bik are identified by filename CRC only. Neither video payload nor any audio/executable payload was downloaded.',
        'Original UI.INI is retained; it specifies command-bar ordering, not menu screen coordinates.',
        'Archive provenance does not establish that a legacy-named PCX was used by the shipped RA2 main menu.'
    ]
    if not args.probe:
        local_key = 'ra2.mix/local.mix'
        local = acquisition.indexes[local_key]
        selected = ['ui.ini', 'dialog.pal', 'mainbttn.pal', 'gls.pal',
                    'title.pcx', 'logo.pcx', 'multi.pcx', 'campaign.pcx', 'dbak6440.pcx',
                    'dlgsysa.pcx', 'dlgsysi.pcx', 'leftbar.pcx', 'rightbar.pcx',
                    'shellbttnoff.pcx', 'shellbttnon.pcx', 'shellbttnpress.pcx',
                    'mnbttn.shp', 'pudlgbga.shp', 'pudlgbgn.shp', 'pudlgbgs.shp',
                    'glsl.shp', 'glss.shp', 'bits_a.pcx', 'bits_i.pcx',
                    'cce_i.pcx', 'cce_il.pcx', 'cce_ir.pcx', 'cue_i.pcx',
                    'arrow_dd.pcx', 'arrow_du.pcx', 'arrow_ud.pcx', 'arrow_uu.pcx',
                    'bar_ll.pcx', 'bar_lr.pcx', 'bar_ul.pcx', 'bar_ur.pcx',
                    'sbgripb.pcx', 'sbgripm.pcx', 'sbgript.pcx',
                    'gsbgripb.pcx', 'gsbgripm.pcx', 'gsbgript.pcx',
                    'uparrowp.pcx', 'uparrowr.pcx', 'dnarrowp.pcx', 'dnarrowr.pcx',
                    'guparrowp.pcx', 'guparrowr.pcx', 'gdnarrowp.pcx', 'gdnarrowr.pcx',
                    'trakgrip.pcx', 'trofl.pcx', 'trofm.pcx', 'trofr.pcx']
        available = acquisition.doc['audit'][local_key]['available_names']
        selected += [n for n in available if re.fullmatch(r'b[du][ed]_[lmr]i(?:24|30)\.pcx', n)]
        selected += [n for n in available if n.startswith('tab_') and n.endswith('.pcx')]
        planned = sum(local[reader.filename_hash(n)][1] for n in selected if reader.filename_hash(n) in local)
        print('Selected original payload bytes', planned, flush=True)
        acquisition.doc['selection'] = {'container': local_key, 'names': selected,
            'planned_bytes': planned, 'excluded': 'All executable game code, video, audio, gameplay voxel/sprite data.'}
        for name in selected:
            result = acquisition.extract('ra2.mix', ('local.mix',), local, name)
            if result:
                print('EXTRACTED', name, result[1]['bytes'], flush=True)
        cache = acquisition.indexes['ra2.mix/cache.mix']
        for name in ('shell.pal', 'mousepal.pal'):
            acquisition.extract('ra2.mix', ('cache.mix',), cache, name)
    acquisition.save()
    print('Transferred', acquisition.transferred, 'bytes; retained ranges', acquisition.doc['retained_range_bytes'], flush=True)


if __name__ == '__main__':
    main()
