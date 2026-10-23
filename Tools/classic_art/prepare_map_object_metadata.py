#!/usr/bin/env python3
"""Metadata for only the original scenery names present in five official maps."""
import collections,configparser,hashlib,json,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Generated/ClassicExpansion/MapObjects/metadata.json'
def ini(path):
 p=configparser.ConfigParser(strict=False,interpolation=None,allow_no_value=True,inline_comment_prefixes=(';',));p.optionxform=str;p.read(path,encoding='utf-8');return {s:dict(p[s]) for s in p.sections()}
def get(section,key,default=None):return next((v for k,v in section.items() if k.lower()==key.lower()),default)
def number(v,default=0):
 try:return int(v)
 except(TypeError,ValueError):return default
def boolean(v,default=False):return str(v).lower() in('yes','true','1') if v is not None else default
def filenames(stem,theater=False,new_theater=False,voxel=False):
 stems=[stem.lower()]
 if new_theater or (len(stem)>2 and stem[0].upper() in('C','G','N') and stem[1].upper()=='A'):
  stems.extend(stem[0].lower()+c+stem[2:].lower() for c in('g','t'))
 stems=list(dict.fromkeys(stems));return [s+ext for s in stems for ext in(('.vxl',) if voxel else ('.shp','.tem'))]
rules_path=ROOT/'Assets/Reference/Visual/Metadata/rules.ini';art_path=ROOT/'Assets/Reference/Details/art.ini';rules=ini(rules_path);art=ini(art_path)
counts=collections.defaultdict(collections.Counter)
for m in json.loads((ROOT/'Assets/Generated/Maps/catalog.json').read_text())['maps']:
 for o in json.loads((ROOT/m['dataPath'].removeprefix('res://')).read_text())['objects']:
  values=o['value'].split(',');name=values[1] if o['section'] in('structures','units','infantry','aircraft') else values[0];counts[o['section']][name]+=1
result={'schema':1,'sourceRulesSha256':hashlib.sha256(rules_path.read_bytes()).hexdigest(),'sourceArtSha256':hashlib.sha256(art_path.read_bytes()).hexdigest(),'coordinateConvention':'Map source coordinates stay unchanged in ClassicMapObject.Value/Key. Runtime translation is SourceOrigin only. Structure drawing anchor is foundation center; terrain/unit/smudge drawing anchor is source first-cell center. Native smudge canvas already contains its full Width/Height footprint; do not tile or scale it.','passabilityConvention':'Explicit original IsPassable/Buildable overrides preserved; static defaults: smudge passable/buildable, other occupied scenery blocks. Occupation masks and all original flags retained, not guessed into extra occupied cells.','objects':{},'counts':{}}
for section,entries in counts.items():
 kind={'terrain':'terrain','structures':'structure','units':'unit','smudge':'smudge'}[section]
 for name,count in sorted(entries.items()):
  rule=rules.get(name,{});image=get(rule,'Image',name);a=art.get(image,{});source=get(a,'Image',image);theater=boolean(get(a,'Theater'));newtheater=boolean(get(a,'NewTheater'));voxel=boolean(get(a,'Voxel'),kind=='unit')
  foundation=get(a,'Foundation',get(rule,'Foundation',''));match=re.fullmatch(r'(\d+)x(\d+)',str(foundation),flags=re.I)
  w,h=(int(match[1]),int(match[2])) if match else ((max(1,number(get(rule,'Width'),1)),max(1,number(get(rule,'Height'),1))) if kind=='smudge' else (1,1))
  layers=[]
  bib=get(a,'BibShape')
  if bib:layers.append({'role':'bib','name':bib,'image':get(art.get(bib,{}),'Image',bib),'candidates':filenames(get(art.get(bib,{}),'Image',bib),theater,newtheater),'palette':'unittem'})
  # Only the actual building's original standing/active layers; no game-wide expansion.
  for prop,ref in a.items():
   if not ref or not re.fullmatch(r'(?:IdleAnim|ActiveAnim)(?:Two|Three|Four)?(?:Damaged)?',prop,re.I):continue
   animation=art.get(ref,{});sourceimage=get(animation,'Image',ref)
   layers.append({'role':'animation','name':ref,'image':sourceimage,'referenceProperty':prop,'start':number(get(animation,'Start'),0),'end':number(get(animation,'End'),-1),'loopStart':number(get(animation,'LoopStart'),0),'loopEnd':number(get(animation,'LoopEnd'),0),'loopCount':number(get(animation,'LoopCount'),-1),'rate':number(get(animation,'Rate'),200),'relativeOffsets':{'x':number(get(a,prop+'X'),0),'y':number(get(a,prop+'Y'),0)},'candidates':filenames(sourceimage,boolean(get(animation,'Theater'),theater),boolean(get(animation,'NewTheater'),newtheater)),'palette':'unittem','originalArt':animation})
  result['objects'][name]={'name':name,'kind':kind,'image':image,'sourceImage':source,'foundationW':max(1,w),'foundationH':max(1,h),'passable':boolean(get(rule,'IsPassable'),kind=='smudge'),'landPassable':boolean(get(rule,'IsPassable'),kind=='smudge'),'waterPassable':boolean(get(rule,'IsPassable'),kind=='smudge'),'buildable':boolean(get(rule,'Buildable'),kind=='smudge'),'voxel':voxel,'theater':theater,'newTheater':newtheater,'palette':'isotem' if kind=='smudge' or kind=='terrain' and not boolean(get(rule,'SpawnsTiberium')) else 'unittem','candidates':filenames(source,theater,newtheater,voxel),'anchor':'foundation-center' if kind=='structure' else 'cell-center','occupationBits':number(get(rule,'TemperateOccupationBits'),0),'instanceCount':count,'layers':layers,'originalRules':rule,'originalArt':a}
 result['counts'][kind]={'types':len(entries),'instances':sum(entries.values())}
assert len(result['objects'])==91
OUT.parent.mkdir(parents=True,exist_ok=True);OUT.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'schema':1,'types':len(result['objects']),'counts':result['counts'],'layers':sum(len(x['layers']) for x in result['objects'].values()),'missingRules':[x for x,v in result['objects'].items() if not v['originalRules']],'missingArt':[x for x,v in result['objects'].items() if not v['originalArt']]}))
