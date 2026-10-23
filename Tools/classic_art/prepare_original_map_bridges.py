#!/usr/bin/env python3
"""Retain original bridge/crate SHPs and validate source-only map assemblies.

Original data/pixels only. No game implementation, build, or native launch.
"""
import argparse, collections, hashlib, importlib.util, json, struct, subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
REF=ROOT/'Assets/Reference/ClassicExpansion/MapOverlays'
OUT=ROOT/'Assets/Generated/ClassicExpansion/Terrain/Bridges'
LOW=ROOT/'Assets/Generated/Terrain/OriginalStructures/catalog.json'
REV='acfa5b834899e7fab4c8499ccb4b49c402927b44'
BASE=f'https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/{REV}/game-res/'
NAMES=('bridge','bridgb','crate','wcrate')
HIGH={'BRIDGE1':('bridge',0),'BRIDGE2':('bridge',9),'BRIDGEB1':('bridgb',0),'BRIDGEB2':('bridgb',9)}
# Native deck-plane geometry, not a cropped bounding-box recenter or a claim
# about original executable offsets. X concrete is also checked against real
# OVRPS roadheads in the two official maps that actually contain high bridges.
ANCHORS={'bridge':((90,45),(90,60)),'bridgb':((132,75),(120,90)),'crate':((30,45),),'wcrate':((30,45),)}

def load(name,path):
 s=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m

def fetch():
 reader=load('original_bridge_mix_names',ROOT/'Assets/Reference/Tools/fetch_ra2_samples.py')
 indexes=json.loads((ROOT/'Assets/Reference/ClassicExpansion/Indexes/catalog.json').read_text());records=[]
 for stem in NAMES:
  name=stem+'.tem';match=[i['index'].get(str(reader.filename_hash(name))) for i in indexes if i['archive']=='ra2.mix' and i['nested']=='temperat.mix']
  assert len(match)==1 and match[0]
  offset,size=match[0];part,position=divmod(offset,70_000_000);assert position+size<=70_000_000
  source_url=BASE+f'ra2.mix.part{part:02d}';destination=REF/name
  if destination.exists():raw=destination.read_bytes();assert len(raw)==size
  else:
   command=['curl','--fail','--silent','--show-error','--proxy','http://127.0.0.1:10808','--max-time','25','--retry','4','--retry-all-errors','--retry-delay','0','--max-filesize',str(size),'--range',f'{position}-{position+size-1}','--write-out','%{http_code}',source_url+f'?range={position}-{size}']
   result=subprocess.run(command,capture_output=True,check=True).stdout;assert result[-3:]==b'206' and len(result)==size+3
   raw=result[:-3];destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(raw)
  records.append({'originalFilename':name,'path':destination.relative_to(ROOT).as_posix(),'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest(),'archive':'ra2.mix','nested':'temperat.mix','archiveOffset':offset,'sourceUrl':source_url,'httpRange':f'bytes={position}-{position+size-1}'})
  print(json.dumps({'file':name,'header':struct.unpack_from('<4H',raw),'sha256':records[-1]['sha256']}),flush=True)
 REF.mkdir(parents=True,exist_ok=True);(REF/'sources.json').write_text(json.dumps({'schema':1,'sourceRevision':REV,'assets':records},indent=2)+'\n')

def decode(path,palette):
 raw=path.read_bytes();magic,cw,ch,count=struct.unpack_from('<4H',raw);assert magic==0 and cw>0 and ch>0 and count>0
 frames=[]
 for i in range(count):
  p=8+i*24;x,y,w,h=struct.unpack_from('<4H',raw,p);mode=raw[p+8];offset=struct.unpack_from('<I',raw,p+20)[0];assert x+w<=cw and y+h<=ch
  indices=bytearray(w*h)
  if offset and w and h:
   assert offset>=8+count*24
   if mode in(0,1):assert offset+w*h<=len(raw);indices[:]=raw[offset:offset+w*h]
   elif mode==3:
    cursor=offset
    for row in range(h):
     length=struct.unpack_from('<H',raw,cursor)[0];end=cursor+length;cursor+=2;col=0;assert length>=2 and end<=len(raw)
     while cursor<end:
      value=raw[cursor];cursor+=1
      if value:assert col<w;indices[row*w+col]=value;col+=1
      else:
       run=raw[cursor];cursor+=1;assert run>0 and(col+run<=w or cursor==end and col+run==w+1);col=min(w,col+run)
   else:raise RuntimeError(f'{path.name} frame{i} unsupported mode{mode}')
  rgba=bytearray(w*h*4)
  for j,value in enumerate(indices):
   if value:rgba[j*4:j*4+4]=bytes((palette[value*3]<<2,palette[value*3+1]<<2,palette[value*3+2]<<2,255))
  frames.append(({'index':i,'sourceRect':{'x':x,'y':y,'width':w,'height':h},'hasData':bool(offset and any(indices)),'compression':mode,'dataOffset':offset,'indicesSha256':hashlib.sha256(indices).hexdigest(),'nontransparentIndices':sorted(set(indices)-{0})},rgba))
 return cw,ch,frames

def low_references():
 result=[]
 for t in json.loads(LOW.read_text())['templates']:
  if not t['id'].startswith('lobrdg'):continue
  source=t['spriteManifest']['frames'];count=len(source)//2
  assert len(source)%2==0
  # The only non-middle colored lane is native LOBRDG24/frame2. Its whole
  # source canvas is drawn from cross-lane 2, not silently changed to lane 1.
  axis='Y' if t['id'] in [f'lobrdg{i:02d}' for i in range(10,19)]+[f'lobrdg{i:02d}' for i in range(23,27)]+['lobrdge2','lobrdge4'] else 'X'
  frames=[]
  for f in source[:count]:
   lane=f['index']-1 if count==3 else 0
   ax,ay=(90,50) if count==3 else (90,60)
   ax+=(-30 if axis=='X' else 30)*lane;ay+=15*lane
   frames.append({'index':f['index'],'hasData':f['hasData'],'deckCenterAnchor':{'x':ax,'y':ay}})
  result.append({'overlayName':t['id'].upper(),'template':t['id'],'sourcePath':t['sourcePath'],'sourceSha256':t['sourceSha256'],'axis':axis,'frames':frames,'nativeTileTopAnchor':t['pixelFootAnchorFromTileOrigin']})
 return result

def originals():
 helper=load('original_bridge_png_writer',ROOT/'Tools/classic_art/prepare_original_map_terrain.py');palette=(ROOT/'Assets/Reference/Visual/Terrain/isotem.pal').read_bytes();templates=[];layers=[]
 for name in NAMES:
  source=REF/(name+'.tem');cw,ch,frames=decode(source,palette);half=len(frames)//2;assert len(frames)%2==0
  entry={'id':name,'canvasWidth':cw,'canvasHeight':ch,'colorFrameCount':half,'sourcePath':source.relative_to(ROOT).as_posix(),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'frames':[]}
  if name in('crate','wcrate'):assert half==1
  else:assert half==18
  for frame,rgba in frames:
   index=frame['index'];color=index%half;axis=color//9 if half==18 else 0;ax,ay=ANCHORS[name][axis]
   shadow=index>=half
   if shadow and frame['hasData']:assert frame['nontransparentIndices']==[1],(name,index)
   frame.update({'role':'shadow-mask' if shadow else 'color','pairedFrame':index-half if shadow else index+half,'deckCenterAnchor':{'x':ax,'y':ay},'offsetFromDeckCenter':{'x':frame['sourceRect']['x']-ax,'y':frame['sourceRect']['y']-ay}})
   frame['atlasIndex']=len(layers);layers.append((frame,rgba));entry['frames'].append(frame)
   if frame['hasData']:
    image=bytearray(cw*ch*4);r=frame['sourceRect']
    for row in range(r['height']):image[((r['y']+row)*cw+r['x'])*4:((r['y']+row)*cw+r['x']+r['width'])*4]=rgba[row*r['width']*4:(row+1)*r['width']*4]
    helper.png_write(OUT/'Frames'/f'{name}-{index:03d}.png',cw,ch,image)
   if not shadow and half==18:
    r=frame['sourceRect'];mask=bytearray(len(rgba));near_x=ax+(-60 if axis==0 else 60)
    for y in range(r['height']):
     for x in range(r['width']):
      px,py=r['x']+x+.5,r['y']+y+.5
      # Only native sidewall/timber on or outside the deck's near edge.
      # We do not guess an above-deck rail semantic from SHP colors or repaint road.
      floor=ay+.5*(px-ax)+60 if axis==0 else ay-.5*(px-ax)+60
      if near_x-19<=px<=near_x+19 and py>=floor:
       q=(y*r['width']+x)*4;mask[q:q+4]=rgba[q:q+4]
    fg={'kind':'native-near-side-exterior','sourceRect':dict(r),'offsetFromDeckCenter':dict(frame['offsetFromDeckCenter']),'atlasIndex':len(layers),'hasData':any(mask[3::4])}
    frame['foregroundLayer']=fg;layers.append((fg,mask));helper.png_write(OUT/'Masks'/f'{name}-{index:03d}.png',r['width'],r['height'],mask)
  templates.append(entry)
 return helper,templates,layers

def prepare():
 helper,templates,layers=originals();width=2048;x=y=rh=0
 for layer,rgba in sorted(layers,key=lambda item:-item[0]['sourceRect']['height']):
  w,h=layer['sourceRect']['width'],layer['sourceRect']['height']
  if x+w+4>width:x=0;y+=rh;rh=0
  layer['region']={'x':x+2,'y':y+2,'width':w,'height':h};x+=w+4;rh=max(rh,h+4)
 height=y+rh;assert width*height<=16_777_216;atlas=bytearray(width*height*4)
 for layer,rgba in layers:
  r=layer['region'];w,h=r['width'],r['height']
  for row in range(h):atlas[((r['y']+row)*width+r['x'])*4:((r['y']+row)*width+r['x']+w)*4]=rgba[row*w*4:(row+1)*w*4]
 helper.png_write(OUT/'atlas.png',width,height,atlas)
 catalog={'schema':2,'atlas':'atlas.png','atlasWidth':width,'atlasHeight':height,'templates':templates,
  'highOverlaySelection':{name:{'template':t,'frameOffset':offset,'stateCount':9,'axis':'Y' if offset else 'X'} for name,(t,offset) in HIGH.items()},
  'lowCatalog':'Assets/Generated/Terrain/OriginalStructures/catalog.json','lowOverlays':low_references(),
  'registration':{'projection':'Caller supplies already projected SurfaceLevel deck center. Never add another four levels here. Base TMP separately uses Level.',
   'frameState':'High overlay state 0..8 plus 0/9 axis offset; paired shadow is colorIndex+18. BRIDGE1 0/7/8 are exercised by retained original maps. Second axis and wooden high bridges are source-geometric coverage, absent from these five maps.',
   'anchor':'Derived deck-plane registration from original native deck geometry, checked against unchanged original TMP roadheads in static scenes. Not a claim about original executable draw offsets or bbox recentering.',
   'concreteDeckAnchors':[[90,45],[90,60]],'woodDeckAnchors':[[132,75],[120,90]],
   'concreteDeckQuads':[[[135,7.5],[165,22.5],[45,82.5],[15,67.5]],[[45,22.5],[165,82.5],[135,97.5],[15,37.5]]],
   'low':'Selected native low bridge deck center is (90,50), equivalent to previous tile-top (90,35)+15. Native empty lane states stay empty. Unselected damaged siblings use same deck plane and native lane index.',
   'shadow':'Original index1 coverage is retained unchanged; renderer modulates this mask to black at nominal alpha0.32, never displays cyan as color.',
   'foreground':'High masks retain original pixels outside the deck near edge only. Above-deck rail semantics are not guessed; low bridges reuse previously verified source near-timber masks.',
   'crates':'One original color frame plus one index1 shadow, anchor(30,45). Tick intentionally does not animate this single-frame asset. No rewardKind information or guessed icon.'}}
 (OUT/'catalog.json').write_text(json.dumps(catalog,indent=2)+'\n');print(json.dumps({'templates':[(t['id'],len(t['frames'])) for t in templates],'layers':len(layers),'atlas':[width,height]}),flush=True)

def validate():
 helper=load('original_bridge_validation_png',ROOT/'Tools/classic_art/prepare_original_map_terrain.py');png=helper.png_reader();cat=json.loads((OUT/'catalog.json').read_text());w,h,pixels=png(OUT/'atlas.png',True);palette=(ROOT/'Assets/Reference/Visual/Terrain/isotem.pal').read_bytes();count=0
 for t in cat['templates']:
  assert hashlib.sha256((ROOT/t['sourcePath']).read_bytes()).hexdigest()==t['sourceSha256']
  _,_,decoded=decode(ROOT/t['sourcePath'],palette)
  for f,rgba in decoded:
   packed=t['frames'][f['index']];assert packed['index']==f['index'] and packed['indicesSha256']==f['indicesSha256']
   r=packed['region'];actual=bytearray()
   for row in range(r['height']):actual.extend(pixels[((r['y']+row)*w+r['x'])*4:((r['y']+row)*w+r['x']+r['width'])*4])
   assert actual==rgba,(t['id'],f['index']);count+=1
   fg=packed.get('foregroundLayer')
   if fg:
    r=fg['region'];mask=bytearray()
    for row in range(r['height']):mask.extend(pixels[((r['y']+row)*w+r['x'])*4:((r['y']+row)*w+r['x']+r['width'])*4])
    for p in range(0,len(mask),4):
     if mask[p+3]:assert mask[p:p+4]==rgba[p:p+4]
 low={t['overlayName']:t for t in cat['lowOverlays']};coverage=[];all_high=[]
 for info in json.loads((ROOT/'Assets/Generated/Maps/catalog.json').read_text())['maps']:
  data=json.loads((ROOT/info['dataPath'].removeprefix('res://')).read_text());normal=empty=high=0;states=collections.Counter()
  for c in data['runtimeCells']:
   name=c['overlayName'];state=c['overlayData']
   if name in HIGH:
    assert 0<=state<9;high+=1;normal+=1;states[name+':'+str(state)]+=1;all_high.append({'map':info['id'],'x':c['x'],'y':c['y'],'level':c['level'],'surfaceLevel':c['surfaceLevel'],'name':name,'data':state});assert c['surfaceLevel']==c['level']+4
   elif name.startswith('LOBRDG'):
    assert name in low;f=next(f for f in low[name]['frames'] if f['index']==state)
    normal+=f['hasData'];empty+=not f['hasData'];states[name+':'+str(state)]+=1
  coverage.append({'map':info['id'],'colorCells':normal,'nativeEmptyCells':empty,'highCells':high,'states':dict(states)})
 broken=[c for c in all_high if c['map']=='mp02t2' and c['y']==93];assert [c['x'] for c in broken]==[101,102,107,108]
 result={'pass':True,'nativeFramesExactRgbaAndIndices':count,'foregroundMasksSourcePixelSubsets':36,'shadowMasksAreNativeIndex1':True,'mapCoverage':coverage,'highSourceCells':all_high,'brokenRunPreserved':{'map':'mp02t2','y':93,'sourceOverlayXs':[101,102,107,108],'unsuppliedXs':[103,104,105,106],'fillMissingCells':False},'limitations':['Native game not run. Static source-only geometry/pixel proof.','BRIDGE2/BRIDGEB axes are native-image geometric registration; absent from these five official maps.','Above-deck high rail masks are not guessed. Existing verified low near-timber masks are reused.'],'buildOrNativeGameRun':False}
 (OUT/'validation.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({'pass':True,'nativeFrames':count,'coverage':[{k:v for k,v in c.items() if k!='states'} for c in coverage]}),flush=True)

def preview():
 helper=load('original_bridge_static_assembly',ROOT/'Tools/classic_art/prepare_original_map_terrain.py');tiles,layers=helper.original_sources();base={(t['tileIndex'],f['index']):f for t in tiles for f in t['frames']};base_rgba={l['atlasIndex']:rgba for l,rgba,_,_ in layers};png=helper.png_reader();cat=json.loads((OUT/'catalog.json').read_text());aw,ah,atlas=png(OUT/'atlas.png',True);templates={t['id']:t for t in cat['templates']}
 def blit(image,width,height,rgba,w,h,x,y,shadow=False):
  for row in range(max(0,-y),min(h,height-y)):
   for col in range(max(0,-x),min(w,width-x)):
    p=(row*w+col)*4
    if not rgba[p+3]:continue
    q=((y+row)*width+x+col)*4
    if shadow:
     for ch in range(3):image[q+ch]=image[q+ch]*68//100
    else:image[q:q+4]=rgba[p:p+4]
 def layer_pixels(layer):
  r=layer['region'];return b''.join(atlas[((r['y']+row)*aw+r['x'])*4:((r['y']+row)*aw+r['x']+r['width'])*4] for row in range(r['height']))
 for mapid,x,y,label in [('mp23t4',80,73,'intact'),('mp02t2',105,93,'broken'),('mp02t2',137,127,'short')]:
  data=json.loads((ROOT/f'Assets/Generated/Maps/{mapid}.json').read_text());cells=data['runtimeCells'];level=5 if mapid=='mp23t4' else 8;width,height=960,640;image=bytearray(width*height*4);cx=(x-y)*30;cy=(x+y)*15-level*15
  def point(c,deck=False):return ((c['x']-c['y'])*30-cx+width//2,(c['x']+c['y'])*15-cy+height//2-(c['surfaceLevel'] if deck else c['level'])*15)
  ordered=sorted(cells,key=lambda c:(c['x']+c['y'],c['x']))
  for c in ordered:
   px,py=point(c)
   if px < -300 or px>width+300 or py < -300 or py>height+300:continue
   f=base[(0 if c['tileIndex'] in(-1,65535) else c['tileIndex'],c['subTile'])]
   for l in f['layers']:blit(image,width,height,base_rgba[l['atlasIndex']],l['size']['width'],l['size']['height'],px+l['offsetFromCellCenter']['x'],py+l['offsetFromCellCenter']['y'])
  overlays=[c for c in ordered if c['overlayName'] in HIGH]
  for shadow in (True,False):
   for c in overlays:
    name,offset=HIGH[c['overlayName']];f=templates[name]['frames'][offset+c['overlayData']+(18 if shadow else 0)];px,py=point(c,True);o=f['offsetFromDeckCenter'];r=f['region']
    if px < -300 or px>width+300 or py < -300 or py>height+300:continue
    blit(image,width,height,layer_pixels(f),r['width'],r['height'],round(px+o['x']),round(py+o['y']),shadow)
  helper.png_write(OUT/'Previews'/f'{mapid}-{label}-source-only.png',width,height,image);print('SOURCE_ONLY_BRIDGE_ASSEMBLY',mapid,label,flush=True)

def main():
 p=argparse.ArgumentParser();p.add_argument('--fetch',action='store_true');p.add_argument('--prepare',action='store_true');p.add_argument('--validate',action='store_true');p.add_argument('--preview',action='store_true');a=p.parse_args()
 if a.fetch:fetch()
 if a.prepare:prepare()
 if a.validate:validate()
 if a.preview:preview()
if __name__=='__main__':main()
