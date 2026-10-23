#!/usr/bin/env python3
"""Retain original renderer scalars omitted from the compact artwork catalog."""
import configparser,hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def ini(path):
 p=configparser.ConfigParser(strict=False,interpolation=None,allow_no_value=True,inline_comment_prefixes=(';',));p.optionxform=str;p.read(path,encoding='utf-8');return p
roster=json.loads((ROOT/'Tools/classic_art/roster.json').read_text())
rules_path=ROOT/roster['sources']['rules']['path'];art_path=ROOT/roster['sources']['art']['path'];rules=ini(rules_path);art=ini(art_path)
result={'schema':1,'sources':{'rulesSha256':hashlib.sha256(rules_path.read_bytes()).hexdigest(),'artSha256':hashlib.sha256(art_path.read_bytes()).hexdigest()},'units':{},'buildings':{}}
for kind,records in [('units',roster['units']+roster['auxiliary']),('buildings',roster['buildings'])]:
 for record in records:
  section=rules[record['rulesId']];fields={k:v for k,v in section.items() if k.lower() in {'ifvmode','constructionyard','refinery','unitrepair','nospawnalt','turret','turretcount','ischargeturret'}}
  result[kind][record['rulesId']]={'art':dict(art[record['artId']]) if record['artId'] in art else {},'rules':fields,'buildingArtOffsets':record.get('buildingArtOffsets',{})}
path=ROOT/'Assets/Generated/ClassicExpansion/consumer-metadata.json';path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'unitProfiles':len(result['units']),'buildingProfiles':len(result['buildings']),'path':path.relative_to(ROOT).as_posix()}))
