#!/usr/bin/env python3
"""Bounded original-data-only skirmish artwork extraction and conversion.
No game implementation is fetched; no build or game launch is performed.
"""
import argparse, concurrent.futures, hashlib, importlib.util, json, os
from pathlib import Path
import struct, subprocess, threading, zlib, sys
sys.dont_write_bytecode=True

ROOT=Path(__file__).resolve().parents[2]
REF=ROOT/'Assets/Reference/ClassicExpansion'
OUT=ROOT/'Assets/Generated/ClassicExpansion'
ROSTER=ROOT/'Tools/classic_art/roster.json'
CACHE=Path(os.environ['XDG_CACHE_HOME'])
SHP_DLL=CACHE/'redalert-godot-csharp/asset-pipeline/bin/AssetPipeline/debug/Ra2AssetPipeline.dll'
VXL_DLL=CACHE/'redalert-voxel-baker/bin/Debug/net10.0/VoxelBaker.dll'
BUDGET=64_000_000
LOCK=threading.Lock()

def load(name,path):
 s=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m

def read_json(path,default=None):return json.loads(path.read_text()) if path.exists() else default

def save_json(path,value):
 path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(value,indent=2)+'\n')

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def rel(path):return path.relative_to(ROOT).as_posix()

def emit(**value):print(json.dumps(value),flush=True)

def original_reader():
 reader=load('expansion_original_mix',ROOT/'Assets/Reference/Tools/fetch_ra2_samples.py')
 state=read_json(REF/'fetch-state.json',{'schema':1,'budget':BUDGET,'receivedBytes':0})
 def exact(archive,offset,length):
  chunks=[]
  while length:
   if archive=='ra2.mix':
    part,position=divmod(offset,70_000_000);filename=f'ra2.mix.part{part:02d}';count=min(length,70_000_000-position)
   else:filename,position,count=archive,offset,length
   cache=REF/'RangeCache'/archive/f'{offset}-{count}.bin'
   if cache.exists():data=cache.read_bytes();assert len(data)==count
   else:
    with LOCK:
     if state['receivedBytes']+count>BUDGET:raise RuntimeError('64 MB exact-range artifact budget exceeded')
     state['receivedBytes']+=count;save_json(REF/'fetch-state.json',state)
    url=reader.BASE+filename+f'?range={position}-{count}'
    command=['curl','--fail','--silent','--show-error','--proxy','http://127.0.0.1:10808','--max-time','25','--retry','4','--retry-all-errors','--retry-delay','0','--max-filesize',str(count),'--range',f'{position}-{position+count-1}','--write-out','%{http_code}',url]
    result=subprocess.run(command,capture_output=True)
    if result.returncode or result.stdout[-3:]!=b'206' or len(result.stdout)!=count+3:
     raise RuntimeError(f'Exact range failure {filename} {position}+{count}: {result.returncode} {result.stderr.decode(errors="replace")[-300:]}')
    data=result.stdout[:-3];cache.parent.mkdir(parents=True,exist_ok=True);cache.write_bytes(data)
   chunks.append(data);offset+=count;length-=count
  return b''.join(chunks)
 reader.fetch=exact;return reader

def indices(reader):
 path=REF/'Indexes/catalog.json'
 existing=read_json(path)
 if existing and any(x['nested']=='isotemp.mix' for x in existing) and any(x['nested']=='temperat.mix' for x in existing):return existing
 result=[]
 for archive,nested in [('ra2.mix',('local.mix','conquer.mix','generic.mix','tem.mix','isotemp.mix','temperat.mix','cache.mix')),('language.mix',('cameo.mix','cameo1.mix','cameo2.mix','sidec01.mix','sidec02.mix'))]:
  root=reader.mix_index(archive)
  result.append({'archive':archive,'nested':None,'index':{str(k):list(v) for k,v in root.items()}})
  for name in nested:
   location=root.get(reader.filename_hash(name))
   if not location:continue
   index=reader.mix_index(archive,location[0]);result.append({'archive':archive,'nested':name,'index':{str(k):list(v) for k,v in index.items()}})
   emit(index=name,archive=archive,members=len(index))
 save_json(path,result);return result

def candidates(record):
 return record.get('filenameCandidates',{}).get('filenames',[])

def all_groups(roster):
 groups=[]
 for unit in roster['units']+roster.get('auxiliary',[]):
  groups.append({'key':'unit/'+unit['rulesId'],'role':'base','owner':unit['rulesId'],'voxel':unit['voxel'],'candidates':candidates(unit)})
  if unit.get('noSpawnAlt') and unit.get('voxel'):
   stem=unit['sourceFileId'].lower()+'wo';groups.append({'key':'unit/'+unit['rulesId']+'/noSpawn','role':'noSpawn','owner':unit['rulesId'],'voxel':True,'optional':True,'candidates':[stem+'.vxl',stem+'.hva']})
  for i,part in enumerate(unit.get('turretAndWeapons',{}).get('optionalParts',[])):
   groups.append({'key':f'unit/{unit["rulesId"]}/part/{i}','role':part['role'],'owner':unit['rulesId'],'part':part,'voxel':True,'optional':True,'candidates':candidates(part)})
  for key in ('cameo','altCameo'):
   if unit.get(key):groups.append({'key':key+'/'+unit[key].lower(),'role':'cameo','owner':unit['rulesId'],'optional':key=='altCameo','candidates':[unit[key].lower()+'.shp']})
 for building in roster['buildings']:
  base='building/'+building['rulesId']
  groups.append({'key':base,'role':'base','owner':building['rulesId'],'candidates':candidates(building)})
  for key in ('cameo','altCameo'):
   if building.get(key):groups.append({'key':key+'/'+building[key].lower(),'role':'cameo','owner':building['rulesId'],'optional':key=='altCameo','candidates':[building[key].lower()+'.shp']})
  for prop in ('construction','bib'):
   item=building.get(prop)
   if isinstance(item,dict):groups.append({'key':base+'/'+prop,'role':prop,'owner':building['rulesId'],'optional':True,'metadata':item,'candidates':candidates(item)})
  for i,item in enumerate(building.get('animations',[])):
   groups.append({'key':base+f'/animation/{i}','role':'animation','owner':building['rulesId'],'metadata':item,'optional':True,'candidates':candidates(item)})
  for i,part in enumerate(building.get('turretAndWeapons',{}).get('optionalParts',[])):
   groups.append({'key':base+f'/part/{i}','role':part['role'],'owner':building['rulesId'],'part':part,'voxel':True,'optional':True,'candidates':candidates(part)})
 return groups

def plan():
 roster=read_json(ROSTER);reader=original_reader();indexes=indices(reader)
 reuse=read_json(REF/'reuse-inventory.json',{})
 raw={}
 source_items=reuse.get('rawSources',[])
 if isinstance(source_items,dict):source_items=list(source_items.values())
 for item in source_items:
  path=ROOT/item['path']
  raw.setdefault(path.name.lower(),[]).append(item)
 for path in (REF/'Original').glob('*'):
  if path.is_file():raw.setdefault(path.name.lower(),[]).append({'path':rel(path),'sha256':digest(path)})
 groups=all_groups(roster);assets={};missing=[]
 for group in groups:
  is_voxel=group.get('voxel',False)
  choices=[x for x in group['candidates'] if x.endswith('.vxl' if is_voxel else '.shp')]
  if not is_voxel and group['role']!='cameo':choices += [str(Path(x).with_suffix('.tem')) for x in choices]
  # Preserve original source ID; generic fallback is resolved from real MIX membership.
  found=None
  preferred=('local.mix',) if is_voxel else (('cameo.mix','cameo1.mix','cameo2.mix',None) if group['role']=='cameo' else ('generic.mix','conquer.mix','tem.mix','isotemp.mix','temperat.mix','cache.mix',None))
  for name in choices:
   for nested in preferred:
    for index in indexes:
     if index['nested']!=nested:continue
     if group['role']=='cameo' and index['archive']!='language.mix':continue
     entry=index['index'].get(str(reader.filename_hash(name)))
     if entry:
      found={'name':name,'archive':index['archive'],'nested':nested,'offset':entry[0],'bytes':entry[1]};break
    if found:break
   if found:break
  if not found:
   group['missing']=True;missing.append(group);continue
  group['asset']=found['name'];assets.setdefault(found['name'],found)
  if is_voxel:
   hva=Path(found['name']).stem+'.hva'
   for index in indexes:
    if index['archive']!=found['archive'] or index['nested']!=found['nested']:continue
    entry=index['index'].get(str(reader.filename_hash(hva)))
    if entry:assets.setdefault(hva,{'name':hva,'archive':index['archive'],'nested':index['nested'],'offset':entry[0],'bytes':entry[1]});group['hva']=hva
  for name in (found['name'],group.get('hva')):
   if not name:continue
   asset=assets[name]
   matching=[x for x in raw.get(name,[]) if (ROOT/x['path']).stat().st_size==asset['bytes']]
   if matching:asset['path']=matching[0]['path'];asset['reusedSource']=True
 for name,path in [('unittem.pal',ROOT/'Assets/Reference/Formats/unittem.pal'),('cameo.pal',ROOT/'Assets/Reference/Formats/Cameos/cameo.pal')]:
  if path.exists():assets[name]={'name':name,'path':rel(path),'bytes':path.stat().st_size,'reusedSource':True}
  else:
   for index in indexes:
    entry=index['index'].get(str(reader.filename_hash(name)))
    if entry:assets[name]={'name':name,'archive':index['archive'],'nested':index['nested'],'offset':entry[0],'bytes':entry[1]};break
 result={'schema':1,'sourceRevision':reader.REV,'groups':groups,'assets':assets,'missing':missing}
 save_json(REF/'plan.json',result)
 emit(phase='plan',groups=len(groups),assets=len(assets),missing=len(missing),requiredMissing=[g['key'] for g in missing if not g.get('optional')],fetchBytes=sum(a['bytes'] for a in assets.values() if not a.get('path')),reuseSources=sum(bool(a.get('path')) for a in assets.values()))
 return result

def fetch_assets():
 data=read_json(REF/'plan.json');reader=original_reader()
 def one(asset):
  if 'path' in asset:
   path=ROOT/asset['path']
  else:
   path=REF/'Original'/asset['name'];value=reader.fetch(asset['archive'],asset['offset'],asset['bytes'])
   if path.exists():assert path.read_bytes()==value
   else:path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(value)
   asset['path']=rel(path)
  asset['sha256']=digest(path)
  return asset
 assets=list(data['assets'].values())
 with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
  for i,asset in enumerate(pool.map(one,assets)):
   if i%15==0:emit(phase='fetch',done=i+1,total=len(assets),name=asset['name'])
 save_json(REF/'plan.json',data);save_json(REF/'sources.json',{'schema':1,'sourceRevision':reader.REV,'assets':assets,'budget':read_json(REF/'fetch-state.json')})
 return data

def reuse_manifests():
 inventory=read_json(REF/'reuse-inventory.json',{})
 sprites=inventory.get('sprites',[]);voxels=inventory.get('voxels',[])
 if isinstance(sprites,dict):sprites=list(sprites.values())
 if isinstance(voxels,dict):voxels=list(voxels.values())
 return sprites,voxels

def convert_sprite(asset,palette,shadow,existing):
 for item in existing:
  if item.get('sourceSha256')!=asset['sha256']:continue
  path=ROOT/item['manifest'];doc=read_json(path)
  if doc and doc.get('sourceSha256')==asset['sha256'] and doc.get('palette',{}).get('sha256')==digest(palette) and (doc.get('shadowLayout')==shadow or shadow=='unspecified'):
   return asset['name'][:-4],rel(path),True
 name=Path(asset['name']).stem;destination=OUT/'Sprites'/name;path=destination/'manifest.json';doc=read_json(path)
 if doc:assert doc['sourceSha256']==asset['sha256'] and doc['palette']['sha256']==digest(palette)
 else:
  original=ROOT/asset['path'];raw=bytearray(original.read_bytes());count=struct.unpack_from('<H',raw,6)[0];normalized=[]
  for i in range(count):
   pos=8+i*24;codec=raw[pos+8];offset=struct.unpack_from('<I',raw,pos+20)[0]
   if codec>3 and offset==0:normalized.append((i,codec));raw[pos+8]=3
  decoder=original
  if normalized:
   decoder=REF/'DecoderInputs'/asset['name'];decoder.parent.mkdir(parents=True,exist_ok=True)
   if decoder.exists():assert decoder.read_bytes()==raw
   else:decoder.write_bytes(raw)
  result=subprocess.run(['dotnet',str(SHP_DLL),'convert','--shp',str(decoder),'--pal',str(palette),'--out',str(destination),'--layout','atlas','--columns',str(min(16,count)),'--shadow-layout',shadow],capture_output=True,text=True)
  if result.returncode:raise RuntimeError(asset['name']+': '+result.stderr+' '+result.stdout[-2000:])
  if normalized:
   doc=read_json(path);doc['sourceSha256']=asset['sha256']
   save_json(REF/'DecoderInputs'/(asset['name']+'.json'),{'original':asset['path'],'originalSha256':asset['sha256'],'decoderInput':rel(decoder),'decoderInputSha256':digest(decoder),'normalization':'Unused codec on zero-offset frames only; original pixels and offsets unchanged','frames':[i for i,c in normalized]})
   for i,codec in normalized:doc['frames'][i]['compression']=codec
   save_json(path,doc)
 return name,rel(path),False

def voxel_signature(doc):
 return sorted((p['name'],p['sourceSha256'],p.get('hvaSha256')) for p in doc['parts'])

def prepare():
 assert SHP_DLL.exists() and VXL_DLL.exists(),'Cached converter DLL missing; do not build'
 data=read_json(REF/'plan.json');roster=read_json(ROSTER);assets=data['assets'];groups={g['key']:g for g in data['groups']}
 old_sprites,old_voxels=reuse_manifests();palette=ROOT/assets['unittem.pal']['path'];cameo_palette=ROOT/assets['cameo.pal']['path']
 catalog={'schema':1,'sourceRevision':data['sourceRevision'],'units':{},'buildings':{},'sprites':{},'voxels':{},'cameos':{},'missing':data['missing'],'sourceManifest':'Assets/Reference/ClassicExpansion/sources.json','sequenceSections':roster['sequenceSections']}
 sprite_refs={}
 cameo_reuse=read_json(REF/'reuse-inventory.json',{}).get('existingCameos',[])
 used_assets={g['asset']:g for g in data['groups'] if g.get('asset') and not g.get('voxel')}
 # Highest-priority shipyard and infantry sprites first; cameos are inexpensive.
 ordered=sorted(used_assets.items(),key=lambda kv:(0 if 'yard' in kv[0] or kv[1]['role']=='cameo' else 1,kv[0]))
 for i,(filename,group) in enumerate(ordered):
  is_cameo=group['role']=='cameo';asset=assets[filename]
  if is_cameo:
   name=Path(filename).stem
   # Legacy indexed cameo PNGs lack tRNS. Reconstruct the exact original color
   # frame directly, retaining index-zero transparency, without duplicate atlases.
   native=load('expansion_native_pixels',ROOT/'Tools/classic_art/verify_expansion_pixels.py')
   width,height,rgba=native.native_shp_frame(ROOT/asset['path'],cameo_palette,0)
   writer=load('expansion_png_writer',ROOT/'Tools/classic_art/prepare_cursors.py').png_write
   icon=OUT/'Cameos'/f'{name}.png';icon.parent.mkdir(parents=True,exist_ok=True);writer(icon,width,height,rgba)
   catalog['cameos'][name]={'png':rel(icon),'width':width,'height':height,'sourceSha256':asset['sha256'],'sourceFrame':0,'paletteSha256':digest(cameo_palette)}
   continue
  name,path,reused=convert_sprite(asset,palette,'second-half',old_sprites)
  catalog['sprites'][name]={'manifest':path,'sourceSha256':asset['sha256'],'reused':reused,'emptySource':not any(f['hasData'] for f in read_json(ROOT/path)['frames'])};sprite_refs[filename]=name
  if i%15==0:emit(phase='sprite',done=i+1,total=len(ordered),name=name,reused=reused)
 def bake(record,base_key,building=False,turret_index=0,empty_spawn=False):
  base=groups.get(base_key+('/noSpawn' if empty_spawn else ''));parts={}
  if not building and base and base.get('asset'):parts['body']=assets[base['asset']]
  extras=sorted((g for k,g in groups.items() if k.startswith(base_key+'/part/') and g.get('asset')),key=lambda g:g.get('part',{}).get('turretIndex',0))
  for g in ([] if empty_spawn else extras):
   if g.get('part',{}).get('turretIndex',0)==turret_index:parts.setdefault(g['role'],assets[g['asset']])
  if not parts:return None
  key=record['artId'].lower()+('_empty' if empty_spawn else '_turret'+str(turret_index) if turret_index else '');signature=[]
  for role,asset in parts.items():
   hva=assets.get(Path(asset['name']).stem+'.hva');signature.append((role,asset['sha256'],hva.get('sha256') if hva else None))
  for item in old_voxels:
   path=ROOT/item['manifest'];doc=read_json(path)
   if doc and doc.get('palette',{}).get('sha256')==digest(palette) and voxel_signature(doc)==sorted(signature):
    catalog['voxels'][key]={'manifest':rel(path),'reused':True};return key
  path=OUT/'Voxels'/f'{key}.json'
  if not path.exists():
   # HVA resolution is sibling-based. New referenced VXLs/HVAs may reside in
   # different original directories, so stage only missing siblings in our scope.
   staged=REF/'VoxelParts';staged.mkdir(parents=True,exist_ok=True)
   command=['dotnet',str(VXL_DLL),'bake','--palette',str(palette),'--output',str(path.parent),'--name',key]
   for role,asset in parts.items():
    target=staged/asset['name'];value=(ROOT/asset['path']).read_bytes()
    if not target.exists():target.write_bytes(value)
    else:assert target.read_bytes()==value
    hva=assets.get(Path(asset['name']).stem+'.hva')
    if hva:
     sibling=staged/hva['name'];value=(ROOT/hva['path']).read_bytes()
     if not sibling.exists():sibling.write_bytes(value)
     else:assert sibling.read_bytes()==value
    command+=['--part',role+'='+str(target)]
   command+=['--directions','32','--columns','8','--scale','12','--elevation','30','--frame','0']
   result=subprocess.run(command,capture_output=True,text=True)
   if result.returncode:raise RuntimeError(key+': '+result.stderr+' '+result.stdout[-2000:])
  doc=read_json(path);assert voxel_signature(doc)==sorted(signature)
  catalog['voxels'][key]={'manifest':rel(path),'reused':False};emit(phase='voxel',name=key,parts=list(parts));return key
 def identity(record,base_key):
  group=groups[base_key];result={'artId':record['artId'],'cameo':(record.get('cameo') or '').lower(),'sequence':record.get('sequences',{}),'sequenceSection':record.get('sequenceSection'),'sourceFileId':record['sourceFileId'],'frameMetadata':record.get('spriteFrameMetadata'),'movementDomain':record.get('movementDomain'),'turretAndWeapons':record.get('turretAndWeapons',{}),'noSpawnAlt':record.get('noSpawnAlt',False)}
  if record.get('voxel'):
   name=bake(record,base_key);result.update(kind='voxel',voxelName=name)
   indices=sorted({g.get('part',{}).get('turretIndex',0) for k,g in groups.items() if k.startswith(base_key+'/part/') and g.get('asset')})
   if len(indices)>1:result['voxelVariants']=[{'turretIndex':i,'voxelName':bake(record,base_key,turret_index=i)} for i in indices if i>0]
   if groups.get(base_key+'/noSpawn',{}).get('asset'):result['noSpawnVoxelName']=bake(record,base_key,empty_spawn=True)
  else:
   result.update(kind='sprite',spriteName=sprite_refs.get(group.get('asset')))
   if result['spriteName']:result['emptyBase']=catalog['sprites'][result['spriteName']]['emptySource']
  if group.get('missing'):result['missing']=True
  return result
 # Naval/air/MCV source bakes precede remaining land vehicles.
 units=roster['units']+roster.get('auxiliary',[])
 for record in sorted(units,key=lambda r:(0 if r['movementDomain'] in ('water','air','amphibious') or 'MCV' in r['rulesId'] else 1,r['rulesId'])):
  catalog['units'][record['rulesId']]=identity(record,'unit/'+record['rulesId'])
 for record in roster['buildings']:
  key='building/'+record['rulesId'];entry=identity(record,key);entry['foundation']=record.get('foundation');entry['layers']=[]
  for gkey,group in groups.items():
   if not gkey.startswith(key+'/') or group.get('voxel'):continue
   metadata=dict(group.get('metadata',{}));metadata.update(role=group['role'],spriteName=sprite_refs.get(group.get('asset')),sourceFile=group.get('asset'),missing=bool(group.get('missing')))
   entry['layers'].append(metadata)
  if record.get('turretAndWeapons',{}).get('turretAnimIsVoxel'):entry['turretVoxelName']=bake(record,key,True)
  catalog['buildings'][record['rulesId']]=entry
 catalog['counts']={'unitVariants':len(roster['units']),'auxiliaries':len(roster.get('auxiliary',[])),'buildingVariants':len(roster['buildings']),'sprites':len(catalog['sprites']),'voxels':len(catalog['voxels']),'cameos':len(catalog['cameos'])}
 catalog['complete']=not any(g for g in data['missing'] if not g.get('optional'))
 save_json(OUT/'catalog.json',catalog);emit(phase='catalog',counts=catalog['counts'],complete=catalog['complete']);return catalog

def validate():
 catalog=read_json(OUT/'catalog.json');data=read_json(REF/'plan.json');errors=[];checked={'sourceFiles':0,'spriteFrames':0,'voxelFacings':0,'cameos':0}
 for asset in data['assets'].values():
  path=ROOT/asset['path'];assert path.stat().st_size==asset['bytes'] and digest(path)==asset['sha256'],asset['name'];checked['sourceFiles']+=1
 for name,item in catalog['sprites'].items():
  path=ROOT/item['manifest'];doc=read_json(path);assert doc['sourceSha256']==item['sourceSha256'],name
  assert doc['canvasWidth']>0 and doc['canvasHeight']>0
  if not any(f['hasData'] for f in doc['frames']):
   source=next(a for a in data['assets'].values() if a['sha256']==item['sourceSha256']);raw=(ROOT/source['path']).read_bytes();count=struct.unpack_from('<H',raw,6)[0]
   assert all(struct.unpack_from('<I',raw,28+24*i)[0]==0 for i in range(count)),name
  for image in doc['images']:
   for channel in ('color','indices','teamMask'):
    png=path.parent/image[channel]
    with png.open('rb') as stream:header=stream.read(24)
    assert header.startswith(b'\x89PNG\r\n\x1a\n') and struct.unpack_from('>II',header,16)==(image['width'],image['height']),(name,channel)
  for frame in doc['frames']:
   assert frame['index']>=0
   if frame['hasData']:assert len(frame['indicesSha256'])==64 and frame['sourceRect']['width']>0
   checked['spriteFrames']+=1
 for name,item in catalog['voxels'].items():
  path=ROOT/item['manifest'];doc=read_json(path);assert doc['schema']=='westwood-voxel-atlas/v1' and doc['directions']==32 and doc['atlas']['columns']==8 and doc['animationFrame']==0,name
  assert (path.parent/doc['composite']).exists()
  for part in doc['parts']:
   assert len(part['frames'])==32 and all(f['opaquePixels']>0 for f in part['frames']),name
   for key in ('png','alphaRaw','teamRaw','indicesRaw','normalsRaw'):assert (path.parent/part[key]).exists(),(name,key)
   original=data['assets'][part['source'].lower()];assert original['sha256']==part['sourceSha256']
   if part.get('hva'):assert data['assets'][part['hva'].lower()]['sha256']==part['hvaSha256'] and not part['identityPose']
   width,height=doc['atlas']['width'],doc['atlas']['height'];alpha=(path.parent/part['alphaRaw']).read_bytes();assert len(alpha)==width*height
   for frame in part['frames']:
    x,y,w,h=frame['rect'];assert x>=0 and y>=0 and x+w<=width and y+h<=height
    assert sum(alpha[(y+row)*width+x:(y+row)*width+x+w].count(255) for row in range(h))==frame['opaquePixels'],(name,part['name'],frame['direction'])
    assert frame['anchor']==doc['atlas']['anchor']
   checked['voxelFacings']+=32
 for name,item in catalog['cameos'].items():
  raw=(ROOT/item['png']).read_bytes();assert raw.startswith(b'\x89PNG\r\n\x1a\n')
  assert struct.unpack_from('>II',raw,16)==(item['width'],item['height']);checked['cameos']+=1
 for group in ('units','buildings'):
  for name,item in catalog[group].items():
   key=item.get('voxelName') if item['kind']=='voxel' else item.get('spriteName')
   if not key:errors.append(name+' missing base artwork')
   if item.get('cameo') and item['cameo'] not in catalog['cameos']:errors.append(name+' missing cameo '+item['cameo'])
 result={'schema':1,'pass':not errors,'checked':checked,'errors':errors,'missingOptional': [g['key'] for g in catalog['missing'] if g.get('optional')],'nativeGameLaunched':False,'builtToolsOrGame':False,'sourceRevision':catalog['sourceRevision'],'budget':read_json(REF/'fetch-state.json')}
 save_json(OUT/'Units/validation.json',result);emit(phase='validation',**result)
 if errors:raise RuntimeError(str(errors))

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--scan',action='store_true');parser.add_argument('--fetch',action='store_true');parser.add_argument('--prepare',action='store_true');parser.add_argument('--validate',action='store_true');args=parser.parse_args()
 if args.scan:plan()
 if args.fetch:fetch_assets()
 if args.prepare:prepare()
 if args.validate:validate()
if __name__=='__main__':main()
