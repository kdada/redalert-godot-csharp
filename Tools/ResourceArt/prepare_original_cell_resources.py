#!/usr/bin/env python3
"""Authoritative original five-map TIB/GEM cell artwork. No GOLD substitution.
Writes only Assets/Generated/Resources/OriginalCell; never builds or runs games.
"""
import argparse,collections,hashlib,importlib.util,json,struct,sys
from pathlib import Path
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Generated/Resources/OriginalCell'
REF=OUT/'Sources'
PALETTE='temperat'
PALETTE_SHA256='5903b69868b84f494cfbb4e7100398015ef9775b37726019a0d7b5fb6cb33b55'

def module():
 s=importlib.util.spec_from_file_location('original_cell_source_pipeline',ROOT/'Tools/classic_art/prepare_map_object_art.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
 m.OUT=OUT;m.REF=REF;m.META=OUT/'metadata.json';m.BUDGET=1_000_000;m.metadata=lambda:m.readj(m.META);m.names_for=lambda record,voxel=False:record['candidates']
 m.PALETTES[PALETTE]=REF/'Original'/'temperat.pal'
 # Empty second halves are empty frames, not evidence of a shadow convention.
 def layout(path):
  raw=path.read_bytes();count=struct.unpack_from('<H',raw,6)[0];audit=[]
  for i in range(count):
   _,indices=m.frame_indices(raw,i);audit.append({'index':i,'empty':not any(indices),'opaquePixels':sum(v!=0 for v in indices),'onlyShadowIndices':set(indices)<={0,1}})
  second=count%2==0 and any(not a['empty'] for a in audit[count//2:]) and all(a['onlyShadowIndices'] for a in audit[count//2:]) and any(not a['onlyShadowIndices'] for a in audit[:count//2])
  return ('second-half' if second else 'unspecified'),audit
 m.shadow_layout=layout
 return m

def derive(m):
 rules=m.ini(ROOT/'Assets/Reference/Visual/Metadata/rules.ini');art=m.ini(ROOT/'Assets/Reference/Details/art.ini');images={};counts=collections.Counter();objects={};maps=[]
 for info in m.readj(ROOT/'Assets/Generated/Maps/catalog.json')['maps']:
  data=m.readj(ROOT/info['dataPath'].removeprefix('res://'));per=collections.Counter()
  for cell in data['runtimeCells']:
   resource=cell.get('resourceKind',0)
   if not resource:continue
   assert resource in(1,2);name=cell['overlayName'];assert name in rules
   rule=dict(rules[name]);image=rule.get('Image',name);a=dict(art[image]) if image in art else {};source=a.get('Image',image);kind='ore' if resource==1 else 'gems';key=source.lower()
   assert source.startswith('TIB') if resource==1 else source=='GEM01'
   assert 'GOLD' not in source.upper()
   prior=images.setdefault(name,key);assert prior==key;counts[name]+=1;per[kind]+=1
   if source not in objects:objects[source]={'name':source,'kind':kind,'image':source,'sourceImage':source,'voxel':False,'palette':PALETTE,'candidates':[key+'.tem',key+'.shp'],'layers':[],'originalRules':dict(rules[image]) if image in rules else rule,'originalArt':a}
  maps.append({'map':info['id'],'oreCells':per['ore'],'gemCells':per['gems']})
 result={'schema':1,'scope':'Only resourceKind>0 images in the five retained original maps; literal Rules.Image then Art.Image; no GOLD substitutions','sourceRulesSha256':m.digest(ROOT/'Assets/Reference/Visual/Metadata/rules.ini'),'sourceArtSha256':m.digest(ROOT/'Assets/Reference/Details/art.ini'),'objects':dict(sorted(objects.items())),'overlayImages':dict(sorted(images.items())),'overlayCounts':dict(sorted(counts.items())),'mapCounts':maps}
 assert len(objects)==12 and len(images)==18
 m.savej(m.META,result);m.emit(phase='authoritative-metadata',sourceNames=list(result['objects']),overlayImages=result['overlayImages'],mapCounts=maps);return result

def plan(m):
 derive(m);p=m.plan();assert len(p['assets'])==12 and not p['missing']
 # Resource overlays use the original theater palette TEMPERAT, not the TMP
 # palette ISOTEM or actor palette UNITTEM. Retain it in the exact-range ledger.
 indexes=m.readj(ROOT/'Assets/Reference/ClassicExpansion/Indexes/catalog.json')
 index=next(i for i in indexes if i['archive']=='ra2.mix' and i['nested']=='cache.mix')
 offset,size=index['index'][str(m.filename_hash('temperat.pal'))];assert size==768
 palette={'name':'temperat.pal','archive':'ra2.mix','nested':'cache.mix','offset':offset,'bytes':size,'kind':'palette'}
 path=REF/'Original'/'temperat.pal'
 if path.exists():
  assert m.digest(path)==PALETTE_SHA256;palette.update(path=m.rel(path),sha256=PALETTE_SHA256,reusedSource=False)
 p['assets']['temperat.pal']=palette
 # Never rely on another task's mutable resource output or source cache.
 for a in p['assets'].values():
  if 'path' in a and not (ROOT/a['path']).is_relative_to(OUT):
   for key in('path','sha256','reusedSource'):a.pop(key,None)
 assert sum(a['bytes'] for a in p['assets'].values() if 'path' not in a)<=m.BUDGET
 m.savej(REF/'plan.json',p)

def fetch(m):
 m.fetch();ledger=m.readj(REF/'sources.json')
 for asset in ledger['assets']:
  offset=asset['offset'];remaining=asset['bytes'];segments=[]
  while remaining:
   part,position=divmod(offset,70_000_000);n=min(remaining,70_000_000-position);segments.append({'url':m.BASE+f'ra2.mix.part{part:02d}','httpRange':f'bytes={position}-{position+n-1}','bytes':n});offset+=n;remaining-=n
  asset['sourceRanges']=segments
 m.savej(REF/'sources.json',ledger)

def audit_source(m,source):
 raw=source.read_bytes();zero,w,h,count=struct.unpack_from('<4H',raw);assert zero==0 and (w,h,count)==(60,60,24),(source.name,w,h,count)
 frames=[]
 for i in range(count):
  header,indices=m.frame_indices(raw,i);x,y,fw,fh=struct.unpack_from('<4H',raw,8+i*24)
  frames.append({'index':i,'sourceRect':{'x':x,'y':y,'width':fw,'height':fh},'groundRelativeCrop':{'x':x-w/2,'y':y-(h/2+15),'width':fw,'height':fh},'canvasCenterRelativeCrop':{'x':x-w/2,'y':y-h/2,'width':fw,'height':fh},'opaquePixels':sum(v!=0 for v in indices),'empty':not any(indices),'onlyShadowIndices':set(indices)<={0,1},'indicesSha256':hashlib.sha256(indices).hexdigest()})
 density=[f['opaquePixels'] for f in frames[:12]];tail=frames[12:]
 classification='empty' if all(f['empty'] for f in tail) else 'shadow-mask' if all(f['onlyShadowIndices'] for f in tail) else 'color'
 return {'canvas':{'width':w,'height':h},'frames':frames,'densityOpaquePixels':density,'densityMonotonicNondecreasing':all(a<=b for a,b in zip(density,density[1:])),'tail':{'indices':list(range(12,24)),'classification':classification,'opaquePixels':[f['opaquePixels'] for f in tail]}}

def prepare(m):
 assert m.digest(m.PALETTES[PALETTE])==PALETTE_SHA256
 c=m.prepare();meta=m.metadata();p=m.readj(REF/'plan.json');audits={}
 # The shared producer preserves earlier wrong-palette files as unused cache.
 # Publish stable raw-image identities, with manifests pointing to new TEMPERAT
 # conversions; consumer APIs/density selectors therefore remain unchanged.
 identities={key:Path(m.readj(ROOT/item['manifest'])['sourceFile']).stem.lower() for key,item in c['sprites'].items()}
 c['sprites']={identities[key]:item for key,item in c['sprites'].items()}
 for item in c['objects'].values():
  if 'spriteName'in item:item['spriteName']=identities[item['spriteName']]
 for name,item in c['sprites'].items():
  doc=m.readj(ROOT/item['manifest']);source=next(a for a in p['assets'].values() if a['sha256']==doc['sourceSha256']);audit=audit_source(m,ROOT/source['path']);audits[name]=audit
  assert len(doc['frames'])==24 and len(doc['images'])==1
  kind=meta['objects'][name.upper()]['kind'];item.update(kind=kind,nativeCanvas=audit['canvas'],nativeCanvasCenter={'x':30,'y':30},nativeAnchor={'x':30,'y':45},groundCenterOffsetFromCanvasCenter={'x':0,'y':15},densityFrames=list(range(12)),densityOpaquePixels=audit['densityOpaquePixels'],densityMonotonicNondecreasing=audit['densityMonotonicNondecreasing'],densityOrderByOpaquePixels=sorted(range(12),key=lambda i:audit['densityOpaquePixels'][i]),tailFrames=audit['tail'],allNativeFrames=list(range(24)),anchorConvention='ground cell center = native canvas center + (0,15); native lower-half diamond spans y30..60; preserve original sourceRect; no crop recenter')
 c['paletteSelection']={'name':'temperat.pal','sha256':PALETTE_SHA256,'scope':'original theater resource overlay palette, distinct from TMP isotem and actor unittem','proof':'same original TIB01 index12: isotem0,124,244 versus temperat252,244,96; native TIB becomes gold and GEM becomes multicolor'}
 c.update(overlayImages=meta['overlayImages'],defaultOre=[k for k,s in c['sprites'].items() if s['kind']=='ore'],defaultGems=[k for k,s in c['sprites'].items() if s['kind']=='gems'],missingRequired=[],missingOptional=[],counts={'sprites':len(c['sprites']),'overlayNames':len(meta['overlayImages']),'originalFrames':sum(len(a['frames']) for a in audits.values()),'oreCells':sum(i['oreCells'] for i in meta['mapCounts']),'gemCells':sum(i['gemCells'] for i in meta['mapCounts'])},sourceIdentity='Original Rules.Image or name, then original Art.Image or image; TIB2 alias preserved; no GOLD family',nativeAnchorConvention='All authored canvases are 60x60; canvas center is (30,30), authored cell diamond occupies y30..60 and its ground center is (30,45). For projected CELL CENTER use nativeAnchor(30,45), preserving sourceRect. source-frame-audit.json records bounds relative to both the canvas center and ground anchor; no bbox recenter.')
 assert len(c['sprites'])==12 and len(c['defaultOre'])==11 and c['defaultGems']==['gem01']
 m.savej(OUT/'source-frame-audit.json',{'schema':1,'sources':audits});m.savej(OUT/'catalog.json',c)
 text=['# Authoritative original cell resources','','Only the actual five-map resource images are included. Resolve original Rules.Image or overlay name, then original Art.Image or that image. TIB2_## aliases TIB##; GEM01 stays GEM01. No GOLD family fallback or substitution is present.','','Twelve original 60x60 SHPs retain all 24 original frames. Frames0..11 are original density states; every frame12..23 was independently decoded and is EMPTY, not a real shadow. Manifests use shadowLayout=unspecified; renderer must use the explicit densityFrames list rather than assume all24 are density colors.','','## Ground registration','','Native canvas center is (30,30). The authored isometric ground diamond is in the lower half, nominal x0..60/y30..60. For a supplied projected CELL CENTER the source anchor is (30,45) = canvas center+(0,15). TIB01 full density crop(4,31,52,25) and GEM01(7,31,42,26) lie around this plane. Using anchor(30,30) shifts these ground piles15px forward. Preserve original sourceRect offsets; do not recenter their cropped bounding boxes. Every raw crop and its bounds relative to BOTH pivots are retained in source-frame-audit.json. Actual final scene registration is the parent renderer/native check.','','## Density audit','','All original indices and order are preserved. Eleven of twelve images monotonically gain opaque pixels through frames0..11. TIB02 is an original exception: frame0 has414 opaque pixels, frames1..11 have7..341. This is actual retained original data, not corrected/reordered in the image. densityFrames remains0..11. densityOrderByOpaquePixels records the measured order separately if the renderer needs it.','','| Image | Source crop of frame11 | Opaque pixels by original frames0..11 | Tail |','|---|---|---|---|']
 for name,audit in audits.items():
  rect=audit['frames'][11]['sourceRect'];text.append('| '+name+' | '+','.join(str(rect[k]) for k in('x','y','width','height'))+' | '+','.join(map(str,audit['densityOpaquePixels']))+' | '+audit['tail']['classification']+' |')
 text+=['','## Reproduction','','python3 Tools/ResourceArt/prepare_original_cell_resources.py --plan --fetch --prepare --validate --preview','','Source SHA256/size, pinned MIX offsets and exact HTTP206 ranges are in Sources/sources.json. Original TEMPERAT.PAL (SHA256 '+PALETTE_SHA256+') is separately retained in the same exact-range source ledger and bound by every standard manifest. The earlier ISOTEM choice produced rainbow ore/gray gems despite preserving all original indices; UNITTEM also produces invalid magenta pixels. TEMPERAT correctly gives gold TIB and multicolor GEM, with all raw SHPs, frame order, crop offsets, anchors and density metadata preserved. validation.json independently compares all288 native frame-index arrays, all288 native RGBA frames, all288 remap masks, zero-index alpha and PNG CRC/dimensions. The helper uses only existing cached conversion DLLs. No tools/game build, Native launch or writes to any other Resources directory occur.']
 (OUT/'README.md').write_text('\n'.join(text)+'\n');m.emit(phase='CATALOG-STABLE',complete=c['complete'],counts=c['counts'],defaultOre=c['defaultOre'],defaultGems=c['defaultGems'],groundAnchor={'x':30,'y':45})

def validate(m):
 c=m.readj(OUT/'catalog.json');p=m.readj(REF/'plan.json');meta=m.metadata();native=m.native();checked=collections.Counter();comparison=[];largest=0;zero_rgba=0
 assert c['complete'] and not c['missing'] and len(c['sprites'])==12 and c['metadataSha256']==m.digest(m.META)==p['metadataSha256']
 assert c['overlayImages']==meta['overlayImages'] and all(k in c['sprites'] for k in c['overlayImages'].values())
 for a in p['assets'].values():assert (ROOT/a['path']).stat().st_size==a['bytes'] and m.digest(ROOT/a['path'])==a['sha256'];checked['sources']+=1
 for name,item in c['sprites'].items():
  mp=ROOT/item['manifest'];d=m.readj(mp);assert len(d['images'])==1 and len(d['frames'])==24 and (d['canvasWidth'],d['canvasHeight'])==(60,60);assert d['palette']['sha256']==m.digest(m.PALETTES[PALETTE]);image=d['images'][0];decoded={k:native.png(mp.parent/image[k],True) for k in('color','indices','teamMask')};aw,ah,_=decoded['indices'];largest=max(largest,aw*ah);assert aw*ah<=16_777_216
  source=next(a for a in p['assets'].values() if a['sha256']==d['sourceSha256']);raw=(ROOT/source['path']).read_bytes();audit=audit_source(m,ROOT/source['path']);assert item['densityOpaquePixels']==audit['densityOpaquePixels'] and item['tailFrames']==audit['tail']
  for f in d['frames']:
   _,original=m.frame_indices(raw,f['index']);rect=f['imageRect'];x,y,w,h=[rect[k] for k in('x','y','width','height')];assert 0<=x<=x+w<=aw and 0<=y<=y+h<=ah and f['imageSet']==0
   crop=b''.join(decoded['indices'][2][(y+j)*aw+x:(y+j)*aw+x+w] for j in range(h));assert crop==original and hashlib.sha256(crop).hexdigest()==f['indicesSha256'];checked['frameIndices']+=1
   rgba=b''.join(decoded['color'][2][((y+j)*aw+x)*4:((y+j)*aw+x+w)*4] for j in range(h));mask=b''.join(decoded['teamMask'][2][(y+j)*aw+x:(y+j)*aw+x+w] for j in range(h));shadow=f.get('role')=='shadow';assert mask==bytes(i-15 if 16<=i<=31 and not shadow else 0 for i in crop);checked['remapMasks']+=1
   for j,v in enumerate(crop):
    assert rgba[j*4+3]==(128 if shadow and v else 255 if v else 0)
    if not v:zero_rgba+=1
   if shadow:assert set(crop)<={0,1};checked['shadowCoverage']+=1
   else:
    result=native.verify_manifest_frame(mp,ROOT/source['path'],m.PALETTES[PALETTE],f['index']);assert result['passed'];comparison.append(result);checked['nativeRgbaComparisons']+=1
  assert item['densityFrames']==list(range(12)) and item['nativeAnchor']=={'x':30,'y':45} and item['nativeCanvasCenter']=={'x':30,'y':30}
 state=m.readj(REF/'fetch-state.json');assert state['receivedBytes']<=1_000_000
 result={'schema':1,'pass':True,'checked':dict(checked),'zeroIndexTransparentPixels':zero_rgba,'largestAtlasPixels':largest,'missingRequired':[],'missingOptional':[],'budget':state,'allOriginalFramesPreserved':True,'originalPalette':PALETTE,'paletteSha256':PALETTE_SHA256,'noGoldSubstitution':True,'noBuildOrNativeLaunch':True}
 m.savej(OUT/'validation.json',result);m.savej(OUT/'native-pixel-comparisons.json',{'pass':True,'comparisons':comparison});m.emit(phase='validation',**result)

def preview(m):
 c=m.readj(OUT/'catalog.json');p=m.readj(REF/'plan.json');writer=m.load('original_cell_png_writer',ROOT/'Tools/classic_art/prepare_cursors.py').png_write
 for name in('tib01','tib07','gem01'):
  d=m.readj(ROOT/c['sprites'][name]['manifest']);source=next(a for a in p['assets'].values() if a['sha256']==d['sourceSha256'])
  for i in(0,5,11):
   w,h,rgba=m.native().native_shp_frame(ROOT/source['path'],m.PALETTES[PALETTE],i);writer(OUT/'Previews'/f'{name}-{i:02}.png',w,h,rgba)
 m.emit(phase='preview',files=9)

def main():
 parser=argparse.ArgumentParser()
 for flag in('plan','fetch','prepare','validate','preview'):parser.add_argument('--'+flag,action='store_true')
 args=parser.parse_args();m=module()
 for flag,fn in(('plan',plan),('fetch',fetch),('prepare',prepare),('validate',validate),('preview',preview)):
  if getattr(args,flag):fn(m)
if __name__=='__main__':main()
