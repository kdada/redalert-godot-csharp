#!/usr/bin/env python3
"""Necessary import checks: source-byte round trips, original pixels, map semantics."""
from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
from map_data import decode, chunk_codecs

ROOT = Path(__file__).resolve().parents[2]
REF = ROOT / 'Assets/Reference/Maps'
OUT = ROOT / 'Assets/Generated/Maps'


def main():
    for encoded, result in [('1648656c6c6f110000', b'Hello'), ('12414000110000', b'AAAA'),
                            ('164142434445231000110000', b'ABCDEABCDE')]:
        assert chunk_codecs.lzo1x(bytes.fromhex(encoded), len(result)) == result
    rejected = 0
    for bad, size in [(b'\x11\0', 0), (bytes.fromhex('124140ff110000'), 4),
                      (bytes.fromhex('1648656c6c6f110000'), 4)]:
        try:
            chunk_codecs.lzo1x(bad, size)
        except ValueError:
            rejected += 1
    assert rejected == 3
    spec = importlib.util.spec_from_file_location('classic_map_png_validator', ROOT / 'Tools/validate_art.py')
    helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
    sources = json.loads((REF / 'sources.json').read_text())['assets']
    for source in sources:
        assert hashlib.sha256((ROOT/source['path']).read_bytes()).hexdigest() == source['sha256']
    tile_table = json.loads((OUT / 'temperate-tiles.json').read_text())['tiles']
    tiles = {tile['tileIndex']: tile for tile in tile_table}
    for tile in tile_table:
        assert hashlib.sha256((ROOT/tile['sourcePath']).read_bytes()).hexdigest() == tile['sourceSha256']
    catalog = json.loads((OUT / 'catalog.json').read_text())
    assert catalog['schema'] == 1 and len(catalog['maps']) >= 3
    assert len({info['id'] for info in catalog['maps']}) == len(catalog['maps'])
    results = []
    for info in catalog['maps']:
        path = REF/'Original'/(info['id']+'.map')
        original, records, overlay, density, report = decode(path)
        exported = json.loads((ROOT/info['dataPath'].removeprefix('res://')).read_text())
        cells = exported['runtimeCells']; assert len(cells) == len(records)
        rebuilt = b''.join(struct.pack('<hhiBBB', c['sourceX'], c['sourceY'], c['tileIndex'],
            c['subTile'], c['level'], c['iceGrowth']) for c in cells) + b'\0'*report['terminatorBytes']
        assert hashlib.sha256(rebuilt).hexdigest() == report['isoMapPack5']['decodedSha256']
        assert info['sourceSha256'] == hashlib.sha256(path.read_bytes()).hexdigest() == exported['sourceSha256']
        coords = set(); resource_cells = []
        for cell in cells:
            x, y = cell['x'], cell['y']; sx, sy = cell['sourceX'], cell['sourceY']
            assert sx == x+exported['sourceOriginX'] and sy == y+exported['sourceOriginY']
            assert 0 <= x < exported['width'] and 0 <= y < exported['height']
            assert (x,y) not in coords; coords.add((x,y))
            tile = tiles[0 if cell['tileIndex'] in (-1,65535) else cell['tileIndex']]
            frame = tile['frames'][str(cell['subTile'])]
            assert all(cell[key] == frame[key] for key in ('terrainType','rampType','templateHeight'))
            assert cell['tileSetId'] == tile['tileSetId'] and cell['template'] == tile['template']
            assert cell['overlayIndex'] == overlay[sy*512+sx] and cell['overlayData'] == density[sy*512+sx]
            if cell['resourceKind']:
                resource_cells.append((sx,sy,cell['overlayIndex'],cell['overlayData']))
                assert cell['resourceDensity'] == cell['overlayData']
            if cell['waterPassable']:
                assert cell['terrainType'] == 9 and not (cell['bridge'] and not cell['highBridge'])
        for bridge in exported['bridges']:
            bridge_points = {(p['x'],p['y']) for p in bridge['cells']}
            assert all(c['surfaceLevel'] == bridge['deckLevel'] for c in cells if (c['x'],c['y']) in bridge_points)
        assert len(exported['spawns']) == info['maxPlayers']
        lookup = {(cell['x'],cell['y']):cell for cell in cells}
        for spawn in exported['spawns']:
            value = int(original['waypoints'][str(spawn['index'])])
            assert (spawn['sourceX'],spawn['sourceY']) == (value%1000,value//1000)
            assert lookup[(spawn['x'],spawn['y'])]['landPassable'] and lookup[(spawn['x'],spawn['y'])]['playable']
        preview, _ = chunk_codecs.unpack(original['previewpack'], chunk_codecs.lzo1x)
        pw, ph, pixels = helper.png(ROOT/info['previewPath'].removeprefix('res://'), True)
        assert pixels == preview
        assert info['navalSuitable'] is False or exported['shipyardsAllowed'] and len(exported['harborCandidates']) > 0
        for candidate in exported['harborCandidates']:
            assert all(lookup[(candidate['x']+dx,candidate['y']+dy)]['waterPassable'] for dy in range(3) for dx in range(3))
        results.append({'id':info['id'],'cellsRoundTrippedExactly':len(cells),'originalOverlayCoordinatesChecked':len(cells),
            'spawnsExact':len(exported['spawns']),'resourceCellsExact':len(resource_cells),
            'originalPreviewRGBBytesExact':len(preview),'previewSize':[pw,ph],
            'terrainTypeCounts':dict(Counter(cell['terrainType'] for cell in cells))})
    result = {'schema':1,'passed':True,'originalSourcesHashed':len(sources),'originalTemplatesHashed':len(tile_table),
        'malformedLzoStreamsRejected':rejected,'maps':results,'builds':0,'gameLaunches':0}
    target = ROOT/'Reports/skirmish-parity/maps-validation.json'
    target.write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result,indent=2))


if __name__ == '__main__':
    main()
