#!/usr/bin/env python3
"""Compare earlier CnCNet variants against original archive map data."""
import hashlib
import json
from pathlib import Path
import fetch
from map_data import decode, ini

ROOT = Path(__file__).resolve().parents[2]
REF = ROOT / 'Assets/Reference/Maps'


def main():
    module = fetch.reader(); index = module.mix_index('multi.mix'); records = []; chosen = {}
    for label, candidates in [('malibu', ['mp17t6.map']), ('mayday', ['mp31s2.map', 'mp31u2.map'])]:
        name = next(name for name in candidates if module.filename_hash(name) in index)
        record = fetch.retain(module, 'multi.mix', '', index, name, 'Comparison')
        records.append(record); chosen[label] = ROOT/record['path']
    fetch.save_sources(records)
    results = []
    for label, variant in [('malibu','2_malibu_cliffs_le.map'),('mayday','2_mayday_oils_fixed.map')]:
        original_path = chosen[label]; variant_path = ROOT/'Assets/Reference/Terrain/Maps'/variant
        a, ac, ao, ad, _ = decode(original_path); b, bc, bo, bd, _ = decode(variant_path)
        cells_a = {(c[0],c[1]):c[2:] for c in ac}; cells_b = {(c[0],c[1]):c[2:] for c in bc}
        common = set(cells_a) & set(cells_b)
        same = sum(cells_a[p] == cells_b[p] for p in common)
        overlay_names = list(ini(REF/'Metadata/rules.ini')['overlaytypes'].values())
        resource_ids = {i for i,name in enumerate(overlay_names) if name.startswith(('GEM','TIB'))}
        def resources(overlay, data, coords):
            return {(x,y):(overlay[y*512+x],data[y*512+x]) for x,y in coords if overlay[y*512+x] in resource_ids}
        ra = resources(ao,ad,cells_a); rb = resources(bo,bd,cells_b)
        bulk = {'isomappack5','overlaypack','overlaydatapack','previewpack'}
        section_changes = {section:sum(a.get(section,{}).get(key)!=b.get(section,{}).get(key)
            for key in set(a.get(section,{}))|set(b.get(section,{})))
            for section in set(a)|set(b) if section not in bulk and a.get(section,{})!=b.get(section,{})}
        results.append({'original':original_path.relative_to(ROOT).as_posix(),
            'variant':variant_path.relative_to(ROOT).as_posix(),
            'originalSha256':hashlib.sha256(original_path.read_bytes()).hexdigest(),
            'variantSha256':hashlib.sha256(variant_path.read_bytes()).hexdigest(),
            'originalMap':a['map'],'variantMap':b['map'],
            'variantName':b['basic'].get('name',''),'variantAuthor':b['basic'].get('author',''),
            'originalCells':len(ac),'variantCells':len(bc),'sharedCellCoordinates':len(common),
            'identicalTileSubLevelIceOnSharedCoordinates':same,
            'differentTileSubLevelIceOnSharedCoordinates':len(common)-same,
            'identicalTileSubLevelIcePercent':round(100*same/len(common),6),
            'changedNonPackSectionKeyCounts':dict(sorted(section_changes.items())),
            'originalStartingWaypoints':{k:v for k,v in a.get('waypoints',{}).items() if k.isdigit() and int(k)<8},
            'variantStartingWaypoints':{k:v for k,v in b.get('waypoints',{}).items() if k.isdigit() and int(k)<8},
            'allOverlayTypeDifferences':sum(x!=y for x,y in zip(ao,bo)),
            'allOverlayDataDifferences':sum(x!=y for x,y in zip(ad,bd)),
            'originalResourceCells':len(ra),'variantResourceCells':len(rb),
            'resourceCoordinatesOrDensityChanged':len(set(ra)|set(rb))-sum(ra.get(p)==rb.get(p) for p in set(ra)&set(rb)),
            'originalStructures':len(a.get('structures',{})),'variantStructures':len(b.get('structures',{})),
            'classification':'Edited CnCNet variant, not byte-identical official RA2 map.'})
    target = ROOT/'Reports/skirmish-parity/reference-map-comparison.json'
    target.write_text(json.dumps({'schema':1,'maps':results},ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(results,ensure_ascii=False,indent=2))


if __name__ == '__main__':
    main()
