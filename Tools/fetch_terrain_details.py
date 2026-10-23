#!/usr/bin/env python3
"""Fetch LAT terrain and full shoreline sets using the retained fixed MIX source."""
import importlib.util,json,string
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('terrain_detail_reader',ROOT/'Assets/Reference/Tools/fetch_ra2_samples.py')
r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)
r.MAX_BYTES=12_000_000
root=r.mix_index('ra2.mix');entry=root[r.filename_hash('isotemp.mix')]
index=r.mix_index('ra2.mix',entry[0]);folder='Visual/Terrain'
names=[]
for stem in ('ruff','sandy','green'):
    names.append(stem+'01.tem')
    names += [stem+'01'+letter+'.tem' for letter in string.ascii_lowercase]
for stem,count in [('dlat',16),('glat',16),('plat',16),('shore',42),('pave',2),('proad',4),('droads',8),('droadc',4),('droadj',3)]:
    names += [f'{stem}{i:02d}.tem' for i in range(1,count+1)]
for name in names:
    r.extract('ra2.mix','isotemp.mix',index,name,folder)
path=ROOT/'Assets/Reference/manifest-terrain-details.json'
path.write_text(json.dumps({'retrieval_date':'2026-10-18','transferred_bytes':r.transferred,'assets':r.records},indent=2)+'\n')
print('Terrain detail entries',len(r.records),'transfer bytes',r.transferred,flush=True)
