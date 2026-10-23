#!/usr/bin/env python3
"""Bounded original-map object DATA extraction. Never builds tools or runs a game."""
import argparse,concurrent.futures,configparser,hashlib,importlib.util,json,os,struct,subprocess,sys,threading,zlib
from collections import Counter
from pathlib import Path
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2]
REF=ROOT/'Assets/Reference/Visual/MapObjects'
OUT=ROOT/'Assets/Generated/ClassicExpansion/MapObjects'
META=OUT/'metadata.json'
REV='acfa5b834899e7fab4c8499ccb4b49c402927b44'
BASE=f'https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/{REV}/game-res/'
BUDGET=32_000_000
CACHE=Path(os.environ['XDG_CACHE_HOME'])
SHP=CACHE/'redalert-godot-csharp/asset-pipeline/bin/AssetPipeline/debug/Ra2AssetPipeline.dll'
VXL=CACHE/'redalert-voxel-baker/bin/Debug/net10.0/VoxelBaker.dll'
PALETTES={'isotem':ROOT/'Assets/Reference/Visual/Terrain/isotem.pal','unittem':ROOT/'Assets/Reference/Formats/unittem.pal'}
LOCK=threading.Lock()
def readj(p,default=None):return json.loads(p.read_text()) if p.exists() else default
def savej(p,d):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,indent=2)+'\n')
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def rel(p):return p.relative_to(ROOT).as_posix()
def emit(**d):print(json.dumps(d),flush=True)
def load(name,p):
 s=importlib.util.spec_from_file_location(name,p);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
def filename_hash(name):
 data=name.upper().encode('ascii');remainder=len(data)%4
 if remainder:data+=bytes([remainder])+data[len(data)//4*4:len(data)//4*4+1]*(3-remainder)
 return zlib.crc32(data)
def original_write(p,raw):
 if p.exists():assert p.read_bytes()==raw,p
 else:p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(raw)
def ini(p):
 c=configparser.ConfigParser(strict=False,interpolation=None,allow_no_value=True,inline_comment_prefixes=(';',));c.optionxform=str;c.read(p);return c

def metadata():
 if META.exists():
  d=readj(META);assert d['schema']==1 and len(d['objects'])==91;return d
 # A read-only base-source plan can run while the parent prepares the richer metadata.
 rules=ini(ROOT/'Assets/Reference/Visual/Metadata/rules.ini');art=ini(ROOT/'Assets/Reference/Details/art.ini');objects={}
 for info in readj(ROOT/'Assets/Generated/Maps/catalog.json')['maps']:
  for record in readj(ROOT/info['dataPath'].removeprefix('res://'))['objects']:
   section=record['section'];fields=record['value'].split(',');name=fields[1] if section in ('structures','units','infantry','aircraft') else fields[0]
   if name not in objects:
    rule=dict(rules[name]) if name in rules else {};image=rule.get('Image',name);a=dict(art[image]) if image in art else {};source=a.get('Image',image)
    objects[name]={'name':name,'kind':{'structures':'structure','units':'unit'}.get(section,section),'image':image,'sourceImage':source,'voxel':a.get('Voxel','').lower()=='yes','palette':'isotem' if section in ('terrain','smudge') else 'unittem','layers':[],'instanceCount':0}
   objects[name]['instanceCount']+=1
 return {'schema':1,'provisionalBasesOnly':True,'objects':objects}

def names_for(record,voxel=False):
 result=[]
 for candidate in record.get('candidates',[]):
  if isinstance(candidate,str):result.append(candidate.lower())
 for key in ('sourceImage','image','name','artSection','sourceFileId'):
  stem=record.get(key)
  if not isinstance(stem,str) or not stem:continue
  stem=Path(stem).stem.lower();stems=[stem]
  if len(stem)>1:stems += [stem[0]+x+stem[2:] for x in ('g','t')]
  for s in stems:
   for ext in (('.vxl',) if voxel else ('.shp','.tem')):result.append(s+ext)
 suffix='.vxl' if voxel else None
 return list(dict.fromkeys(n for n in result if n.endswith(suffix) if suffix)) if suffix else list(dict.fromkeys(n for n in result if n.endswith(('.shp','.tem'))))

def plan():
 data=metadata();indexes=readj(ROOT/'Assets/Reference/ClassicExpansion/Indexes/catalog.json');groups=[];assets={};missing=[]
 def lookup(choices,voxel=False):
  order=('local.mix','conquer.mix','generic.mix','temperat.mix','tem.mix','isotemp.mix','cache.mix',None) if voxel else ('generic.mix','conquer.mix','temperat.mix','tem.mix','isotemp.mix','cache.mix','local.mix',None)
  for name in choices:
   for nested in order:
    for index in indexes:
     if index['archive']!='ra2.mix' or index['nested']!=nested:continue
     found=index['index'].get(str(filename_hash(name)))
     if found:return {'name':name,'archive':index['archive'],'nested':nested,'offset':found[0],'bytes':found[1]}
 def add(key,owner,record,role,optional=False):
  voxel=bool(record.get('voxel'));choices=names_for(record,voxel);group={'key':key,'owner':owner,'role':role,'optional':optional,'voxel':voxel,'palette':record.get('palette','unittem'),'metadata':record,'candidates':choices};groups.append(group)
  found=lookup(choices,voxel)
  if not found:group['missing']=True;missing.append(group);return
  group['asset']=found['name'];assets.setdefault(found['name'],found)
  if voxel:
   hva=lookup([Path(found['name']).stem+'.hva'],True)
   if hva:group['hva']=hva['name'];assets.setdefault(hva['name'],hva)
 for name,record in data['objects'].items():
  add('object/'+name,name,record,'base')
  for i,layer in enumerate(record.get('layers',[])):
   if layer.get('role') not in ('bib','animation'):continue
   layer=dict(layer);layer.setdefault('palette',record.get('palette','unittem'))
   add('object/'+name+'/layer/'+str(i),name,layer,layer['role'],True)
 # Reuse only indexed member-size-matching retained original bytes, never another extractor's mutable state.
 inventory=readj(ROOT/'Assets/Reference/ClassicExpansion/reuse-inventory.json',{}).get('rawSources',[])
 if isinstance(inventory,dict):inventory=list(inventory.values())
 retained=readj(ROOT/'Assets/Reference/ClassicExpansion/sources.json',{}).get('assets',[])
 raw={}
 for item in inventory+retained:
  p=ROOT/item['path'];raw.setdefault(p.name.lower(),[]).append(p)
 for asset in assets.values():
  own=REF/'Original'/asset['name']
  if own.exists():raw.setdefault(asset['name'],[]).insert(0,own)
  for p in raw.get(asset['name'],[]):
   if p.exists() and p.stat().st_size==asset['bytes']:asset.update(path=rel(p),sha256=digest(p),reusedSource=not str(p).startswith(str(REF)));break
 result={'schema':1,'sourceRevision':REV,'metadataSha256':digest(META) if META.exists() else None,'provisionalBasesOnly':bool(data.get('provisionalBasesOnly')),'groups':groups,'assets':assets,'missing':missing}
 savej(REF/'plan.json',result);emit(phase='plan',objects=len(data['objects']),groups=len(groups),assets=len(assets),requiredMissing=[g['key'] for g in missing if not g['optional']],optionalMissing=[g['key'] for g in missing if g['optional']],fetchBytes=sum(a['bytes'] for a in assets.values() if 'path' not in a),reusedSources=sum('path' in a for a in assets.values()),provisional=result['provisionalBasesOnly']);return result

def fetch():
 data=readj(REF/'plan.json');state=readj(REF/'fetch-state.json',{'schema':1,'budget':BUDGET,'receivedBytes':0})
 def exact(archive,offset,length):
  chunks=[]
  while length:
   if archive=='ra2.mix':part,position=divmod(offset,70_000_000);filename=f'ra2.mix.part{part:02d}';count=min(length,70_000_000-position)
   else:filename,position,count=archive,offset,length
   path=REF/'RangeCache'/archive/f'{offset}-{count}.bin'
   if path.exists():raw=path.read_bytes();assert len(raw)==count
   else:
    with LOCK:
     assert state['receivedBytes']+count<=BUDGET,'MapObjects 32MB budget exceeded'
     state['receivedBytes']+=count;savej(REF/'fetch-state.json',state)
    url=BASE+filename+f'?range={position}-{count}'
    result=subprocess.run(['curl','--fail','--silent','--show-error','--proxy','http://127.0.0.1:10808','--max-time','25','--retry','4','--retry-all-errors','--retry-delay','0','--max-filesize',str(count),'--range',f'{position}-{position+count-1}','--write-out','%{http_code}',url],capture_output=True)
    if result.returncode or result.stdout[-3:]!=b'206' or len(result.stdout)!=count+3:raise RuntimeError(f'Exact206 failure {filename} {position}+{count}: {result.returncode} '+result.stderr.decode(errors='replace')[-300:])
    raw=result.stdout[:-3];original_write(path,raw)
   chunks.append(raw);offset+=count;length-=count
  return b''.join(chunks)
 def one(asset):
  if 'path' not in asset:
   p=REF/'Original'/asset['name'];original_write(p,exact(asset['archive'],asset['offset'],asset['bytes']));asset['path']=rel(p)
  asset['sha256']=digest(ROOT/asset['path']);return asset
 with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
  for i,asset in enumerate(pool.map(one,data['assets'].values())):
   if i%12==0:emit(phase='fetch',done=i+1,total=len(data['assets']),name=asset['name'])
 savej(REF/'plan.json',data);savej(REF/'sources.json',{'schema':1,'sourceRevision':REV,'assets':list(data['assets'].values()),'budget':state});return data

_NATIVE=None
def native():
 global _NATIVE
 if _NATIVE is None:_NATIVE=load('map_objects_independent_pixels',ROOT/'Tools/classic_art/verify_expansion_pixels.py')
 return _NATIVE
def frame_indices(raw,index):
 n=native();w,h,header,offsets=n._headers(raw,index);return header,n._crop_indices(raw,header,offsets)
def shadow_layout(path):
 raw=path.read_bytes();count=struct.unpack_from('<H',raw,6)[0];masks=[]
 for i in range(count):
  header,pixels=frame_indices(raw,i);masks.append({'index':i,'empty':not any(pixels),'onlyShadowIndices':set(pixels)<={0,1}})
 second=count%2==0 and all(x['onlyShadowIndices'] for x in masks[count//2:]) and any(not x['onlyShadowIndices'] for x in masks[:count//2])
 return ('second-half' if second else 'unspecified'),masks

def prepare():
 assert SHP.exists() and VXL.exists(),'Cached converters are required; never auto-build'
 assert META.exists(),'Parent metadata must be frozen before publication'
 data=readj(REF/'plan.json');assert data['metadataSha256']==digest(META),'Rerun --plan --fetch after parent metadata changes'
 assert not data['provisionalBasesOnly'];objects=metadata()['objects'];assets=data['assets'];sprite_keys={};voxel_keys={};audits={}
 catalog={'schema':1,'sourceRevision':REV,'metadataSha256':digest(META),'objects':{},'sprites':{},'voxels':{},'missing':data['missing'],'sourceManifest':rel(REF/'sources.json')}
 for group in data['groups']:
  if 'asset' not in group or group['voxel']:continue
  asset=assets[group['asset']];palette=PALETTES[group['palette']];identity=(asset['name'],group['palette'])
  if identity in sprite_keys:continue
  name=Path(asset['name']).stem
  if name in catalog['sprites']:name+='_'+group['palette']
  source=ROOT/asset['path'];layout,audit=shadow_layout(source);audits[asset['name']]={'layout':layout,'frames':audit};destination=OUT/'Sprites'/name;manifest=destination/'manifest.json';doc=readj(manifest)
  # Retain prior palette conversions as unused cache entries when correcting
  # a class-specific palette. Only the exact current conversion is published.
  if doc and doc['palette']['sha256']!=digest(palette):
   name+='_'+group['palette'];destination=OUT/'Sprites'/name;manifest=destination/'manifest.json';doc=readj(manifest)
  if doc:assert doc['sourceSha256']==asset['sha256'] and doc['palette']['sha256']==digest(palette) and doc['shadowLayout']==layout
  else:
   raw=bytearray(source.read_bytes());count=struct.unpack_from('<H',raw,6)[0];normalized=[]
   for i in range(count):
    pos=8+i*24;codec=raw[pos+8];offset=struct.unpack_from('<I',raw,pos+20)[0]
    if codec>3 and offset==0:raw[pos+8]=3;normalized.append((i,codec))
   decoder=source
   if normalized:decoder=REF/'DecoderInputs'/asset['name'];original_write(decoder,raw)
   result=subprocess.run(['dotnet',str(SHP),'convert','--shp',str(decoder),'--pal',str(palette),'--out',str(destination),'--layout','atlas','--columns',str(min(16,count)),'--shadow-layout',layout],capture_output=True,text=True)
   if result.returncode:raise RuntimeError(name+': '+result.stderr+' '+result.stdout[-2000:])
   doc=readj(manifest)
   if normalized:
    doc['sourceSha256']=asset['sha256']
    for i,codec in normalized:doc['frames'][i]['compression']=codec
    savej(manifest,doc);savej(REF/'DecoderInputs'/(asset['name']+'.json'),{'original':asset['path'],'originalSha256':asset['sha256'],'decoderInputSha256':digest(decoder),'normalization':'Unused codec only on zero-offset empty frames','frames':normalized})
  sprite_keys[identity]=name;catalog['sprites'][name]={'manifest':rel(manifest),'sourceSha256':asset['sha256'],'palette':group['palette'],'shadowLayout':layout,'colorFrames':len(doc['frames'])//2 if layout=='second-half' else len(doc['frames']),'emptySource':not any(f['hasData'] for f in doc['frames'])}
  emit(phase='sprite',name=name,done=len(sprite_keys),frames=len(doc['frames']),shadowLayout=layout)
 for group in data['groups']:
  if not group['voxel'] or 'asset' not in group:continue
  asset=assets[group['asset']]
  if asset['name'] in voxel_keys:continue
  name=Path(asset['name']).stem;manifest=OUT/'Voxels'/f'{name}.json';stage=REF/'VoxelParts';target=stage/asset['name'];original_write(target,(ROOT/asset['path']).read_bytes())
  if group.get('hva'):original_write(stage/group['hva'],(ROOT/assets[group['hva']]['path']).read_bytes())
  if not manifest.exists():
   result=subprocess.run(['dotnet',str(VXL),'bake','--palette',str(PALETTES['unittem']),'--output',str(manifest.parent),'--name',name,'--part','body='+str(target),'--directions','32','--columns','8','--scale','12','--elevation','30','--frame','0'],capture_output=True,text=True)
   if result.returncode:raise RuntimeError(name+': '+result.stderr+' '+result.stdout[-2000:])
  catalog['voxels'][name]={'manifest':rel(manifest)};voxel_keys[asset['name']]=name;emit(phase='voxel',name=name)
 groups={g['key']:g for g in data['groups']}
 for name,record in objects.items():
  base=groups['object/'+name];entry={'kind':'voxel' if base['voxel'] else 'sprite','layers':[],'missing':bool(base.get('missing'))}
  if base.get('asset'):
   if base['voxel']:entry['voxelName']=voxel_keys[base['asset']]
   else:
    key=sprite_keys[(base['asset'],base['palette'])];entry.update(spriteName=key,emptyBase=catalog['sprites'][key]['emptySource'])
  for i,layer in enumerate(record.get('layers',[])):
   group=groups.get('object/'+name+'/layer/'+str(i))
   if not group:continue
   item=dict(layer);item['missing']=bool(group.get('missing'))
   if group.get('asset'):item['spriteName']=sprite_keys[(group['asset'],group['palette'])]
   entry['layers'].append(item)
  catalog['objects'][name]=entry
 catalog['complete']=not any(not g['optional'] for g in data['missing']);catalog['counts']={'objects':len(catalog['objects']),'sprites':len(catalog['sprites']),'voxels':len(catalog['voxels']),'layers':sum(len(e['layers']) for e in catalog['objects'].values())}
 savej(REF/'shadow-audit.json',audits);savej(OUT/'catalog.json',catalog);emit(phase='catalog',complete=catalog['complete'],counts=catalog['counts']);return catalog

def validate():
 c=readj(OUT/'catalog.json');p=readj(REF/'plan.json');assets=p['assets'];errors=[];checks={'sources':0,'spriteFrames':0,'nativeColorComparisons':0,'voxelFacings':0};png=native().png;comparisons=[];largest=0
 for a in assets.values():
  source=ROOT/a['path'];assert source.stat().st_size==a['bytes'] and digest(source)==a['sha256'];checks['sources']+=1
 for name,item in c['sprites'].items():
  mp=ROOT/item['manifest'];d=readj(mp);assert len(d['images'])==1 and all(f['imageSet']==0 for f in d['frames']);decoded=[]
  for image in d['images']:
   pair={}
   for key in ('color','indices','teamMask'):
    w,h,pixels=png(mp.parent/image[key],True);assert (w,h)==(image['width'],image['height']) and w*h<=16_777_216;largest=max(largest,w*h);pair[key]=(w,h,pixels)
   decoded.append(pair)
  source=next(a for a in assets.values() if a['sha256']==d['sourceSha256']);raw=(ROOT/source['path']).read_bytes()
  for f in d['frames']:
   header,native_indices=frame_indices(raw,f['index']);rect=f['imageRect'];x,y,w,h=[rect[k] for k in ('x','y','width','height')];aw,ah,atlas=decoded[f['imageSet']]['indices'];assert 0<=x<=x+w<=aw and 0<=y<=y+h<=ah
   crop=b''.join(atlas[(y+row)*aw+x:(y+row)*aw+x+w] for row in range(h));assert hashlib.sha256(crop).hexdigest()==f['indicesSha256'];assert crop==native_indices
   is_shadow=f.get('role')=='shadow'
   if is_shadow:assert set(crop)<={0,1}
   mw,mh,mask=decoded[f['imageSet']]['teamMask'];actual_mask=b''.join(mask[(y+row)*mw+x:(y+row)*mw+x+w] for row in range(h));expected_mask=bytes(i-15 if 16<=i<=31 and not is_shadow else 0 for i in crop);assert actual_mask==expected_mask
   checks['spriteFrames']+=1
  color_count=item['colorFrames'];chosen=sorted({0,max(0,color_count//2),max(0,color_count-1)})
  for index in chosen:
   result=native().verify_manifest_frame(mp,ROOT/source['path'],PALETTES[item['palette']],index);comparisons.append(result);assert result['passed'],(name,index);checks['nativeColorComparisons']+=1
 for name,item in c['voxels'].items():
  mp=ROOT/item['manifest'];d=readj(mp);a=d['atlas'];assert d['directions']==32 and a['columns']==8 and a['width']*a['height']<=16_777_216
  assert d['palette']['sha256']==digest(PALETTES['unittem'])
  base=next(g for g in p['groups'] if g.get('voxel') and g.get('asset')==name+'.vxl');assert len(d['parts'])==1 and d['parts'][0]['sourceSha256']==assets[base['asset']]['sha256']
  if base.get('hva'):assert d['parts'][0]['hvaSha256']==assets[base['hva']]['sha256']
  for part in d['parts']:
   alpha=(mp.parent/part['alphaRaw']).read_bytes();indices=(mp.parent/part['indicesRaw']).read_bytes();team=(mp.parent/part['teamRaw']).read_bytes();assert len(alpha)==len(indices)==len(team)==a['width']*a['height'];assert all(t==(i-15 if 16<=i<=31 and cov else 0) for i,t,cov in zip(indices,team,alpha))
   for direction in range(32):
    x=direction%8*a['cellWidth'];y=direction//8*a['cellHeight'];assert any(any(alpha[(y+row)*a['width']+x:(y+row)*a['width']+x+a['cellWidth']]) for row in range(a['cellHeight']));checks['voxelFacings']+=1
 for name,entry in c['objects'].items():assert not entry['missing'] and (entry.get('spriteName') in c['sprites'] or entry.get('voxelName') in c['voxels']),name
 assert len(c['objects'])==91 and c['complete'];report={'schema':1,'pass':True,'checked':checks,'largestSpriteAtlasPixels':largest,'missingOptional':[g['key'] for g in c['missing'] if g['optional']],'noBuildOrNativeLaunch':True,'budget':readj(REF/'fetch-state.json')};savej(OUT/'Catalog/validation.json',report);savej(REF/'native-pixel-comparisons.json',{'pass':True,'comparisons':comparisons});emit(phase='validation',**report)

def preview():
 c=readj(OUT/'catalog.json');p=readj(REF/'plan.json');writer=load('map_object_png_writer',ROOT/'Tools/classic_art/prepare_cursors.py').png_write
 names=('TREE10','TREE21','CAMIAM04','CABHUT','CAOILD','CRATER07','TIBTRE01')
 for name in names:
  e=c['objects'].get(name)
  if not e or not e.get('spriteName'):continue
  item=c['sprites'][e['spriteName']];d=readj(ROOT/item['manifest']);source=next(a for a in p['assets'].values() if a['sha256']==d['sourceSha256']);w,h,rgba=native().native_shp_frame(ROOT/source['path'],PALETTES[item['palette']],0);writer(OUT/'Catalog/Previews'/f'{name.lower()}.png',w,h,rgba)
 emit(phase='preview',files=[n.lower()+'.png' for n in names])

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--plan',action='store_true');parser.add_argument('--fetch',action='store_true');parser.add_argument('--prepare',action='store_true');parser.add_argument('--validate',action='store_true');parser.add_argument('--preview',action='store_true');a=parser.parse_args()
 if a.plan:plan()
 if a.fetch:fetch()
 if a.prepare:prepare()
 if a.validate:validate()
 if a.preview:preview()
if __name__=='__main__':main()
