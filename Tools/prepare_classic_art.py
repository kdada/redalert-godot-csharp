#!/usr/bin/env python3
"""Convert retained original sidebar SHP and TMP terrain; pack terrain without repainting."""
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import zlib
ROOT=Path(__file__).resolve().parents[1]
CACHE=Path(os.environ['XDG_CACHE_HOME'])/'redalert-godot-csharp'
SHP=CACHE/'asset-pipeline/bin/AssetPipeline/debug/Ra2AssetPipeline.dll'
TMP=CACHE/'terrain-pipeline/bin/TerrainPipeline/debug/Ra2TerrainPipeline.dll'
spec=importlib.util.spec_from_file_location('png_integrity',ROOT/'Tools/validate_art.py')
png_reader=importlib.util.module_from_spec(spec);spec.loader.exec_module(png_reader)


def convert(source,palette,destination,kind):
    manifest=destination/'manifest.json'
    if manifest.exists():
        data=json.loads(manifest.read_text())
        previous=data['sourceSha256'] if kind=='shp' else data['source']['sha256']
        assert previous==hashlib.sha256(source.read_bytes()).hexdigest(),source
        return data
    command=['dotnet',str(SHP if kind=='shp' else TMP),'convert','--'+kind,str(source),'--pal',str(palette),'--out',str(destination)]
    if kind=='shp':command+=['--layout','atlas','--shadow-layout','unspecified']
    result=subprocess.run(command,cwd=ROOT,capture_output=True,text=True)
    if result.returncode:raise RuntimeError(f'{source}: {result.stderr}\n{result.stdout}')
    return json.loads(manifest.read_text())


def write_png(path,w,h,pixels):
    def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
    raw=b''.join(b'\0'+pixels[y*w*4:(y+1)*w*4] for y in range(h))
    data=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b'')
    path.write_bytes(data)


def main():
    interface=[]
    for number,side in [('01','allied'),('02','soviet')]:
        folder=ROOT/f'Assets/Reference/Visual/sidec{number}'
        for source in sorted(folder.glob('*.shp')):
            destination=ROOT/f'Assets/Generated/Interface/{side}/{source.stem}'
            doc=convert(source,folder/'sidebar.pal',destination,'shp')
            interface.append({'side':side,'name':source.stem,'width':doc['canvasWidth'],'height':doc['canvasHeight'],'frames':len(doc['frames'])})
    for source in sorted((ROOT/'Assets/Reference/Visual/Scenery').glob('*.tem')):
        palette=ROOT/'Assets/Reference/Visual/Terrain/isotem.pal'
        destination=ROOT/f'Assets/Generated/Interface/allied/{source.stem}'
        doc=convert(source,palette,destination,'shp')
        interface.append({'side':'allied','name':source.stem,'width':doc['canvasWidth'],'height':doc['canvasHeight'],'frames':len(doc['frames'])})
    output=ROOT/'Assets/Generated/Interface/catalog.json'
    output.write_text(json.dumps({'frames':interface},indent=2)+'\n')
    frames=[]
    for source in sorted((ROOT/'Assets/Reference/Visual/Terrain').glob('*.tem')):
        destination=ROOT/f'Assets/Generated/Terrain/Tiles/{source.stem}'
        doc=convert(source,source.parent/'isotem.pal',destination,'tmp')
        for frame in doc['frames']:
            # Raised tiles and cliff extra data are preserved by the decoder but not flattened into level ground.
            if frame['height']!=0 or frame['rampType']!=0:continue
            frames.append((source.stem,destination/frame['diamond']['rgbaPng'],frame))
    columns=16;cell_w=64;cell_h=34;w=columns*cell_w;h=math.ceil(len(frames)/columns)*cell_h
    atlas=bytearray(w*h*4);groups={};provenance=[]
    for i,(name,path,frame) in enumerate(frames):
        iw,ih,pixels=png_reader.png(path,True);assert(iw,ih)==(60,30)
        x=(i%columns)*cell_w+2;y=(i//columns)*cell_h+2
        for row in range(30):atlas[((y+row)*w+x)*4:((y+row)*w+x+60)*4]=pixels[row*60*4:(row+1)*60*4]
        rect={'x':x,'y':y,'width':60,'height':30}
        prefix=''.join(c for c in name if c.isalpha()).lower()
        group=next((g for stem,g in [('clear','clear'),('ruff','thick'),('sandy','sandy'),('green','sand'),('rough','crops'),('water','water'),('clat','grass-transition'),('dlat','rough-transition'),('glat','sand-transition'),('plat','pave-transition'),('pvclr','pave'),('shore','shore'),('droad','road'),('proad','road'),('pave','pave')] if name.startswith(stem)),prefix)
        groups.setdefault(group,[]).append(rect)
        groups.setdefault(name,[]).append(rect)
        provenance.append({'file':name+'.tem','frame':frame['index'],'cellX':frame['cellX'],'cellY':frame['cellY'],'terrainType':frame['terrainType'],'indicesSha256':frame['diamond']['indicesSha256'],'region':rect})
    terrain=ROOT/'Assets/Generated/Terrain';terrain.mkdir(parents=True,exist_ok=True)
    write_png(terrain/'ground.png',w,h,atlas)
    (terrain/'catalog.json').write_text(json.dumps({'schema':1,'atlas':'ground.png','tileWidth':60,'tileHeight':30,'groups':groups,'tiles':provenance},indent=2)+'\n')
    print(json.dumps({'interface':interface,'terrainTiles':len(frames),'groups':{name:len(items) for name,items in groups.items() if not any(c.isdigit() for c in name)}},indent=2))

if __name__=='__main__':main()
