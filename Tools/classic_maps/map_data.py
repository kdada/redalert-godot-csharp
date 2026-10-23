"""Map data parsing. No original engine code or executable behavior is used."""
from pathlib import Path
import struct
import importlib.util

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('classic_map_chunk_codecs', HERE / 'codecs.py')
chunk_codecs = importlib.util.module_from_spec(spec); spec.loader.exec_module(chunk_codecs)


def ini(path):
    raw = Path(path).read_bytes()
    try:
        text = raw.decode('utf-8-sig')
    except UnicodeDecodeError:
        text = raw.decode('cp1252', errors='replace')
    result = {}; section = None
    for original in text.splitlines():
        line = original.split(';', 1)[0].strip()
        if line.startswith('[') and line.endswith(']'):
            section = result.setdefault(line[1:-1].strip().lower(), {})
        elif section is not None and '=' in line:
            key, value = line.split('=', 1); section[key.strip().lower()] = value.strip()
    return result


def tilesets(path):
    doc = ini(path); result = []
    for section, values in sorted(doc.items()):
        if not section.startswith('tileset') or not section[7:].isdigit():
            continue
        count = int(values.get('tilesinset', '0')); prefix = values.get('filename', '').lower()
        for number in range(1, count + 1):
            result.append({'tileIndex': len(result), 'tileSetId': int(section[7:]),
                'setName': values.get('setname', ''), 'template': f'{prefix}{number:02d}',
                'filename': f'{prefix}{number:02d}.tem'})
    return result


def csf(path):
    data = Path(path).read_bytes(); assert data[:4] == b' FSC'
    labels = struct.unpack_from('<I', data, 8)[0]; cursor = 24; result = {}
    for _ in range(labels):
        assert data[cursor:cursor+4] == b' LBL'; cursor += 4
        strings, size = struct.unpack_from('<II', data, cursor); cursor += 8
        key = data[cursor:cursor+size].decode('ascii').lower(); cursor += size
        values = []
        for _ in range(strings):
            tag = data[cursor:cursor+4]; cursor += 4
            assert tag in (b' RTS', b'WRTS')
            length = struct.unpack_from('<I', data, cursor)[0]; cursor += 4
            values.append(bytes(b ^ 255 for b in data[cursor:cursor+length*2]).decode('utf-16le')); cursor += length*2
            if tag == b'WRTS':
                extra = struct.unpack_from('<I', data, cursor)[0]; cursor += 4+extra
        result[key] = values[0] if values else ''
    assert cursor == len(data)
    return result


def decode(path):
    doc = ini(path)
    iso, iso_manifest = chunk_codecs.unpack(doc['isomappack5'], chunk_codecs.lzo1x)
    records = []; cursor = 0; seen = set()
    while cursor + 11 <= len(iso):
        x, y, tile, sub, level, ice = struct.unpack_from('<hhiBBB', iso, cursor)
        if x == 0 and y == 0:
            break
        if (x, y) in seen or not (-512 < x < 512 and -512 < y < 512) or level > 31:
            raise ValueError(f'Invalid/duplicate original map cell {(x,y,tile,sub,level,ice)} at {cursor}')
        seen.add((x, y)); records.append((x, y, tile, sub, level, ice)); cursor += 11
    tail = iso[cursor:]
    if tail and any(tail):
        raise ValueError(f'Unexpected nonzero tile terminator {tail.hex()}')
    overlay, overlay_manifest = chunk_codecs.unpack(doc['overlaypack'], chunk_codecs.lcw)
    overlay_data, data_manifest = chunk_codecs.unpack(doc['overlaydatapack'], chunk_codecs.lcw)
    if len(overlay) != 512 * 512 or len(overlay_data) != 512 * 512:
        raise ValueError('Original overlay planes must be 512x512')
    return doc, records, overlay, overlay_data, {'isoMapPack5': iso_manifest,
        'overlayPack': overlay_manifest, 'overlayDataPack': data_manifest, 'tileRecords': len(records),
        'terminatorBytes': len(tail)}
