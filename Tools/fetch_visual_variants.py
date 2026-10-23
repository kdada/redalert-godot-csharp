#!/usr/bin/env python3
"""Retain original clear-ground alternatives and any available resource/scenery SHP."""
import importlib.util,json,string
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('visual_variants',ROOT/'Assets/Reference/Tools/fetch_ra2_samples.py')
r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)
root=r.mix_index('ra2.mix')
for nested in ('isotemp.mix','conquer.mix','generic.mix','tem.mix','temperat.mix','temperate.mix'):
    entry=root.get(r.filename_hash(nested))
    if not entry:continue
    index=r.mix_index('ra2.mix',entry[0])
    if nested=='isotemp.mix':
        names=['clear01'+letter+'.tem' for letter in string.ascii_lowercase]
        folder='Visual/Terrain'
    else:
        names=[f'{stem}{i:02d}{extension}' for stem,count in [('tree',8),('trock',5),('gold',4)] for i in range(1,count+1) for extension in ('.shp','.tem')]
        folder='Visual/Scenery'
    for name in names:r.extract('ra2.mix',nested,index,name,folder)
    print(nested,'entry_count',len(index),flush=True)
out=ROOT/'Assets/Reference/manifest-visual-variants.json'
out.write_text(json.dumps({'assets':r.records,'transferred_bytes':r.transferred},indent=2)+'\n')
print('Variant files',len(r.records),'transferred',r.transferred,flush=True)
