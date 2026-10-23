#!/usr/bin/env python3
"""Ten original temperate rock overlays only; independent 1MB exact-range state.
No game implementation, build, game launch, or writes outside the two Rocks trees.
"""
import argparse,hashlib,importlib.util,json,struct,sys
from pathlib import Path
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2]
REF=ROOT/'Assets/Reference/Visual/MapObjects/Rocks'
OUT=ROOT/'Assets/Generated/ClassicExpansion/MapObjects/Rocks'
NAMES=tuple(f'{prefix}ROCK{i:02d}' for prefix in('T','S') for i in range(1,6))
def module():
 spec=importlib.util.spec_from_file_location('bounded_rock_source_pipeline',ROOT/'Tools/classic_art/prepare_map_object_art.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
 m.REF=REF;m.OUT=OUT;m.META=OUT/'metadata.json';m.BUDGET=1_000_000
 m.metadata=lambda:m.readj(m.META)
 m.names_for=lambda record,voxel=False:record['candidates']
 return m

def metadata(m):
 data={'schema':1,'scope':'Only ten actual temperate rock overlay names and one existing-image rubble alias','objects':{name:{'name':name,'kind':'overlay','image':name,'sourceImage':name,'voxel':False,'palette':'isotem','candidates':[name.lower()+'.tem',name.lower()+'.shp'],'layers':[],'anchor':'cell-center'} for name in NAMES}}
 m.savej(m.META,data)

def plan(m):
 metadata(m);p=m.plan()
 for name in NAMES[:5]:
  filename=name.lower()+'.tem';source=ROOT/'Assets/Reference/Visual/Scenery'/filename
  asset=p['assets'].get(filename);assert asset is not None and source.exists(),name
  assert source.stat().st_size==asset['bytes'];asset.update(path=m.rel(source),sha256=m.digest(source),reusedSource=True)
 assert len(p['groups'])==len(p['assets'])==10 and not p['missing']
 assert sum(a['bytes'] for a in p['assets'].values() if 'path' not in a)<=m.BUDGET
 m.savej(REF/'plan.json',p);m.emit(phase='rock-plan',required=10,retainedTrock=5,fetchBytes=sum(a['bytes'] for a in p['assets'].values() if 'path' not in a))

def fetch(m):
 p=m.fetch();ledger=m.readj(REF/'sources.json')
 for a in ledger['assets']:
  offset=a['offset'];remaining=a['bytes'];segments=[]
  while remaining:
   part,position=divmod(offset,70_000_000);count=min(remaining,70_000_000-position)
   segments.append({'url':m.BASE+f'ra2.mix.part{part:02d}','httpRange':f'bytes={position}-{position+count-1}','bytes':count});offset+=count;remaining-=count
  a['sourceRanges']=segments
 m.savej(REF/'sources.json',ledger)

def prepare(m):
 c=m.prepare();notes=[]
 for name in NAMES:
  e=c['objects'][name];s=c['sprites'][e['spriteName']];d=m.readj(ROOT/s['manifest']);w,h=d['canvasWidth'],d['canvasHeight'];f=d['frames'][0]
  origin={'mode':'native-canvas-center','sourceAnchor':{'x':w/2,'y':h/2},'input':'projected ground cell center','doNotRecenterCrop':True}
  e.update(drawFlat=False,origin=origin,nativeCanvas={'width':w,'height':h},nativeColorCrop=f['sourceRect'],shadowLayout=s['shadowLayout'])
  notes.append({'name':name,'spriteName':e['spriteName'],'nativeCanvas':e['nativeCanvas'],'colorCrop':f['sourceRect'],'shadowCrop':d['frames'][f['pairedFrame']]['sourceRect'] if f.get('pairedFrame') is not None else None,'sourceAnchor':origin['sourceAnchor'],'groundRelativeColorBounds':{'x':f['sourceRect']['x']-w/2,'y':f['sourceRect']['y']-h/2,'width':f['sourceRect']['width'],'height':f['sourceRect']['height']}})
 c['objects']['RUBBLE_OVERLAY']={**c['objects']['SROCK01'],'aliasOf':'SROCK01','drawFlat':True}
 c['counts']={'sourceNames':10,'objects':11,'sprites':10,'voxels':0,'layers':0};c['originConvention']='Input is the ground cell center. Preserve native canvas center and original SourceRect offset; no crop-center adjustment. RUBBLE_OVERLAY uses the exact SROCK01 image with drawFlat=true.'
 m.savej(OUT/'catalog.json',c);m.savej(OUT/'origin-notes.json',{'schema':1,'objects':notes,'rubbleAlias':'RUBBLE_OVERLAY=SROCK01, drawFlat=true'})
 text=['# Original overlay rocks','',c['originConvention'],'','All source images use isotem.pal. Native source crops and both color/shadow frames are preserved. Ordinary rocks use native canvas center; these authored canvases place their base near that origin. Final native registration is validated by the caller with its projected CELL CENTER. No raster bounds recenter, synthesis or recoloring.','', '| Name | Canvas | Color crop x,y,w,h | Ground anchor |','|---|---|---|---|']
 for n in notes:
  r=n['colorCrop'];a=n['sourceAnchor'];text.append(f"| {n['name']} | {n['nativeCanvas']['width']}×{n['nativeCanvas']['height']} | {r['x']},{r['y']},{r['width']},{r['height']} | {a['x']},{a['y']} |")
 text+=['','RUBBLE_OVERLAY shares SROCK01 without a duplicate sprite conversion and retains its original DrawFlat flag.','', 'Only this Rocks directory and its independent raw-source Rocks directory are written. Existing cached conversion DLLs are required; no build or native game launch occurs.']
 (OUT/'README.md').write_text('\n'.join(text)+'\n');m.emit(phase='rock-catalog-stable',complete=c['complete'],counts=c['counts'])

def validate(m):
 c=m.readj(OUT/'catalog.json');p=m.readj(REF/'plan.json');assets=p['assets'];native=m.native();png=native.png;checked={'sources':0,'frames':0,'nativeRgbaComparisons':0,'nativeShadowCoverageComparisons':0,'remapMasks':0,'zeroIndexTransparentPixels':0};comparisons=[];pixel_checks=[];largest=0
 assert c['complete'] and len(c['objects'])==11 and len(c['sprites'])==10 and not c['missing'] and not c['voxels']
 assert c['objects']['RUBBLE_OVERLAY']['spriteName']==c['objects']['SROCK01']['spriteName'] and c['objects']['RUBBLE_OVERLAY']['drawFlat']
 assert c['metadataSha256']==m.digest(m.META)==p['metadataSha256']
 for a in assets.values():
  path=ROOT/a['path'];assert path.stat().st_size==a['bytes'] and m.digest(path)==a['sha256'];checked['sources']+=1
 for name,s in c['sprites'].items():
  mp=ROOT/s['manifest'];d=m.readj(mp);assert len(d['images'])==1 and all(f['imageSet']==0 for f in d['frames']);assert d['palette']['sha256']==m.digest(m.PALETTES['isotem'])
  image=d['images'][0];decoded={key:png(mp.parent/image[key],True) for key in('color','indices','teamMask')};aw,ah,_=decoded['indices'];largest=max(largest,aw*ah);assert aw*ah<=16_777_216
  source=next(a for a in assets.values() if a['sha256']==d['sourceSha256']);raw=(ROOT/source['path']).read_bytes()
  for f in d['frames']:
   header,indices=m.frame_indices(raw,f['index']);r=f['imageRect'];x,y,w,h=[r[k] for k in('x','y','width','height')];assert 0<=x<=x+w<=aw and 0<=y<=y+h<=ah
   crop=b''.join(decoded['indices'][2][(y+row)*aw+x:(y+row)*aw+x+w] for row in range(h));assert crop==indices and hashlib.sha256(crop).hexdigest()==f['indicesSha256']
   shadow=f.get('role')=='shadow';assert not shadow or set(crop)<={0,1}
   mask=b''.join(decoded['teamMask'][2][(y+row)*aw+x:(y+row)*aw+x+w] for row in range(h));assert mask==bytes(i-15 if 16<=i<=31 and not shadow else 0 for i in crop);checked['remapMasks']+=1
   rgba=b''.join(decoded['color'][2][((y+row)*aw+x)*4:((y+row)*aw+x+w)*4] for row in range(h))
   for i,index in enumerate(crop):
    if index==0:assert rgba[i*4+3]==0;checked['zeroIndexTransparentPixels']+=1
   if shadow:
    expected=bytes(channel for index in crop for channel in (0,0,0,128 if index else 0));assert rgba==expected;checked['nativeShadowCoverageComparisons']+=1
   else:
    result=native.verify_manifest_frame(mp,ROOT/source['path'],m.PALETTES['isotem'],f['index']);assert result['passed'];comparisons.append(result);checked['nativeRgbaComparisons']+=1
   pixel_checks.append({'sprite':name,'frame':f['index'],'role':f.get('role'),'indicesSha256':hashlib.sha256(crop).hexdigest(),'rgbaSha256':hashlib.sha256(rgba).hexdigest()});checked['frames']+=1
 state=m.readj(REF/'fetch-state.json');assert state['receivedBytes']<=1_000_000
 result={'schema':1,'pass':True,'checked':checked,'largestSpriteAtlasPixels':largest,'sourceNames':10,'aliases':11,'missingRequired':[],'missingOptional':[],'budget':state,'builtToolsOrGame':False,'nativeGameLaunched':False};m.savej(OUT/'validation.json',result);m.savej(REF/'native-pixel-comparisons.json',{'pass':True,'comparisons':comparisons,'allFramePixelChecks':pixel_checks,'shadowSemantics':'Exact original 0/1 coverage, transparent index0 and black alpha128 index1 as the standard independent converter defines.'});m.emit(phase='rock-validation',**result)

def preview(m):
 c=m.readj(OUT/'catalog.json');p=m.readj(REF/'plan.json');writer=m.load('rock_source_png_writer',ROOT/'Tools/classic_art/prepare_cursors.py').png_write
 for name in NAMES:
  s=c['sprites'][c['objects'][name]['spriteName']];d=m.readj(ROOT/s['manifest']);source=next(a for a in p['assets'].values() if a['sha256']==d['sourceSha256']);w,h,rgba=m.native().native_shp_frame(ROOT/source['path'],m.PALETTES['isotem'],0);writer(OUT/'Previews'/f'{name.lower()}.png',w,h,rgba)
 m.emit(phase='rock-preview',files=10)

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--plan',action='store_true');parser.add_argument('--fetch',action='store_true');parser.add_argument('--prepare',action='store_true');parser.add_argument('--validate',action='store_true');parser.add_argument('--preview',action='store_true');a=parser.parse_args();m=module()
 if a.plan:plan(m)
 if a.fetch:fetch(m)
 if a.prepare:prepare(m)
 if a.validate:validate(m)
 if a.preview:preview(m)
if __name__=='__main__':main()
