#!/usr/bin/env python3
"""Pack original official-map TMP pixels and source offsets, without flattening.

Consumes only retained original TMP/PAL and the decoded original map DATA.
No game code is downloaded/copied. Does not build or launch the game.
"""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[2]
INPUT = ROOT / 'Assets/Generated/Maps/temperate-tiles.json'
OUTPUT = ROOT / 'Assets/Generated/ClassicExpansion/Terrain'
PALETTE = ROOT / 'Assets/Reference/Visual/Terrain/isotem.pal'


def sha(value):
    return hashlib.sha256(value).hexdigest()


def png_reader():
    spec = importlib.util.spec_from_file_location('original_map_png_reader', ROOT / 'Tools/validate_art.py')
    module = importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module.png


def png_write(path, width, height, pixels):
    def chunk(kind, value):
        return struct.pack('>I', len(value)) + kind + value + struct.pack('>I', zlib.crc32(kind + value) & 0xffffffff)
    raw = b''.join(b'\0' + pixels[y * width * 4:(y + 1) * width * 4] for y in range(height))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
        + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def original_sources():
    palette = PALETTE.read_bytes();assert len(palette) == 768 and max(palette) <= 63
    # TMP index 0 is empty support inside slope/ramp diamonds as well as extras.
    # Retain original palette RGB, but never turn its blue placeholder opaque.
    colors = [bytes((palette[i*3] << 2, palette[i*3+1] << 2, palette[i*3+2] << 2, 0 if i == 0 else 255)) for i in range(256)]
    mapping = json.loads(INPUT.read_text());tiles = [];layers = []
    for tile in mapping['tiles']:
        data = (ROOT / tile['sourcePath']).read_bytes();assert sha(data) == tile['sourceSha256'], tile['template']
        bx, by, tw, th = struct.unpack_from('<4i', data);assert tw == 60 and th == 30 and bx > 0 and by > 0
        offsets = [struct.unpack_from('<I', data, 16+i*4)[0] for i in range(bx*by)]
        starts = sorted(set(o for o in offsets if o));ends = {s: starts[i+1] if i+1<len(starts) else len(data) for i,s in enumerate(starts)}
        item = {k: tile[k] for k in ('tileIndex','tileSetId','setName','template','sourcePath','sourceSha256')}
        item.update({'blocksX':bx,'blocksY':by,'frames':[],'emptyIndices':[]})
        for index, source in enumerate(offsets):
            if not source:
                item['emptyIndices'].append(index);continue
            assert source >= 16+bx*by*4 and source+1852 <= ends[source]
            x,y,extra_offset,z_offset,extra_z_offset,ex,ey,ew,eh,flags = struct.unpack_from('<2i3I4iI', data, source)
            height,terrain,ramp = data[source+40:source+43]
            gx,gy = (index%bx-index//bx)*30,(index%bx+index//bx)*15
            assert z_offset == 952
            expected = tile['frames'][str(index)]
            assert (height,terrain,ramp,x,y) == (expected['templateHeight'],expected['terrainType'],expected['rampType'],expected['offsetX'],expected['offsetY'])
            frame = {'index':index,'cellX':index%bx,'cellY':index//bx,'sourceHeaderOffset':source,
                'templateHeight':height,'terrainType':terrain,'rampType':ramp,'flags':flags,
                'rawOffset':{'x':x,'y':y},'gridOffset':{'x':gx,'y':gy},'layers':[]}
            def add(kind,width,layer_height,ox,oy,indices,depth):
                assert len(indices) == (900 if kind=='diamond' else width*layer_height)
                rgba = bytearray(width*layer_height*4)
                if kind == 'diamond':
                    position=0
                    for row in range(30):
                        count=0 if row==29 else 4*(15-abs(14-row));left=(60-count)//2
                        for col in range(count):
                            rgba[(row*60+left+col)*4:(row*60+left+col+1)*4] = colors[indices[position]];position+=1
                    assert position==900
                else:
                    for p,value in enumerate(indices):
                        if value:rgba[p*4:p*4+4]=colors[value]
                layer = {'kind':kind,'size':{'width':width,'height':layer_height},'sourceOffset':{'x':ox,'y':oy},
                    'offsetFromCellCenter':{'x':ox-gx-30,'y':oy-gy-15},
                    'offsetFromDiamond':{'x':ox-x,'y':oy-y},'indicesSha256':sha(indices),
                    'depthSha256':sha(depth) if depth else None,'atlasIndex':len(layers)}
                frame['layers'].append(layer);layers.append((layer,rgba,indices,depth))
            add('diamond',60,30,x,y,data[source+52:source+952],data[source+952:source+1852])
            if flags&1:
                assert ew>0 and eh>0 and ew*eh<=16_777_216
                ep=source+(extra_offset or 1852);size=ew*eh;assert ep>=source+1852 and ep+size<=ends[source]
                zp=source+extra_z_offset if extra_z_offset else ep+size
                depth=data[zp:zp+size] if zp+size<=ends[source] else b''
                if extra_z_offset:assert zp>=ep+size and len(depth)==size
                add('extra',ew,eh,ex,ey,data[ep:ep+size],depth)
            item['frames'].append(frame)
        tiles.append(item)
    return tiles,layers


def prepare():
    tiles,layers=original_sources();width=2048;padding=2;x=y=row_height=0
    for layer,_,_,_ in sorted(layers,key=lambda item:(-item[0]['size']['height'],-item[0]['size']['width'],item[0]['atlasIndex'])):
        w,h=layer['size']['width'],layer['size']['height'];assert w+padding*2<=width
        if x+w+padding*2>width:x=0;y+=row_height;row_height=0
        layer['region']={'x':x+padding,'y':y+padding,'width':w,'height':h}
        x+=w+padding*2;row_height=max(row_height,h+padding*2)
    height=y+row_height;assert width*height<=16_777_216
    atlas=bytearray(width*height*4);indices_channel=bytearray();depth_channel=bytearray()
    for layer,rgba,indices,depth in layers:
        r=layer['region'];w,h=r['width'],r['height']
        for row in range(h):
            target=((r['y']+row)*width+r['x'])*4
            atlas[target:target+w*4]=rgba[row*w*4:(row+1)*w*4]
        layer['indicesOffset']=len(indices_channel);layer['indicesLength']=len(indices);indices_channel.extend(indices)
        layer['depthOffset']=len(depth_channel) if depth else None;layer['depthLength']=len(depth);depth_channel.extend(depth)
    OUTPUT.mkdir(parents=True,exist_ok=True)
    png_write(OUTPUT/'atlas.png',width,height,atlas)
    (OUTPUT/'indices.bin').write_bytes(indices_channel);(OUTPUT/'depth.bin').write_bytes(depth_channel)
    catalog={'schema':1,'theater':'TEMPERATE','atlas':'atlas.png','atlasWidth':width,'atlasHeight':height,
        'tileWidth':60,'tileHeight':30,'levelRise':15,'tileIndexSentinels':[-1,65535],
        'sourceMapping':INPUT.relative_to(ROOT).as_posix(),'sourceMappingSha256':sha(INPUT.read_bytes()),
        'palette':{'sourcePath':PALETTE.relative_to(ROOT).as_posix(),'sha256':sha(PALETTE.read_bytes()),'componentBits':6,'expansion':'channel << 2'},
        'indices':'indices.bin','depth':'depth.bin','tiles':tiles,
        'registration':{'input':'projectedCellCenter already includes original map Level * 15 native pixels.',
            'formula':'offsetFromCellCenter = rawLayerOffset - isometricSourceSubTileGridOffset - (30,15). Map.Level already includes templateHeight; never subtract templateHeight again.',
            'diamond':'All 1179 source header offsets equal their source subtile grid offsets; therefore every diamond is centered at (-30,-15). Extra layers retain exact original offsets.',
            'height':'Source templateHeight and raw map Level are retained separately. Source template maxHeight assembly adjustment is not applied after a per-cell Level projection.',
            'foreground':'Base draws diamond then exact native extra. Foreground helper draws only extra by default, with optional original top; caller selects genuinely higher/front cells and excludes subject cell.',
            'pixels':'Native palette index0 is transparent in diamonds and extras. No mesh warp, color replacement, synthetic wall, rescaling of source pixels, or terrain guessing.'}}
    (OUTPUT/'catalog.json').write_text(json.dumps(catalog,indent=2)+'\n')
    print(json.dumps({'templates':len(tiles),'frames':sum(len(t['frames']) for t in tiles),'layers':len(layers),'extraLayers':sum(l[0]['kind']=='extra' for l in layers),'atlas':[width,height]},indent=2),flush=True)


def validate():
    tiles,layers=original_sources();catalog=json.loads((OUTPUT/'catalog.json').read_text());png=png_reader()
    width,height,pixels=png(OUTPUT/'atlas.png',True);indices=(OUTPUT/'indices.bin').read_bytes();depth=(OUTPUT/'depth.bin').read_bytes()
    packed={layer['atlasIndex']:layer for tile in catalog['tiles'] for frame in tile['frames'] for layer in frame['layers']}
    for layer,rgba,source_indices,source_depth in layers:
        p=packed[layer['atlasIndex']];r=p['region'];actual=bytearray()
        for row in range(r['height']):
            start=((r['y']+row)*width+r['x'])*4;actual.extend(pixels[start:start+r['width']*4])
        assert actual==rgba,layer['atlasIndex']
        assert indices[p['indicesOffset']:p['indicesOffset']+p['indicesLength']]==source_indices
        if source_depth:assert depth[p['depthOffset']:p['depthOffset']+p['depthLength']]==source_depth
    lookup={(tile['tileIndex'],frame['index']):frame for tile in catalog['tiles'] for frame in tile['frames']}
    maps=json.loads((ROOT/'Assets/Generated/Maps/catalog.json').read_text());coverage=[]
    for info in maps['maps']:
        path=info['dataPath'].removeprefix('res://');data=json.loads((ROOT/path).read_text());missing=[]
        for cell in data['runtimeCells']:
            key=(0 if cell['tileIndex'] in(-1,65535) else cell['tileIndex'],cell['subTile'])
            if key not in lookup:missing.append(key)
        assert not missing,(info['id'],sorted(set(missing)))
        coverage.append({'id':info['id'],'cells':len(data['runtimeCells']),'missing':0})
    result={'pass':True,'templates':len(tiles),'frames':len(lookup),'layers':len(layers),'atlasDimensions':[width,height],
        'allNativeRgbaMatch':True,'allOriginalIndicesMatch':True,'allRawDepthBytesMatch':True,'mapCoverage':coverage,
        'registration':'Map.Level projected once at15px. No second templateHeight shift. Raw extra-relative offsets retained.',
        'buildOrNativeGameRun':False}
    (OUTPUT/'validation.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2),flush=True)


def preview():
    tiles,layers=original_sources();lookup={(tile['tileIndex'],frame['index']):frame for tile in tiles for frame in tile['frames']};rgba_by_index={layer['atlasIndex']:rgba for layer,rgba,_,_ in layers}
    maps=json.loads((ROOT/'Assets/Generated/Maps/catalog.json').read_text())
    for info in maps['maps']:
        data=json.loads((ROOT/info['dataPath'].removeprefix('res://')).read_text());cells=data['runtimeCells'];by_xy={(c['x'],c['y']):c for c in cells};spawn=data['spawns'][0];land=[c for c in cells if c['playable'] and c['ground']!=3]
        coast=[c for c in land if any(by_xy.get((c['x']+dx,c['y']+dy),{}).get('ground')==3 for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)))]
        cliffs=[c for c in land if any(l['kind']=='extra' for l in lookup[(0 if c['tileIndex'] in(-1,65535) else c['tileIndex'],c['subTile'])]['layers'])]
        def nearest(items):return min(items,key=lambda c:(c['x']-spawn['x'])**2+(c['y']-spawn['y'])**2) if items else None
        for label,center in [('spawn',by_xy.get((spawn['x'],spawn['y']))),('coast',nearest(coast)),('cliff',nearest(cliffs))]:
            if not center:continue
            width,height=960,640;image=bytearray(width*height*4);cx=(center['x']-center['y'])*30;cy=(center['x']+center['y'])*15-center['level']*15
            for cell in sorted(cells,key=lambda c:(c['x']+c['y'],c['x'])):
                px=(cell['x']-cell['y'])*30-cx+width//2;py=(cell['x']+cell['y'])*15-cell['level']*15-cy+height//2
                if px < -300 or px>width+300 or py < -300 or py>height+300:continue
                frame=lookup[(0 if cell['tileIndex'] in(-1,65535) else cell['tileIndex'],cell['subTile'])]
                for layer in frame['layers']:
                    w,h=layer['size']['width'],layer['size']['height'];ox=px+layer['offsetFromCellCenter']['x'];oy=py+layer['offsetFromCellCenter']['y'];rgba=rgba_by_index[layer['atlasIndex']]
                    for row in range(max(0,-oy),min(h,height-oy)):
                        for col in range(max(0,-ox),min(w,width-ox)):
                            p=(row*w+col)*4
                            if rgba[p+3]:
                                target=((oy+row)*width+ox+col)*4;image[target:target+4]=rgba[p:p+4]
            png_write(OUTPUT/'Previews'/f'{info["id"]}-{label}.png',width,height,image)
        print('SOURCE_ASSEMBLY_PREVIEW',info['id'],flush=True)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--prepare',action='store_true');parser.add_argument('--validate',action='store_true');parser.add_argument('--preview',action='store_true');args=parser.parse_args()
    if args.prepare:prepare()
    if args.validate:validate()
    if args.preview:preview()


if __name__=='__main__':main()
