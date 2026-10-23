#!/usr/bin/env python3
"""Measure ramp corner geometry against original nonzero surface-index silhouettes.

This is an independent geometric fit to data, not an engine implementation.
Base TMP Z data is deliberately NOT interpreted as physical height.
"""
from itertools import product
from pathlib import Path
import json
import struct
import hashlib

CORNER_SIGNATURES = ['0000','0110','0011','1001','1100','0010','0001','1000','0100','0111','1011','1101','1110','0121','1012','2101','1210','0101','1010','0101','1010']
CORNERS = [tuple(map(int, value)) for value in CORNER_SIGNATURES]

ROOT = Path(__file__).resolve().parents[2]
YMIN = -32
HEIGHT = 64
WIDTH = 60
BASE = [(30,-1),(60,14),(30,29),(0,14)]


def native_mask(path):
    data = path.read_bytes(); offset = struct.unpack_from('<I',data,16)[0]; points = set(); cursor = offset+52
    for y in range(29):
        count = 4*(15-abs(14-y)); left = (60-count)//2
        for x in range(count):
            if data[cursor+x]: points.add((left+x,y))
        cursor += count
    flags = struct.unpack_from('<I',data,offset+36)[0]
    if flags & 1:
        x,y,w,h = struct.unpack_from('<4i',data,offset+20)
        start = struct.unpack_from('<I',data,offset+8)[0] or 1852
        extra = data[offset+start:offset+start+w*h]
        for j in range(h):
            for i in range(w):
                if extra[j*w+i]: points.add((x+i,y+j))
    return bitset(points), points


def bitset(points):
    result = 0
    for x,y in points:
        assert 0<=x<WIDTH and YMIN<=y<YMIN+HEIGHT, (x,y)
        result |= 1 << ((y-YMIN)*WIDTH+x)
    return result


def triangle(a,b,c):
    area = (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    if not area: return set()
    sign = 1 if area>0 else -1; result = set()
    for y in range(max(YMIN,min(a[1],b[1],c[1])),min(YMIN+HEIGHT,max(a[1],b[1],c[1])+1)):
        for x in range(max(0,min(a[0],b[0],c[0])),min(WIDTH,max(a[0],b[0],c[0]))):
            px = x+.5
            if all(sign*((q[0]-p[0])*(y-p[1])-(q[1]-p[1])*(px-p[0]))>=0 for p,q in ((a,b),(b,c),(c,a))):
                result.add((x,y))
    return result


def model(corners, diagonal):
    p = [(x,y-height*15) for (x,y),height in zip(BASE,corners)]
    tris = ((0,1,2),(0,2,3)) if diagonal==0 else ((0,1,3),(1,2,3))
    return bitset(set().union(*(triangle(*(p[i] for i in tri)) for tri in tris)))


def edge_evidence():
    out = ROOT/'Assets/Generated/Maps'; results = []
    for info in json.loads((out/'catalog.json').read_text())['maps']:
        doc = json.loads((out/(info['id']+'.json')).read_text())
        cells = {(cell['x'],cell['y']):cell for cell in doc['runtimeCells']}; total = 0; mismatches = 0; by_ramp = {}; examples = []
        for (x,y), a in cells.items():
            if not a['landPassable'] or a['bridge']: continue
            for point, ai, bi in [((x+1,y),(1,2),(0,3)),((x,y+1),(3,2),(0,1))]:
                b = cells.get(point)
                if not b or not b['landPassable'] or b['bridge'] or not (a['rampType'] or b['rampType']): continue
                total += 1
                errors = [a['level']+CORNERS[a['rampType']][i]-b['level']-CORNERS[b['rampType']][j] for i,j in zip(ai,bi)]
                bad = any(errors); mismatches += bad
                for ramp in {a['rampType'],b['rampType']}:
                    counts = by_ramp.setdefault(ramp, {'checkedEdges':0,'matchingEdges':0})
                    counts['checkedEdges'] += 1; counts['matchingEdges'] += not bad
                if bad and len(examples)<8:
                    examples.append({'sourceA':[a['sourceX'],a['sourceY']], 'sourceB':[b['sourceX'],b['sourceY']],
                        'rampA':a['rampType'],'rampB':b['rampType'],'heightDifferences':errors})
        results.append({'id':info['id'],'checkedEdges':total,'matchingEdges':total-mismatches,
            'matchingFraction':(total-mismatches)/total if total else 1,'byRampType':by_ramp,'originalMismatchExamples':examples})
    return results


def main():
    models = [(corners,diagonal,model(corners,diagonal)) for corners in product(range(3),repeat=4) if min(corners)==0 for diagonal in (0,1)]
    rows = []; flat_z = None
    for number in range(21):
        path = ROOT/'Assets/Reference/Terrain/Original'/f'slope{number:02}.tem' if number else ROOT/'Assets/Reference/Visual/Terrain/clear01.tem'
        native, points = native_mask(path)
        data = path.read_bytes(); offset = struct.unpack_from('<I',data,16)[0]; z_data = data[offset+952:offset+1852]
        if number == 0: flat_z = z_data
        assert z_data == flat_z
        scores = sorted([((native^mask).bit_count(),-(native&mask).bit_count(),corners,diagonal,mask.bit_count()) for corners,diagonal,mask in models])
        chosen_error = min((native^model(CORNERS[number], diagonal)).bit_count() for diagonal in (0,1))
        row = {'rampType':number,'corners':CORNERS[number],'nativePixels':len(points),
            'chosenSilhouettePixelDifference':chosen_error,'sourcePath':path.relative_to(ROOT).as_posix(),
            'sourceSha256':hashlib.sha256(data).hexdigest(),'baseZIdenticalToFlat':True,
            'fits':[{'differentPixels':s[0],'corners':s[2],'diagonal':s[3],'modelPixels':s[4]} for s in scores[:4]]}
        rows.append(row); print(json.dumps({'rampType':number,'corners':CORNERS[number],'pixelDifference':chosen_error}),flush=True)
    evidence = {'schema':1,'cornerOrder':['NW=(0,0)','NE=(1,0)','SE=(1,1)','SW=(0,1)'],'nativeLevelPixels':15,
        'projectionBaseCorners':BASE,'rows':rows,'sharedEdgeEvidence':edge_evidence(),
        'notes':[
            'Independent geometric fit to original DATA, not original engine implementation or an unverified public table.',
            'Nonzero material-index support is used for geometric inference only; this does not prescribe alpha conversion of retained TMP pixels.',
            'All 20 native base Z diamonds equal the flat 29-y depth buffer. They cannot establish physical corner heights.',
            'Raster differences of 0..60 pixels include original edge trimming and do not mean the original pixels were changed.',
            'Type13 is edge-on and has only four nonzero material pixels. Its corner tuple is inferred from the rotational plane family measured directly in types14..16; it is absent from this imported map batch.',
            'Types17/19 and18/20 share corners; the internal saddle triangulation is NOT proved by silhouettes and is preserved in raw RampType.',
            'Official map records contain a small number of mismatching source ramp edges. They remain unchanged; the data importer does not smooth or repair them.'
        ]}
    (ROOT/'Reports/skirmish-parity/ramp-geometry.json').write_text(json.dumps(evidence,indent=2)+'\n')
    runtime = {'schema':1,'cornerOrder':evidence['cornerOrder'],'nativeLevelPixels':15,
        'ramps':[{'rampType':row['rampType'],'corners':row['corners']} for row in rows]}
    (ROOT/'Assets/Generated/Maps/ramp-corners.json').write_text(json.dumps(runtime,indent=2)+'\n')


if __name__=='__main__':main()
