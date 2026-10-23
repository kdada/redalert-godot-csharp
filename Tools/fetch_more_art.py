#!/usr/bin/env python3
"""Fetch small additional RA2 art entries using exact HTTP ranges; never a full MIX."""
import importlib.util
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
path=ROOT/'Assets/Reference/Tools/fetch_ra2_samples.py'
spec=importlib.util.spec_from_file_location('ra2_range_reader',path)
reader=importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)
root=reader.mix_index('ra2.mix')
groups={
 'generic.mix':('Battlefield/Buildings',['ggcnst.shp','ngcnst.shp','gggcan.shp','ngtsla.shp','ggpill.shp','ggairc.shp','ngradr.shp','ggtech.shp','ngtech.shp']),
 'conquer.mix':('Battlefield/Infantry',['engineer.shp','sengineer.shp','adog.shp','dog.shp']),
 'local.mix':('Battlefield/Voxels',[name+ext for name in ['fv','fvtur','htk','htktur','htkbarl','sref','sreftur','mtnk','mtnktur','mtnkbarl','gtgcantur','gtgcanbarl'] for ext in ['.vxl','.hva']]),
}
for nested,(folder,names) in groups.items():
    entry=root.get(reader.filename_hash(nested))
    if entry is None: raise RuntimeError('Missing '+nested)
    index=reader.mix_index('ra2.mix',entry[0])
    for name in names:
        if not reader.extract('ra2.mix',nested,index,name,folder): print('UNAVAILABLE',name,flush=True)
manifest=reader.ROOT/'manifest-extra.json'
manifest.write_text(json.dumps({'retrieval_date':'2026-10-18','transferred_bytes':reader.transferred,'assets':reader.records},indent=2))
print(f'Fetched {len(reader.records)} additional entries, {reader.transferred} bytes',flush=True)
