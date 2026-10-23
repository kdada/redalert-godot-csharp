#!/usr/bin/env python3
"""Import original official skirmish map data, preserving source coordinates and pixels."""
from collections import Counter, deque
import hashlib
import json
from pathlib import Path
import struct
import zlib
from map_data import ini, csf, decode, chunk_codecs

ROOT = Path(__file__).resolve().parents[2]
REFERENCE = ROOT / 'Assets/Reference/Maps'
OUT = ROOT / 'Assets/Generated/Maps'
REPORT = ROOT / 'Reports/skirmish-parity'


def save_json(path, data, pretty=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2 if pretty else None,
        separators=None if pretty else (',', ':')) + '\n')


def png_rgb(path, width, height, rgb):
    assert len(rgb) == width * height * 3
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    scanlines = b''.join(b'\0' + rgb[y*width*3:(y+1)*width*3] for y in range(height))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
        + chunk(b'IDAT', zlib.compress(scanlines, 9)) + chunk(b'IEND', b''))


def source_templates():
    table = json.loads((REFERENCE / 'tile-sources.json').read_text())['tiles']; result = {}
    for item in table:
        data = (ROOT / item['sourcePath']).read_bytes()
        assert hashlib.sha256(data).hexdigest() == item['sourceSha256']
        bx, by, width, height = struct.unpack_from('<4I', data)
        assert width == 60 and height == 30
        frames = {}
        for index in range(bx*by):
            offset = struct.unpack_from('<I', data, 16 + index*4)[0]
            if not offset:
                continue
            assert offset + 52 <= len(data)
            frames[index] = {'templateHeight': data[offset+40], 'terrainType': data[offset+41],
                'rampType': data[offset+42], 'offsetX': struct.unpack_from('<i', data, offset)[0],
                'offsetY': struct.unpack_from('<i', data, offset+4)[0], 'sourceHeaderOffset': offset}
        result[item['tileIndex']] = {**item, 'blocksX': bx, 'blocksY': by, 'frames': frames}
    save_json(OUT / 'temperate-tiles.json', {'schema': 1, 'theater': 'TEMPERATE', 'tiles': list(result.values())}, True)
    return result


def rectangle(value):
    x, y, width, height = map(int, value.split(','))
    return {'x': x, 'y': y, 'width': width, 'height': height}


def components(points):
    remaining = set(points); result = []
    while remaining:
        first = min(remaining); remaining.remove(first); queue = deque([first]); group = []
        while queue:
            x, y = queue.popleft(); group.append((x, y))
            for point in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
                if point in remaining:
                    remaining.remove(point); queue.append(point)
        result.append(group)
    return result


def import_map(path, templates, rules, pkt, names):
    doc, records, overlay, overlay_data, codec_report = decode(path)
    assert doc['basic'].get('official', '').lower() in ('yes', 'true', '1')
    ident = path.stem; source_size = rectangle(doc['map']['size']); local = rectangle(doc['map']['localsize'])
    info = pkt[ident]; title_key = info['description']; title = names[title_key.lower()]
    maximum_players = int(info['maxplayers']); origin_x = min(cell[0] for cell in records); origin_y = min(cell[1] for cell in records)
    width = max(cell[0] for cell in records) - origin_x + 1; height = max(cell[1] for cell in records) - origin_y + 1
    source_bounds = {'minU': 2*local['x'] - source_size['width'],
        'maxU': 2*(local['x']+local['width']) - source_size['width'],
        'minV': source_size['width'] + 2*local['y'],
        'maxV': source_size['width'] + 2*(local['y']+local['height'])}
    bounds = {'minU': source_bounds['minU'] - origin_x + origin_y,
        'maxU': source_bounds['maxU'] - origin_x + origin_y,
        'minV': source_bounds['minV'] - origin_x - origin_y,
        'maxV': source_bounds['maxV'] - origin_x - origin_y}
    overlay_names = list(rules['overlaytypes'].values())
    bridge_rise = max(frame['templateHeight'] for tile in templates.values() if tile['tileSetId'] in (19,80) for frame in tile['frames'].values())
    terrain_blockers = set()
    for key in doc.get('terrain', {}):
        if key.isdigit():
            pos = int(key); terrain_blockers.add((pos % 1000, pos // 1000))
    cells = []; seen = set(); used_codes = Counter()
    for sx, sy, tile, sub, level, ice in records:
        normalized_tile = 0 if tile in (-1, 65535) else tile
        template = templates[normalized_tile]
        if sub not in template['frames']:
            raise ValueError(f'{ident} references absent original tile/subtile {tile}/{sub}')
        frame = template['frames'][sub]; terrain_type = frame['terrainType']; used_codes[terrain_type] += 1
        if terrain_type not in (8, 9, 10, 11, 12, 13, 14, 15):
            raise ValueError(f'Unmapped source terrain type {terrain_type}')
        ground = 3 if terrain_type == 9 else 2 if terrain_type == 15 else 0 if terrain_type == 13 else 1
        raw_ground = ground
        oid = overlay[sy*512+sx] if 0 <= sx < 512 and 0 <= sy < 512 else 255
        density = overlay_data[sy*512+sx] if 0 <= sx < 512 and 0 <= sy < 512 else 0
        name = overlay_names[oid] if oid != 255 and oid < len(overlay_names) else ''
        low = name.startswith('LOBRDG') and name[6:].isdigit() and int(name[6:]) <= 26
        native_deck = template['tileSetId'] in (19,80) and terrain_type == 11
        high_overlay = name in ('BRIDGE1','BRIDGE2','BRIDGEB1','BRIDGEB2')
        high = native_deck or high_overlay
        surface_level = level + (bridge_rise if high_overlay and not native_deck else 0)
        bridge = low or high
        if bridge:
            ground = 1
        overlay_rule = rules.get(name.lower(), {})
        blocked = overlay_rule.get('wall', '').lower() in ('yes','true','1') or overlay_rule.get('land','').lower() == 'rock'
        land = ground != 3 and ground != 2 and not blocked and (sx, sy) not in terrain_blockers
        water = raw_ground == 3 and not low and not blocked
        resource = 2 if name.startswith('GEM') else 1 if name.startswith('TIB') else 0
        value = int(rules['cruentus' if resource == 2 else 'riparius']['value']) if resource else 0
        x = sx-origin_x; y = sy-origin_y
        playable = bounds['minU'] <= x-y < bounds['maxU'] and bounds['minV'] <= x+y+1 < bounds['maxV']
        cells.append({'x': x, 'y': y, 'sourceX': sx, 'sourceY': sy, 'playable': playable,
            'tileIndex': tile, 'subTile': sub, 'level': level, 'surfaceLevel': surface_level, 'iceGrowth': ice,
            'tileSetId': template['tileSetId'], 'template': template['template'], **{k: frame[k] for k in ('templateHeight','terrainType','rampType')},
            'ground': ground, 'underlyingGround': raw_ground, 'landPassable': land, 'waterPassable': water,
            'buildable': land and terrain_type in (11,12,13,14) and not bridge,
            'bridge': bridge, 'highBridge': high, 'overlayIndex': oid, 'overlayData': density,
            'overlayName': name, 'resourceKind': resource, 'resourceDensity': density if resource else 0, 'resourceValue': value})
        seen.add((x,y))
    lookup = {(cell['x'],cell['y']): cell for cell in cells}
    spawns = []
    for index in range(maximum_players):
        value = int(doc['waypoints'][str(index)]); sx = value % 1000; sy = value // 1000
        spawn = {'index': index, 'x': sx-origin_x, 'y': sy-origin_y, 'sourceX': sx, 'sourceY': sy}
        cell = lookup.get((spawn['x'],spawn['y']))
        assert cell and cell['playable'] and cell['landPassable'], (ident,spawn,cell)
        spawns.append(spawn)
    bridges = []
    for high in (False,True):
        points = {(c['x'],c['y']) for c in cells if c['bridge'] and c['highBridge'] == high}
        for group in components(points):
            xs = [p[0] for p in group]; ys = [p[1] for p in group]
            bridges.append({'id': len(bridges)+1, 'axis': 'X' if max(xs)-min(xs) >= max(ys)-min(ys) else 'Y',
                'high': high, 'minX': min(xs), 'maxX': max(xs), 'minY': min(ys), 'maxY': max(ys),
                'deckLevel': Counter(lookup[p]['surfaceLevel'] for p in group).most_common(1)[0][0],
                'cells': [{'x':x,'y':y} for x,y in sorted(group)]})
    water_points = {(c['x'],c['y']) for c in cells if c['playable'] and c['waterPassable']}
    seas = components(water_points); largest_sea = max(map(len,seas), default=0)
    harbor_candidates = []
    for x,y in sorted(water_points):
        if all((x+dx,y+dy) in water_points for dy in range(3) for dx in range(3)):
            neighbors = [(x+dx,y+dy) for dx,dy in ((-1,1),(3,1),(1,-1),(1,3))]
            if any(lookup.get(p,{}).get('landPassable') for p in neighbors):
                harbor_candidates.append({'x':x,'y':y})
    def shipyard_allowed(key):
        return int(doc.get(key,{}).get('techlevel','3')) >= 0
    shipyards = shipyard_allowed('gayard') and shipyard_allowed('nayard')
    playable_count = sum(c['playable'] for c in cells); fraction = len(water_points)/playable_count
    preview, preview_manifest = chunk_codecs.unpack(doc['previewpack'], chunk_codecs.lzo1x)
    preview_size = rectangle(doc['preview']['size'])
    png_rgb(OUT / 'Previews' / f'{ident}.png', preview_size['width'], preview_size['height'], preview)
    objects = [{'section': section, 'key':key, 'value':value} for section in ('terrain','structures','units','infantry','smudge','aircraft') for key,value in doc.get(section,{}).items()]
    excluded = {'isomappack5','overlaypack','overlaydatapack','previewpack','terrain','structures','units','infantry','smudge','aircraft'}
    source_hash = hashlib.sha256(path.read_bytes()).hexdigest()
    result = {'schema':1, 'id':ident, 'name':title, 'theater':doc['map']['theater'],
        'sourcePath':path.relative_to(ROOT).as_posix(), 'sourceSha256':source_hash,
        'width':width, 'height':height, 'sourceOriginX':origin_x, 'sourceOriginY':origin_y,
        'sourceSize':source_size, 'localSize':local, 'playableBounds':bounds, 'sourcePlayableBounds':source_bounds,
        'runtimeCells':cells, 'spawns':spawns, 'bridges':bridges, 'objects':objects,
        'mapRules':{key:value for key,value in doc.items() if key not in excluded}, 'shipyardsAllowed':shipyards,
        'highBridgeRiseFromOriginalTemplates':bridge_rise,
        'harborCandidates':harbor_candidates, 'notes':[
            'Runtime coordinates are translated only; original source cell coordinates, tile IDs, subtiles, levels, overlays and resource density are preserved.',
            'Playable UV bounds are derived from original LocalSize, with runtime translation; cell centers use V=x+y+1.',
            'Level is the exact original value. For an original BRIDGE overlay on a ground tile, SurfaceLevel adds the rise measured from native Ovrps TMP headers; native Ovrps road/deck cells already carry their deck Level and are not raised twice. UnderlyingGround/Level preserve the boat layer.',
            'ResourceDensity is original OverlayData and ResourceValue is original rules Value; no fabricated resource deposits or start-zone clearing.',
            'Original object and map rule records are data, not executed engine implementation. Multi-level naval underpasses need simulation support.']}
    save_json(OUT / f'{ident}.json', result)
    summary = {'id':ident, 'name':title, 'theater':doc['map']['theater'],
        'dataPath':f'res://Assets/Generated/Maps/{ident}.json', 'previewPath':f'res://Assets/Generated/Maps/Previews/{ident}.png',
        'minPlayers':int(info['minplayers']), 'maxPlayers':maximum_players, 'width':width, 'height':height,
        'navalSuitable':shipyards and largest_sea >= 300 and len(harbor_candidates)>0,
        'waterFraction':round(fraction,6), 'sourceSha256':source_hash}
    report = {**summary, 'sourceSize':source_size, 'localSize':local, 'sourceOrigin':[origin_x,origin_y],
        'decodedCells':len(cells), 'playableCells':playable_count, 'originalTileTypes':len(set(c['tileIndex'] for c in cells)),
        'terrainTypeCounts':dict(used_codes), 'levelCounts':dict(Counter(c['level'] for c in cells)),
        'oreCells':sum(c['resourceKind']==1 for c in cells), 'gemCells':sum(c['resourceKind']==2 for c in cells),
        'overlayCounts':dict(Counter(c['overlayName'] for c in cells if c['overlayIndex'] != 255)),
        'bridges':[{key:value for key,value in b.items() if key != 'cells'} for b in bridges],
        'spawns':spawns, 'shipyardsAllowed':shipyards, 'largestSeaCells':largest_sea, 'harborCandidates':len(harbor_candidates),
        'codec':codec_report, 'preview':{**preview_manifest,'width':preview_size['width'],'height':preview_size['height']}}
    save_json(REPORT / f'{ident}-import.json', report, True)
    print(json.dumps({key:report[key] for key in ('id','name','decodedCells','oreCells','gemCells','shipyardsAllowed','largestSeaCells','harborCandidates','navalSuitable')},ensure_ascii=False),flush=True)
    return summary


def main():
    templates = source_templates()
    rules = ini(REFERENCE / 'Metadata/rules.ini')
    pkt = ini(REFERENCE / 'Metadata/missions.pkt'); names = csf(REFERENCE / 'Metadata/ra2.csf')
    maps = [import_map(path,templates,rules,pkt,names) for path in sorted((REFERENCE / 'Original').glob('*.map'))]
    assert len(maps) >= 3 and any(item['navalSuitable'] for item in maps)
    save_json(OUT / 'catalog.json', {'schema':1,'maps':maps}, True)


if __name__ == '__main__':
    main()
