#!/usr/bin/env python3
"""Build canonical SHP/PAL atlases using the repository's independent C# converter."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import struct

ROOT=Path(__file__).resolve().parents[1]
DLL=Path(os.environ["XDG_CACHE_HOME"])/"redalert-godot-csharp/asset-pipeline/bin/AssetPipeline/debug/Ra2AssetPipeline.dll"
PALETTE=ROOT/"Assets/Reference/Formats/unittem.pal"
SOURCES=[*(('Infantry',n) for n in ('gi','cons','snipe','engineer','adog','dog')),*(('Buildings',n) for n in ('ggpowr','ggpile','ggrefn','ggrefnbb','ggweap','ggweapbb','ngpowr','nghand','ngrefn','ngrefnbb','ngweap','ngweapbb','ggcnst','ngcnst','gggcan','ngtsla','ggpill','ggairc','ngradr','ggtech','ngtech'))]
if not DLL.exists():
    subprocess.run(['zsh','Tools/AssetPipeline/build.zsh'],cwd=ROOT,check=True)
catalog=[]
for category,name in SOURCES:
    source=ROOT/f'Assets/Reference/Battlefield/{category}/{name}.shp'
    destination=ROOT/f'Assets/Generated/Sprites/{name}'
    manifest=destination/'manifest.json'
    digest=hashlib.sha256(source.read_bytes()).hexdigest()
    if manifest.exists():
        metadata=json.loads(manifest.read_text())
        if metadata['sourceSha256']!=digest:
            raise RuntimeError(f'Refusing stale/overwritten asset output: {destination}')
    else:
        result=subprocess.run(['dotnet',str(DLL),'convert','--shp',str(source),'--pal',str(PALETTE),'--out',str(destination),'--layout','atlas','--columns',str(min(24,struct.unpack_from('<H',source.read_bytes(),6)[0])),'--shadow-layout','second-half'],cwd=ROOT,text=True,capture_output=True)
        if result.returncode:
            raise RuntimeError(f'{name}: {result.stderr}\n{result.stdout[-2000:]}')
        metadata=json.loads(manifest.read_text())
    catalog.append({'name':name,'sourceSha256':digest,'frames':len(metadata['frames']),'width':metadata['canvasWidth'],'height':metadata['canvasHeight'],'manifest':str(manifest.relative_to(ROOT))})
    print(f"CONVERTED {name}: {len(metadata['frames'])} frames",flush=True)
output=ROOT/'Assets/Generated/Sprites/catalog.json'
output.write_text(json.dumps({'schema':1,'sprites':catalog},indent=2))
print(f'Prepared {len(catalog)} original SHP atlases with offsets, shadows and remap masks.')
