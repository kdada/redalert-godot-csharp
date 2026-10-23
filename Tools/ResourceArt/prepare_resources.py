#!/usr/bin/env python3
"""Original GOLD/GEM pixels only. Independent <=1 MB extraction; cached DLLs only."""
import argparse, collections, hashlib, importlib.util, json, struct, subprocess, sys
from pathlib import Path
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Generated/Resources'
NAMES=tuple(f'{prefix}{i:02}' for prefix in ('gold','gem') for i in range(1,5))

def module():
    spec=importlib.util.spec_from_file_location('resource_original_data_pipeline',ROOT/'Tools/classic_art/prepare_map_object_art.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
    m.REF=OUT/'Sources';m.OUT=OUT;m.META=OUT/'producer-input.json';m.BUDGET=1_000_000
    m.metadata=lambda:m.readj(m.META)
    m.names_for=lambda record,voxel=False:record['candidates']
    # Audit every frame. An empty tail is not a visible shadow half.
    def layout(path):
        raw=path.read_bytes();count=struct.unpack_from('<H',raw,6)[0];audit=[]
        for i in range(count):
            _,pixels=m.frame_indices(raw,i);audit.append({'index':i,'empty':not any(pixels),'onlyShadowIndices':set(pixels)<={0,1},'opaquePixelCount':sum(bool(p) for p in pixels)})
        tail=audit[count//2:]
        shadow=count%2==0 and all(f['onlyShadowIndices'] for f in tail) and any(not f['empty'] for f in tail)
        return 'second-half' if shadow else 'unspecified',audit
    m.shadow_layout=layout
    return m

def plan(m):
    m.savej(m.META,{'schema':1,'scope':'Only four original GOLD and four original GEM temperate shapes','objects':{n.upper():{'name':n.upper(),'kind':'ore' if n.startswith('gold') else 'gem','image':n.upper(),'sourceImage':n.upper(),'palette':'isotem','voxel':False,'candidates':[n+'.tem',n+'.shp'],'layers':[]} for n in NAMES}})
    data=m.plan()
    for n in NAMES[:4]:
        source=ROOT/'Assets/Reference/Visual/Scenery'/(n+'.tem');asset=data['assets'][n+'.tem'];assert source.exists() and source.stat().st_size==asset['bytes'];asset.update(path=m.rel(source),sha256=m.digest(source),reusedSource=True)
    assert len(data['assets'])==8 and len(data['groups'])==8 and not data['missing']
    needed=sum(a['bytes'] for a in data['assets'].values() if 'path' not in a);assert needed<=m.BUDGET
    m.savej(m.REF/'plan.json',data);m.emit(phase='resource-plan',sources=8,reusedGold=4,fetchBytes=needed)

def fetch(m):
    m.fetch();ledger=m.readj(m.REF/'sources.json')
    for a in ledger['assets']:
        offset=a['offset'];remaining=a['bytes'];segments=[]
        while remaining:
            part,position=divmod(offset,70_000_000);count=min(remaining,70_000_000-position);segments.append({'url':m.BASE+f'ra2.mix.part{part:02}','httpRange':f'bytes={position}-{position+count-1}','bytes':count});offset+=count;remaining-=count
        a['sourceRanges']=segments
    m.savej(m.REF/'sources.json',ledger)

def overlay_metadata(m):
    rulepath=ROOT/'Assets/Reference/Visual/Metadata/rules.ini';artpath=ROOT/'Assets/Reference/Details/art.ini';rules=m.ini(rulepath);art=m.ini(artpath);counts=collections.Counter();kinds={};maps=[]
    for info in m.readj(ROOT/'Assets/Generated/Maps/catalog.json')['maps']:
        local=collections.Counter()
        for cell in m.readj(ROOT/info['dataPath'].removeprefix('res://'))['runtimeCells']:
            kind=cell.get('resourceKind',0)
            if not kind:continue
            name=cell['overlayName'];assert kind in (1,2) and kinds.get(name,kind)==kind;kinds[name]=kind;counts[name]+=1;local['ore' if kind==1 else 'gem']+=1
        maps.append({'mapId':info['id'],'resourceCells':dict(local)})
    result={}
    for name,count in sorted(counts.items()):
        rule=dict(rules[name]);image=rule.get('Image',name);a=dict(art[image]) if image in art else {};kind='ore' if kinds[name]==1 else 'gem';family=list(NAMES[:4] if kind=='ore' else NAMES[4:]);direct=image.lower() if image.lower() in family else None
        result[name]={'resourceKind':kinds[name],'kind':kind,'rulesImage':image,'artImage':a.get('Image',image),'sourceImage':direct or family[0],'sourceImages':family,'sourceSelection':'literal-original-image' if direct else 'default-native-family-member-by-decoded-resource-kind; variation belongs to renderer','instanceCount':count,'originalRules':rule,'originalArt':a}
    return result,maps,{'rulesSha256':m.digest(rulepath),'artSha256':m.digest(artpath),'tiberiumGroups':{n:dict(rules[n]) for n in ('Tiberiums','Riparius','Cruentus','Vinifera','Aboreus')}}

def prepare(m):
    # A prior unfinished audit may have produced a different interpretation of
    # the tiny real GEM04 shadow masks; reconvert owned outputs with the cache.
    data=m.readj(m.REF/'plan.json');assert m.SHP.exists(),'Cached converter is required; never build'
    for n in NAMES:
        source=ROOT/data['assets'][n+'.tem']['path'];layout,_=m.shadow_layout(source);manifest=OUT/'Sprites'/n/'manifest.json';doc=m.readj(manifest)
        if doc and doc['shadowLayout']!=layout:
            # The converter intentionally refuses overwrite. Export to a new
            # owned stage, then copy its four declared outputs into our export.
            import shutil
            stage=OUT/'ConversionStaging'/(n+'-'+layout);staged=m.readj(stage/'manifest.json')
            if not staged:
                result=subprocess.run(['dotnet',str(m.SHP),'convert','--shp',str(source),'--pal',str(m.PALETTES['isotem']),'--out',str(stage),'--layout','atlas','--columns','16','--shadow-layout',layout],capture_output=True,text=True)
                if result.returncode:raise RuntimeError(result.stderr+' '+result.stdout)
                staged=m.readj(stage/'manifest.json')
            assert staged['shadowLayout']==layout and staged['sourceSha256']==m.digest(source)
            files={'manifest.json'}|{image[key] for image in staged['images'] for key in ('color','indices','teamMask')}
            for filename in files:shutil.copyfile(stage/filename,manifest.parent/filename)
    c=m.prepare();c['complete']=False;audits={}
    for n in NAMES:
        entry=c['sprites'][n];doc=m.readj(ROOT/entry['manifest']);source=next(a for a in m.readj(m.REF/'plan.json')['assets'].values() if a['sha256']==doc['sourceSha256']);_,audit=m.shadow_layout(ROOT/source['path']);count=len(audit)
        assert count==24 and all(not f['empty'] and not f['onlyShadowIndices'] for f in audit[:12]),n
        tail=audit[12:];classification='empty' if all(f['empty'] for f in tail) else 'shadow' if all(f['onlyShadowIndices'] for f in tail) else 'color'
        assert classification in ('empty','shadow'),(n,classification)
        entry.update(sourceImage=n.upper(),sourceName=source['name'],kind='ore' if n.startswith('gold') else 'gem',densityFrameCount=12,densityFrames=list(range(12)),totalFrameCount=count,nativeCanvas={'width':doc['canvasWidth'],'height':doc['canvasHeight']},nativeAnchor={'x':doc['canvasWidth']/2,'y':doc['canvasHeight']/2},tailFrames={'start':12,'count':12,'classification':classification,'hasVisibleShadow':classification=='shadow','nonemptyFrames':[f['index'] for f in tail if not f['empty']]},densityOpaquePixelCounts=[f['opaquePixelCount'] for f in audit[:12]],palette='isotem',shadowLayout=doc['shadowLayout'],colorFrames=12)
        audits[n]={'sourceName':source['name'],'sourceSha256':source['sha256'],'frames':audit,'densityFrames':list(range(12)),'tailClassification':classification}
    c['overlayImages'],c['mapResourceCounts'],c['sourceMetadata']=overlay_metadata(m)
    c['resourceKinds']={'1':{'kind':'ore','sourceImages':list(NAMES[:4])},'2':{'kind':'gem','sourceImages':list(NAMES[4:])}}
    c['densityConvention']={'nativeFrames':list(range(12)),'originalMaximumFrame':11,'emptyTailMustNotBeSelected':True,'amountToFrame':'Renderer responsibility; do not apply a historical 180-unit divisor to source OverlayData without the simulation amount scale.'}
    c['aliasConvention']='Exact original Rules.Image/Art.Image is retained independently. TIB*/TIB2* are legacy resource overlay names; decoded ResourceKind selects the ore GOLD family. A default family member is not claimed as an explicit INI alias or original variation-selection algorithm.'
    c['counts']={'sprites':8,'overlayNames':len(c['overlayImages']),'densityFrames':96,'sourceFrames':192};c['complete']=True
    m.savej(OUT/'frame-audit.json',{'schema':1,'sprites':audits});m.savej(OUT/'catalog.json',c);m.emit(phase='resource-catalog-stable',counts=c['counts'],tailClassification={n:c['sprites'][n]['tailFrames']['classification'] for n in NAMES})

def validate(m):
    c=m.readj(OUT/'catalog.json');p=m.readj(m.REF/'plan.json');native=m.native();checked={'sources':0,'frames':0,'densityRgbaComparisons':0,'emptyTailRgbaComparisons':0,'nativeShadowCoverageComparisons':0,'remapMasks':0,'zeroIndexTransparentPixels':0};largest=0;comparisons=[];pixel_checks=[]
    assert c['complete'] and len(c['sprites'])==8 and not c['missing'] and not c['voxels'];assert c['metadataSha256']==p['metadataSha256']==m.digest(m.META)
    for a in p['assets'].values():
        path=ROOT/a['path'];assert path.stat().st_size==a['bytes'] and m.digest(path)==a['sha256'];checked['sources']+=1
    for n,entry in c['sprites'].items():
        mp=ROOT/entry['manifest'];d=m.readj(mp);assert len(d['images'])==1 and d['shadowLayout']==entry['shadowLayout'] and len(d['frames'])==24;assert d['palette']['sha256']==m.digest(m.PALETTES['isotem'])
        assert entry['densityFrameCount']==12 and entry['tailFrames']['classification'] in ('empty','shadow')
        image=d['images'][0];decoded={key:native.png(mp.parent/image[key],True) for key in ('color','indices','teamMask')};aw,ah,_=decoded['indices'];largest=max(largest,aw*ah);assert aw*ah<=16_777_216;source=next(a for a in p['assets'].values() if a['sha256']==d['sourceSha256']);raw=(ROOT/source['path']).read_bytes()
        for f in d['frames']:
            assert f['imageSet']==0
            shadow=f.get('role')=='shadow'
            if d['shadowLayout']=='unspecified':assert f.get('pairedFrame') is None
            elif f['index']<12:assert f.get('pairedFrame')==f['index']+12
            header,indices=m.frame_indices(raw,f['index']);rect=f['imageRect'];x,y,w,h=[rect[k] for k in ('x','y','width','height')];assert 0<=x<=x+w<=aw and 0<=y<=y+h<=ah
            crop=b''.join(decoded['indices'][2][(y+row)*aw+x:(y+row)*aw+x+w] for row in range(h));assert crop==indices and hashlib.sha256(crop).hexdigest()==f['indicesSha256']
            mask=b''.join(decoded['teamMask'][2][(y+row)*aw+x:(y+row)*aw+x+w] for row in range(h));assert mask==bytes(i-15 if 16<=i<=31 and not shadow else 0 for i in crop);checked['remapMasks']+=1
            rgba=b''.join(decoded['color'][2][((y+row)*aw+x)*4:((y+row)*aw+x+w)*4] for row in range(h))
            for i,index in enumerate(crop):
                assert rgba[i*4+3]==((128 if shadow else 255) if index else 0)
                if index==0:checked['zeroIndexTransparentPixels']+=1
            if f['index']<12:assert any(crop)
            elif shadow:assert set(crop)<={0,1}
            else:assert not any(crop)
            if shadow:
                assert rgba==bytes(channel for index in crop for channel in (0,0,0,128 if index else 0));checked['nativeShadowCoverageComparisons']+=1
            else:
                result=native.verify_manifest_frame(mp,ROOT/source['path'],m.PALETTES['isotem'],f['index']);assert result['passed'],(n,f['index']);comparisons.append(result);checked['densityRgbaComparisons' if f['index']<12 else 'emptyTailRgbaComparisons']+=1
            checked['frames']+=1
            pixel_checks.append({'sprite':n,'frame':f['index'],'sourceRect':f['sourceRect'],'indicesSha256':hashlib.sha256(crop).hexdigest(),'rgbaSha256':hashlib.sha256(rgba).hexdigest()})
    state=m.readj(m.REF/'fetch-state.json');assert state['receivedBytes']<=1_000_000
    report={'schema':1,'pass':True,'checked':checked,'sprites':8,'largestSpriteAtlasPixels':largest,'allOneImageSet':True,'densityFramesPerSprite':12,'tailClassification':{n:e['tailFrames']['classification'] for n,e in c['sprites'].items()},'missing':[],'budget':state,'builtToolsOrGame':False,'nativeGameLaunched':False}
    m.savej(OUT/'validation.json',report);m.savej(OUT/'native-pixel-comparisons.json',{'pass':True,'comparisons':comparisons,'frameChecks':pixel_checks});m.emit(phase='resource-validation',**report)

def preview(m):
    c=m.readj(OUT/'catalog.json');p=m.readj(m.REF/'plan.json');writer=m.load('resource_native_png_writer',ROOT/'Tools/classic_art/prepare_cursors.py').png_write
    for n,e in c['sprites'].items():
        d=m.readj(ROOT/e['manifest']);source=next(a for a in p['assets'].values() if a['sha256']==d['sourceSha256'])
        for frame in (0,6,11):
            w,h,rgba=m.native().native_shp_frame(ROOT/source['path'],m.PALETTES['isotem'],frame);writer(OUT/'Previews'/f'{n}-{frame:02}.png',w,h,rgba)
    m.emit(phase='resource-preview',nativeImages=24,resized=False)

def main():
    parser=argparse.ArgumentParser()
    for name in ('plan','fetch','prepare','validate','preview'):parser.add_argument('--'+name,action='store_true')
    args=parser.parse_args();m=module()
    for name in ('plan','fetch','prepare','validate','preview'):
        if getattr(args,name):globals()[name](m)
if __name__=='__main__':main()
