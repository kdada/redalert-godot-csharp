#!/usr/bin/env python3
"""Fetch original sidebar and temperate art with bounded, exact HTTP ranges."""
import argparse
import importlib.util
import json
import re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('ra2_visual_ranges',ROOT/'Assets/Reference/Tools/fetch_ra2_samples.py')
reader=importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)
reader.MAX_BYTES=25_000_000


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--probe',action='store_true')
    args=parser.parse_args()
    roots={archive:reader.mix_index(archive) for archive in ['ra2.mix','language.mix']}
    names=['local mix database.dat','global mix database.dat','sidebar.pal','sidec01.pal','sidec02.pal','uibkgd.pal','sidestrp.pal','isotem.pal','temperat.ini','rules.ini']
    names += [stem+'.shp' for stem in ['sidebar','side','side1','side2','side3','side4','side5','side6','side7','side8','sidenc01','sidenc02','sidestrp','sidestrp1','sidestrp2','sidearch','sidearch1','sidearch2','sidechev','sidetab','sidecd01','sidecd02','radar','radar01','radar02','bkgd','bkgd1','bkgd2','bkgd3','tab00','tab01','tab02','tab03','tab04','tab05','tab06','tab07','power','repair','sell','pause','clock','clockp','cameo','money']]
    names += [f'{stem}{i:02d}.tem' for stem,count in [('clear',16),('clat',16),('rough',16),('dirt',16),('water',16),('shore',16),('troock',5),('trock',5),('tree',8),('gold',4)] for i in range(1,count+1)]
    names += [f'gold{i:02d}.shp' for i in range(1,5)]
    found={}
    for archive,index in roots.items():
        found[archive]=[name for name in names if reader.filename_hash(name) in index]
        containers=[(name,entry) for name in ['sidec01.mix','sidec02.mix','isotem.mix','isotemp.mix','tem.mix','temperat.mix','cache.mix','local.mix','conquer.mix','generic.mix'] if (entry:=index.get(reader.filename_hash(name)))]
        for parent,parent_entry in list(containers):
            if parent not in ('tem.mix','cache.mix'):continue
            parent_index=reader.mix_index(archive,parent_entry[0])
            for name in ['isotem.mix','isotemp.mix','temperat.mix','tem.mix','unittem.pal','isotem.pal']:
                entry=parent_index.get(reader.filename_hash(name))
                if not entry:continue
                if name.endswith('.pal') and not args.probe:
                    reader.extract(archive,parent,parent_index,name,'Visual/Terrain')
                elif name.endswith('.mix'):containers.append((name,entry))
        for nested,entry in containers:
            nested_index=reader.mix_index(archive,entry[0])
            available=[name for name in names if reader.filename_hash(name) in nested_index]
            key=archive+'/'+nested
            found[key]=available
            print(key, 'entries',len(nested_index),'found',available,flush=True)
            # Filename metadata is not executable game code; preserve it for provenance.
            database=nested_index.get(reader.filename_hash('local mix database.dat'))
            if database:
                raw=reader.fetch(archive,*database)
                listed=re.findall(rb'[A-Za-z0-9_. -]+\.(?:shp|pal|tem|ini|pcx)',raw,re.I)
                found[key+'/names']=[name.decode('ascii') for name in listed]
                print('DATABASE',key,'filenames',len(listed),flush=True)
            if args.probe:continue
            selected=[]
            if nested.startswith('sidec'):
                selected=[name for name in available if name.endswith(('.pal','.shp'))]
                folder='Visual/'+nested[:-4]
            elif nested in ('isotem.mix','isotemp.mix'):
                selected=[name for name in available if name.endswith('.tem') and not name.startswith(('tree','gold','trock','troock'))]
                folder='Visual/Terrain'
            elif nested=='tem.mix':
                selected=[name for name in available if name.endswith('.pal') or name.startswith(('tree','gold','trock'))]
                folder='Visual/Temperate'
            elif nested in ('local.mix','conquer.mix'):
                selected=[name for name in available if name in ('temperat.ini','rules.ini') or name.startswith('gold')]
                folder='Visual/Metadata' if nested=='local.mix' else 'Visual/Overlays'
            else:continue
            for name in selected:reader.extract(archive,nested,nested_index,name,folder)
    destination=ROOT/'Assets/Reference/manifest-visual-probe.json' if args.probe else ROOT/'Assets/Reference/manifest-visual.json'
    destination.write_text(json.dumps({'retrieval_date':'2026-10-18','transferred_bytes':reader.transferred,'found':found,'assets':reader.records},indent=2)+'\n')
    print('Transferred',reader.transferred,'bytes; extracted',len(reader.records),'assets',flush=True)

if __name__=='__main__':main()
