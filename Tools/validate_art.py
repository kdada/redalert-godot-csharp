#!/usr/bin/env python3
"""Check retained source hashes, PNG integrity, SHP index crops and VXL team masks."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zlib

ROOT=Path(__file__).resolve().parents[1]


def png(path,pixels=False):
    data=path.read_bytes();assert data[:8]==b'\x89PNG\r\n\x1a\n',path
    cursor=8;compressed=bytearray();width=height=bits=kind=interlace=None;saw_end=False
    while cursor<len(data):
        length=struct.unpack_from('>I',data,cursor)[0];tag=data[cursor+4:cursor+8];payload=data[cursor+8:cursor+8+length]
        assert len(payload)==length and zlib.crc32(tag+payload)&0xffffffff==struct.unpack_from('>I',data,cursor+8+length)[0],path
        if tag==b'IHDR':width,height,bits,kind,_,_,interlace=struct.unpack('>IIBBBBB',payload)
        if tag==b'IDAT':compressed.extend(payload)
        cursor+=length+12
        if tag==b'IEND':saw_end=True;break
    assert saw_end and cursor==len(data),path
    raw=zlib.decompress(compressed)
    channels={0:1,2:3,3:1,4:2,6:4}[kind]
    stride=(width*bits*channels+7)//8
    if interlace==0:assert len(raw)==height*(stride+1),path
    if not pixels:return width,height,None
    assert bits==8 and interlace==0,path
    rows=[]
    for y in range(height):
        start=y*(stride+1);assert raw[start]==0,'Generated PNG must use its documented filter-none encoding: '+str(path)
        rows.append(raw[start+1:start+1+stride])
    return width,height,b''.join(rows)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--report',default='Reports/art-integrity.json');args=parser.parse_args()
    reference=ROOT/'Assets/Reference';source_count=0;png_count=0;frame_count=0;parts_count=0;source_paths=set()
    for name in ('manifest.json','manifest-extra.json','manifest-visual.json','manifest-visual-variants.json','manifest-terrain-details.json','manifest-details.json','manifest-menu.json'):
        if not (reference/name).exists():continue
        for asset in json.loads((reference/name).read_text())['assets']:
            recorded=asset['path'];path=(ROOT/recorded if recorded.startswith('Assets/') else reference/recorded)
            assert hashlib.sha256(path.read_bytes()).hexdigest()==asset['sha256'],path;source_count+=1;source_paths.add(path)
    for path in (ROOT/'Assets').rglob('*.png'):
        png(path);png_count+=1
    sprites=list((ROOT/'Assets/Generated/Sprites').glob('*/manifest.json'))
    interfaces=list((ROOT/'Assets/Generated/Interface').glob('*/*/manifest.json'))
    details=list((ROOT/'Assets/Generated/Details').glob('*/manifest.json'))
    scenery_shadows=0
    for manifest in sprites+interfaces+details:
        document=json.loads(manifest.read_text());images=[]
        for image in document['images']:
            width,height,pixels=png(manifest.parent/image['indices'],True)
            assert width==image['width'] and height==image['height'],manifest
            images.append((width,height,pixels))
        for frame in document['frames']:
            width,height,pixels=images[frame['imageSet']];rect=frame['imageRect'];x,y,w,h=(rect[k] for k in ('x','y','width','height'))
            assert 0<=x<=x+w<=width and 0<=y<=y+h<=height,manifest
            crop=b''.join(pixels[(y+row)*width+x:(y+row)*width+x+w] for row in range(h))
            assert hashlib.sha256(crop).hexdigest()==frame['indicesSha256'],(manifest,frame['index'])
            if manifest.parent.name.startswith(('tree','trock','gold')) and frame['index']>=len(document['frames'])//2:
                assert set(crop)<={0,1},('Scenery shadow must be the original binary silhouette',manifest,frame['index']);scenery_shadows+=1
            frame_count+=1
    vehicles=list((ROOT/'Assets/Generated/Vehicles').glob('*.json'))
    for manifest in vehicles:
        document=json.loads(manifest.read_text());assert document['directions']==32,manifest
        size=document['atlas']['width']*document['atlas']['height']
        for part in document['parts']:
            indices=(manifest.parent/part['indicesRaw']).read_bytes();team=(manifest.parent/part['teamRaw']).read_bytes();alpha=(manifest.parent/part['alphaRaw']).read_bytes()
            assert len(indices)==len(team)==len(alpha)==size,manifest
            assert all(mask==(index-15 if 16<=index<=31 and coverage else 0) for index,mask,coverage in zip(indices,team,alpha)),(manifest,part['name'])
            assert all(value in (0,255) for value in alpha),manifest
            parts_count+=1
    terrain_frames=0;terrain_pixels={}
    palette=(ROOT/'Assets/Reference/Visual/Terrain/isotem.pal').read_bytes()
    for manifest in (ROOT/'Assets/Generated/Terrain/Tiles').glob('*/manifest.json'):
        document=json.loads(manifest.read_text());source=(ROOT/'Assets/Reference/Visual/Terrain'/document['source']['fileName']).read_bytes()
        assert hashlib.sha256(source).hexdigest()==document['source']['sha256']
        for frame in document['frames']:
            layer=frame['diamond'];original=source[frame['sourceOffset']+52:frame['sourceOffset']+952]
            assert len(original)==900 and hashlib.sha256(original).hexdigest()==layer['indicesSha256']
            assert (manifest.parent/layer['indicesFile']).read_bytes()==original
            width,height,pixels=png(manifest.parent/layer['rgbaPng'],True);assert (width,height)==(60,30)
            decoded=bytearray()
            for y in range(30):
                count=4*(y+1) if y<15 else 4*(29-y)
                left=(60-count)//2
                for x in range(60):
                    offset=(y*60+x)*4;rgba=pixels[offset:offset+4]
                    if left<=x<left+count:
                        index=original[len(decoded)];assert rgba==bytes([palette[index*3]<<2,palette[index*3+1]<<2,palette[index*3+2]<<2,255]);decoded.append(index)
                    else:assert rgba==bytes(4)
            assert bytes(decoded)==original;terrain_frames+=1
            terrain_pixels[(document['source']['fileName'],frame['index'])]=pixels
    terrain_root=ROOT/'Assets/Generated/Terrain'
    catalog=json.loads((terrain_root/'catalog.json').read_text());atlas_width,atlas_height,atlas_pixels=png(terrain_root/catalog['atlas'],True)
    packed=0
    for tile in catalog['tiles']:
        region=tile['region'];x,y,w,h=(region[k] for k in ('x','y','width','height'))
        assert w==60 and h==30 and 0<=x<x+w<=atlas_width and 0<=y<y+h<=atlas_height
        actual=b''.join(atlas_pixels[((y+row)*atlas_width+x)*4:((y+row)*atlas_width+x+w)*4] for row in range(h))
        assert actual==terrain_pixels[(tile['file'],tile['frame'])],tile;packed+=1
    layout=json.loads((terrain_root/'layout.json').read_text());assert set(layout['latByMask'])==set(range(1,17))
    for entry in layout['latEvidence']+layout.get('sandLatEvidence',[]):
        assert sum(1<<i for i,value in enumerate(entry['edgeScores']) if value>0)==entry['mask']
        assert layout['latByMask'][entry['mask']]==int(entry['file'][4:6])
        assert hashlib.sha256((ROOT/'Assets/Reference/Visual/Terrain'/entry['file']).read_bytes()).hexdigest()==entry['sha256']
    for entry in layout['shoreByMask']:
        if entry.get('supported') is False:continue
        document=json.loads((terrain_root/'Tiles'/entry['template']/'manifest.json').read_text());columns=document['template']['blocksX'];numbers=entry['frames']
        assert numbers==[numbers[0],numbers[0]+1,numbers[0]+columns,numbers[0]+columns+1]
        assert all((entry['template']+'.tem',number) in terrain_pixels for number in numbers)
    result={'passed':True,'detailAtlases':len(details),'sceneryShadowFrames':scenery_shadows,'packedTerrainTiles':packed,'latMasksVerified':16,'sandTransitionMasksVerified':len(layout.get('sandLatEvidence',[])),'shoreMasksVerified':sum(entry.get('supported',True) for entry in layout['shoreByMask']),'shoreJoinEvidence':layout['shoreDerivation']['after'],'interfaceAtlases':len(interfaces),'terrainFrames':terrain_frames,'sourceRecords':source_count,'sourceFiles':len(source_paths),'pngFiles':png_count,'spriteAtlases':len(sprites),'spriteFrames':frame_count,'vehicleAtlases':len(vehicles),'voxelParts':parts_count,'facingsPerVehicle':32}
    menu_catalog=ROOT/'Assets/Generated/Menu/catalog.json'
    if menu_catalog.exists():result['menuTextures']=len(json.loads(menu_catalog.read_text())['images'])
    target=(ROOT/args.report).resolve();target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps(result,indent=2))
    print(json.dumps(result));return 0

if __name__=='__main__':raise SystemExit(main())
