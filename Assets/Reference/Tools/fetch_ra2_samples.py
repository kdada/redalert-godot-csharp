#!/usr/bin/env python3
"""Reproduce small RA2 samples via HTTP Range; never fetch a whole game archive.

Uses Python stdlib + Node/OpenSSL legacy provider for Blowfish. The RSA constant
and MIX layout are documented by OpenRA and tcrs/ra2ff. Only binary game assets
are written, exclusively beneath this script's Assets/Reference parent.
"""
import base64
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import urllib.request
import zlib

ROOT = Path(__file__).resolve().parents[1]
REV = 'acfa5b834899e7fab4c8499ccb4b49c402927b44'
BASE = f'https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/{REV}/game-res/'
MAX_BYTES = 19_000_000
transferred = 0
records = []
MODULUS = int.from_bytes(base64.b64decode(
    'AihRvNoIbTn85FZRYNZRcT+i6KpU+maCsEqr3Q5q+LDB5tH7Tz2qQ38V')[2:], 'big')
BF_JS = """const c=require('node:crypto');let x='';process.stdin.on('data',b=>x+=b).on('end',()=>{const a=JSON.parse(x),d=c.createDecipheriv('bf-ecb',Buffer.from(a.key,'hex'),null);d.setAutoPadding(false);process.stdout.write(Buffer.concat([d.update(Buffer.from(a.data,'hex')),d.final()]));});"""


def fetch(archive, offset, length):
    global transferred
    result = b''
    remaining = length
    while remaining:
        if archive == 'ra2.mix':
            part, po = divmod(offset, 70_000_000)
            filename = f'ra2.mix.part{part:02d}'
            size = min(remaining, 70_000_000 - po)
        else:
            filename, po, size = archive, offset, remaining
        if transferred + size > MAX_BYTES:
            raise RuntimeError('20 MB transfer budget would be exceeded')
        url = BASE + filename
        request = urllib.request.Request(
            url + f'?range={po}-{size}',
            headers={'Range': f'bytes={po}-{po + size - 1}'})
        with urllib.request.urlopen(request, timeout=25) as response:
            expected = f'bytes {po}-{po + size - 1}/'
            if response.status != 206 or not response.headers.get('Content-Range', '').startswith(expected):
                raise RuntimeError('Server did not honor exact byte range')
            chunk = response.read(size + 1)
            if len(chunk) != size:
                raise RuntimeError('Incorrect response size')
        transferred += size
        result += chunk
        remaining -= size
        offset += size
    return result


def blowfish(data, key):
    request = json.dumps({'data': data.hex(), 'key': key.hex()}).encode()
    return subprocess.run(
        ['node', '--openssl-legacy-provider', '-e', BF_JS], input=request,
        capture_output=True, check=True).stdout


def mix_index(archive, offset=0):
    prefix = fetch(archive, offset, 92)
    flags = struct.unpack_from('<I', prefix)[0]
    if flags & ~0x30000 == 0:
        if flags & 0x20000:
            key = b''.join(pow(int.from_bytes(prefix[4+i:44+i], 'little'),
                65537, MODULUS).to_bytes(40, 'little')[:39]
                for i in (0, 40))[:56]
            count = struct.unpack_from('<H', blowfish(prefix[84:92], key))[0]
            size = (6 + 12 * count + 7) // 8 * 8
            header = blowfish(fetch(archive, offset + 84, size), key)
            data_start = offset + 84 + size
        else:
            count = struct.unpack_from('<H', prefix, 4)[0]
            header = fetch(archive, offset + 4, 6 + 12 * count)
            data_start = offset + 4 + len(header)
    else:
        count = struct.unpack_from('<H', prefix)[0]
        header = fetch(archive, offset, 6 + 12 * count)
        data_start = offset + len(header)
    return {ident: (data_start + pos, size) for ident, pos, size
        in struct.iter_unpack('<III', header[6:6 + 12 * count])}


def filename_hash(name):
    data = name.upper().encode('ascii')
    remainder = len(data) % 4
    if remainder:
        data += bytes([remainder]) + data[len(data)//4*4:len(data)//4*4+1] * (3-remainder)
    return zlib.crc32(data)


def extract(archive, nested_name, index, name, folder):
    entry = index.get(filename_hash(name))
    if entry is None:
        return False
    offset, size = entry
    data = fetch(archive, offset, size)
    destination = ROOT / folder / name
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        if destination.read_bytes() != data:
            raise RuntimeError(f'Refusing to overwrite changed sample: {destination}')
    else:
        destination.write_bytes(data)
    if archive == 'ra2.mix':
        part, po = divmod(offset, 70_000_000)
        url = BASE + f'ra2.mix.part{part:02d}'
    else:
        po, url = offset, BASE + archive
    item = dict(path=destination.relative_to(ROOT).as_posix(), bytes=size,
        sha256=hashlib.sha256(data).hexdigest(), source_url=url,
        source_archive=archive, nested_archive=nested_name,
        archive_offset=offset, http_range=f'bytes={po}-{po+size-1}',
        original_filename=name, actual_type=name.rsplit('.', 1)[1].upper())
    if name.endswith('.shp'):
        magic, width, height, frames = struct.unpack_from('<4H', data)
        if magic != 0 or not width or not height or not frames:
            raise RuntimeError(f'Invalid SHP header: {name}')
        item.update(width=width, height=height, frames=frames)
    if name.endswith('.vxl') and not data.startswith(b'Voxel Animation'):
        raise RuntimeError(f'Invalid VXL signature: {name}')
    if name.endswith('.pal') and len(data) != 768:
        raise RuntimeError(f'Invalid PAL size: {name}')
    records.append(item)
    print(json.dumps(item, ensure_ascii=False), flush=True)
    return True


def main():
    root = mix_index('ra2.mix')
    groups = {
        'conquer.mix': ('Battlefield/Infantry', ['gi.shp', 'cons.shp', 'snipe.shp']),
        'local.mix': ('Battlefield/Voxels', [
            name + ext for name in ['gtnk', 'gtnktur', 'gtnkbarl', 'htnk',
                'htnktur', 'htnkbarl', 'cmin', 'harv', 'harvtur', 'tnkd', 'ttnk', 'ttnktur']
                for ext in ['.vxl', '.hva']]),
        'generic.mix': ('Battlefield/Buildings', ['ggpowr.shp', 'ggpowr_a.shp',
            'ggpile.shp', 'ggpile_a.shp', 'ggrefn.shp', 'ggrefnbb.shp',
            'ggweap.shp', 'ggweapbb.shp', 'ggweap_1.shp', 'ggweap_2.shp',
            'ngpowr.shp', 'nghand.shp', 'ngrefn.shp', 'ngrefnbb.shp',
            'ngweap.shp', 'ngweapbb.shp']),
        'tem.mix': ('Formats', ['unittem.pal', 'isotem.pal']),
    }
    for nested, (folder, names) in groups.items():
        pos, _ = root[filename_hash(nested)]
        index = mix_index('ra2.mix', pos)
        for name in names:
            extract('ra2.mix', nested, index, name, folder)
    language = mix_index('language.mix')
    icon_names = ['giicon.shp', 'gtnkicon.shp', 'ahrvicon.shp', 'e2icon.shp',
        'htnkicon.shp', 'harvicon.shp', 'snipicon.shp', 'tnkdicon.shp',
        'beagicon.shp', 'paraicon.shp', 'aparicon.shp', 'ttnkicon.shp',
        'clegicon.shp', 'cconicon.shp', 'cameo.pal']
    for name in icon_names:
        extract('language.mix', None, language, name, 'Formats/Cameos')
    for nested in ['cameo.mix', 'cameo1.mix', 'cameo2.mix', 'sidec01.mix', 'sidec02.mix']:
        if filename_hash(nested) in language:
            pos, _ = language[filename_hash(nested)]
            index = mix_index('language.mix', pos)
            for name in icon_names:
                extract('language.mix', nested, index, name, 'Formats/Cameos')
    print('TRANSFERRED_BYTES', transferred, flush=True)
    manifest = ROOT / 'manifest.json'
    previous = json.loads(manifest.read_text()) if manifest.exists() else {}
    replaced_paths = {item['path'] for item in records}
    previous['retrieval_date'] = '2026-10-18'
    previous['last_range_download_bytes'] = transferred
    previous['assets'] = [item for item in previous.get('assets', [])
        if item['path'] not in replaced_paths] + records
    manifest.write_text(json.dumps(previous, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
