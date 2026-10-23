#!/usr/bin/env python3
"""Measure original LAT edges and connect complete original straight/corner shores.

Only metadata is generated. Source pixels are never synthesized or repainted.
"""
import collections,hashlib,itertools,json,math,struct
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Assets/Reference/Visual/Terrain'
PALETTE=(SOURCE/'isotem.pal').read_bytes()
RGB=[tuple(value<<2 for value in PALETTE[i*3:i*3+3]) for i in range(256)]
WATER=[b>r*1.12 and b>g*.94 and b>24 for r,g,b in RGB]
COORDS=[]
for y in range(30):
    width=4*(y+1) if y<15 else 4*(29-y)
    for x in range((60-width)//2,(60+width)//2):
        u=(x+.5-30)/60+(y+.5-15)/30+.5
        v=(y+.5-15)/30-(x+.5-30)/60+.5
        COORDS.append((u,v))
assert len(COORDS)==900
EDGES=[lambda u,v:u<.20 and .3<v<.7,lambda u,v:v<.20 and .3<u<.7,
       lambda u,v:u>.8 and .3<v<.7,lambda u,v:v>.8 and .3<u<.7]
EDGE_POINTS=[[i for i,(u,v) in enumerate(COORDS) if condition(u,v)] for condition in EDGES]
CENTER=[i for i,(u,v) in enumerate(COORDS) if .28<u<.72 and .28<v<.72]
BINS=12
PROFILE_POINTS=[]
for edge in range(4):
    samples=[[] for _ in range(BINS)]
    for index,(u,v) in enumerate(COORDS):
        normal,tangent=(u,v) if edge in (0,2) else (v,u)
        if (normal<.075 if edge<2 else normal>.925):
            samples[min(BINS-1,max(0,int(tangent*BINS)))].append(index)
    assert all(samples)
    PROFILE_POINTS.append(samples)
SIDE_FRAMES=((0,2),(0,1),(1,3),(2,3))
# Complete original 2x2 templates: straights, convex and concave corners.
# Their orientation is verified against all four source edge profiles below.
SHORE_GROUPS={1:(37,38),2:(35,36),3:(17,18,19),4:(39,40),5:(25,26,27),
              7:(23,24),8:(33,34),10:(9,10,11),11:(15,16),12:(1,2,3),13:(31,32),14:(7,8)}


def template(path):
    data=path.read_bytes();width,height,tw,th=struct.unpack_from('<IIII',data)
    assert (tw,th)==(60,30)
    result={}
    for number in range(width*height):
        offset=struct.unpack_from('<I',data,16+number*4)[0]
        if not offset:continue
        raw=data[offset+52:offset+952];assert len(raw)==900
        profiles=[[sum(WATER[raw[i]] for i in points)/len(points) for points in bins] for bins in PROFILE_POINTS]
        result[number]={'indices':raw,'water':sum(WATER[i] for i in raw)/900,
                        'center':sum(WATER[raw[i]] for i in CENTER)/len(CENTER),
                        'edges':[sum(WATER[raw[i]] for i in points)/len(points) for points in EDGE_POINTS],
                        'profiles':profiles,'height':data[offset+40],'ramp':data[offset+42]}
    return width,height,result


def difference(first,second):
    return sum(abs(a-b) for a,b in zip(first,second))/len(first)


def patch_side(patch,edge):
    return [value for index in SIDE_FRAMES[edge] for value in patch['samples'][index]['profiles'][edge]]


def join(first,second,edge):
    return difference(patch_side(first,edge),patch_side(second,(edge+2)%4))


def coast_metrics(entries,frames,size=96):
    tiles=[('water01',(y&1)*2+(x&1)) if min(x,y,size-1-x,size-1-y)<2 else ('clear01',0) for y in range(size) for x in range(size)]
    used=set()
    for top in range(-1,size,2):
        for left in range(-1,size,2):
            mask=sum(1<<(dy*2+dx) for dy in range(2) for dx in range(2) if min(left+dx,top+dy,size-1-left-dx,size-1-top-dy)<2)
            if mask in (0,15):continue
            entry=entries[mask]
            for dy in range(2):
                for dx in range(2):
                    x,y=left+dx,top+dy
                    if 0<=x<size and 0<=y<size:
                        tiles[y*size+x]=(entry['template'],entry['frames'][dy*2+dx]);used.add(y*size+x)
    errors=[]
    for y in range(size):
        for x in range(size):
            index=y*size+x
            for dx,dy,edge in ((1,0,2),(0,1,3)):
                nx,ny=x+dx,y+dy
                if nx>=size or ny>=size or not (index in used or ny*size+nx in used):continue
                a,b=tiles[index],tiles[ny*size+nx]
                pa=frames[a]['profiles'][edge] if a in frames else [0.0]*BINS
                pb=frames[b]['profiles'][(edge+2)%4] if b in frames else [0.0]*BINS
                errors.append(difference(pa,pb))
    return {'shoreCells':len(used),'testedEdges':len(errors),'meanWaterProfileError':round(sum(errors)/len(errors),6),
            'maxWaterProfileError':round(max(errors),6),'abruptEdgesAboveQuarter':sum(error>.25 for error in errors)}


def main():
    target=ROOT/'Assets/Generated/Terrain/layout.json'
    previous=json.loads(target.read_text()) if target.exists() else None
    clear=collections.Counter(i for path in SOURCE.glob('clear01*.tem') for i in template(path)[2][0]['indices'])
    thick=collections.Counter(i for path in SOURCE.glob('ruff01*.tem') for i in template(path)[2][0]['indices'])
    clear_total=sum(clear.values());thick_total=sum(thick.values())
    weights=[math.log(((thick[i]+.5)/(thick_total+128))/((clear[i]+.5)/(clear_total+128))) for i in range(256)]
    lat_by_mask={};evidence=[]
    for number in range(1,17):
        path=SOURCE/f'clat{number:02d}.tem';raw=template(path)[2][0]['indices']
        scores=[sum(weights[raw[i]] for i in points)/len(points) for points in EDGE_POINTS]
        mask=sum(1<<edge for edge,score in enumerate(scores) if score>0)
        assert mask not in lat_by_mask,(number,mask)
        lat_by_mask[mask]=number
        evidence.append({'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'edgeScores':scores,'mask':mask})
    assert set(lat_by_mask)==set(range(16))
    sand=collections.Counter(i for path in SOURCE.glob('green01*.tem') for i in template(path)[2][0]['indices'])
    sand_total=sum(sand.values())
    sand_weights=[math.log(((sand[i]+.5)/(sand_total+128))/((clear[i]+.5)/(clear_total+128))) for i in range(256)]
    sand_lat={};sand_evidence=[]
    # GLAT01's solid case is rendered with green01, not a transition. Its worn
    # edge is classifier-ambiguous; measure all fifteen runtime transition cases.
    for number in range(2,17):
        path=SOURCE/f'glat{number:02d}.tem';raw=template(path)[2][0]['indices']
        scores=[sum(sand_weights[raw[i]] for i in points)/len(points) for points in EDGE_POINTS]
        mask=sum(1<<edge for edge,score in enumerate(scores) if score>0)
        assert mask not in sand_lat,(number,mask)
        sand_lat[mask]=number
        sand_evidence.append({'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'edgeScores':scores,'mask':mask})
    assert sand_lat=={mask:number for mask,number in lat_by_mask.items() if mask!=15},'Sand GLAT must independently agree with all fifteen runtime LAT orientations.'
    all_frames={};sources={}
    for path in sorted(SOURCE.glob('shore*.tem'))+[SOURCE/'water01.tem']:
        width,height,frames=template(path);sources[path.stem]=(width,height,frames)
        all_frames.update({(path.stem,index):frame for index,frame in frames.items()})
    groups={}
    for mask,numbers in SHORE_GROUPS.items():
        groups[mask]=[]
        for number in numbers:
            name=f'shore{number:02d}';width,height,frames=sources[name]
            assert (width,height)==(2,2) and set(frames)==set(range(4))
            assert all(not frame['height'] and not frame['ramp'] for frame in frames.values())
            patch={'template':name,'frames':list(range(4)),'samples':[frames[i] for i in range(4)]}
            for edge,indices in enumerate(SIDE_FRAMES):
                wet=[bool(mask&(1<<i)) for i in indices];profile=patch_side(patch,edge);mean=sum(profile)/len(profile)
                if all(wet):assert mean>.90,(mask,name,edge,'ocean side not water',mean)
                elif not any(wet):assert mean<.03,(mask,name,edge,'inland side not dry',mean)
            groups[mask].append(patch)
    # Choose the four repeating straights together with their actual corner joints.
    # 44 is the count of joins in each 45-module straight on the current 96x96 map.
    best=None
    for top,bottom,left,right in itertools.product(*(groups[mask] for mask in (3,12,5,10))):
        cost=44*(join(top,top,2)+join(bottom,bottom,2)+join(left,left,3)+join(right,right,3))
        chosen={3:top,12:bottom,5:left,10:right}
        corner_costs={7:lambda p:join(p,top,2)+join(p,left,3),
                      11:lambda p:join(top,p,2)+join(p,right,3),
                      13:lambda p:join(left,p,3)+join(p,bottom,2),
                      14:lambda p:join(right,p,3)+join(bottom,p,2)}
        for mask,score in corner_costs.items():
            patch=min(groups[mask],key=lambda p:(score(p),p['template']));chosen[mask]=patch;cost+=score(patch)
        key=(cost,tuple(chosen[mask]['template'] for mask in sorted(chosen)))
        if best is None or key<best[0]:best=(key,chosen)
    chosen=best[1]
    for mask in (1,2,4,8):chosen[mask]=groups[mask][0]
    shores=[]
    for mask in range(16):
        if mask not in chosen:
            shores.append({'mask':mask,'supported':False,'template':'','frames':[]});continue
        patch=chosen[mask]
        shores.append({'mask':mask,'supported':True,'template':patch['template'],'frames':patch['frames'],
                       'waterFractions':[round(frame['water'],3) for frame in patch['samples']],
                       'sideWaterFractions':[round(sum(patch_side(patch,edge))/(BINS*2),3) for edge in range(4)]})
    after=coast_metrics(shores,all_frames)
    before=previous.get('shoreDerivation',{}).get('before') if previous else None
    if before is None and previous:before=coast_metrics(previous['shoreByMask'],all_frames)
    assert after['abruptEdgesAboveQuarter']==0,after
    profiles=[{'template':name,'frame':index,'waterEdges':[[round(value,6) for value in row] for row in frame['profiles']]}
              for (name,index),frame in sorted(all_frames.items())]
    output={'schema':2,'edgeOrder':['x-','y-','x+','y+'],'latByMask':[lat_by_mask[i] for i in range(16)],
            'latEvidence':evidence,'sandLatEvidence':sand_evidence,'shorePatchSize':2,'shoreByMask':shores,'shoreEdgeProfiles':profiles,
            'shoreDerivation':{'profileBins':BINS,'sampleStripWidth':.075,'referenceMapSize':96,'before':before,'after':after,
                               'selection':'Complete straight and corner templates with all outside directions checked; minimize repeated seams and all eight corner joints.'},
            'notes':['LAT connectivity is measured from original CLAT edge-index distributions; all sixteen masks must be unique.',
                     'Shore profiles follow increasing logical edge coordinates, not image-row order.',
                     'No cropped bends or inland shore41/42 windows are used as straight coast.',
                     'Masks 0/15 are handled by base terrain. Diagonal saddles 6/9 are unsupported, rather than silently using an unrelated corner.',
                     'Water topology comes only from existing simulation terrain; metadata changes no movement or visibility rules.']}
    target.write_text(json.dumps(output,indent=2)+'\n')
    print(json.dumps({'selected':{mask:patch['template'] for mask,patch in sorted(chosen.items())},'before':before,'after':after},indent=2))


if __name__=='__main__':main()
