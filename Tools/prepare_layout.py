#!/usr/bin/env python3
"""Record published SHP alignment facts; no external runtime or game implementation."""
import hashlib
import json
from pathlib import Path
import re
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
base='https://raw.githubusercontent.com/OpenRA/ra2/master/mods/ra2/sequences/'
offsets={}
sources=[]
for name in ['allied-structures.yaml','soviet-structures.yaml']:
    url=base+name
    with urllib.request.urlopen(url,timeout=25) as response:
        raw=response.read(1_000_001)
    if len(raw)>1_000_000: raise RuntimeError('Unexpected sequence document size')
    sources.append({'url':url,'sha256':hashlib.sha256(raw).hexdigest()})
    filename=None
    in_defaults=False
    for line in raw.decode().splitlines():
        if line and not line.startswith((' ', '\t','#')):
            filename=None;in_defaults=False
        if line=='\tDefaults:':
            in_defaults=True;continue
        if line.startswith('\t') and not line.startswith('\t\t'):
            in_defaults=False
        if not in_defaults: continue
        if line.startswith('\t\tFilename:'):
            filename=Path(line.split(':',1)[1].strip()).stem
        if filename and line.startswith('\t\tOffset:'):
            values=[int(v.strip()) for v in line.split(':',1)[1].split(',')]
            offsets.setdefault(filename,{'x':values[0],'y':values[1]})
folder=ROOT/'Assets/Generated/Sprites'
(folder/'layout.json').write_text(json.dumps({'schema':1,'note':'Published pixel alignment facts, relative to nominal SHP canvas center. Numeric data only.','sources':sources,'offsets':offsets},indent=2))
print('Recorded',len(offsets),'building alignment offsets')
