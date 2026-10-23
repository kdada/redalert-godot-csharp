#!/usr/bin/env python3
"""Retain a small number of public CnCNet map data/image references, not code."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import subprocess
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Reference/Terrain/Maps'
REPO = 'CnCNet/cncnet-yr-client-package'


def get(url, limit=2_000_000):
    result = subprocess.run(['curl', '--fail', '--silent', '--show-error', '--proxy', 'http://127.0.0.1:10808',
        '--max-time', '35', '--retry', '1', '--max-filesize', str(limit), url], capture_output=True)
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors='replace'))
    if len(result.stdout) > limit:
        raise RuntimeError('reference file exceeded byte budget')
    return result.stdout


def main():
    commit = json.loads(get(f'https://api.github.com/repos/{REPO}/commits/master'))['sha']
    tree = json.loads(get(f'https://api.github.com/repos/{REPO}/git/trees/{commit}?recursive=1'))['tree']
    candidates = [item['path'] for item in tree if item['path'].lower().endswith('.map')
        and any(name in item['path'].lower() for name in ('isle_of_war', 'mayday_oils_fixed', 'malibu_cliffs_le'))]
    # Include an official-origin classic island map where present, alongside two
    # public client-package maps with their distributed preview images.
    candidates = candidates[:3]
    print(json.dumps({'commit': commit, 'maps': candidates}), flush=True)
    OUT.mkdir(parents=True, exist_ok=True)
    jobs = [path[:-4] + ext for path in candidates for ext in ('.map', '.png')]
    def one(path):
        url = f'https://raw.githubusercontent.com/{REPO}/{commit}/' + quote(path)
        raw = get(url)
        destination = OUT / Path(path).name
        if destination.exists() and destination.read_bytes() != raw:
            raise RuntimeError(f'Refusing changed map reference {destination}')
        destination.write_bytes(raw)
        return {'path': destination.relative_to(ROOT).as_posix(), 'sourceUrl': url,
            'sourceRepository': REPO, 'commit': commit, 'sha256': hashlib.sha256(raw).hexdigest(), 'bytes': len(raw)}
    with ThreadPoolExecutor(max_workers=4) as executor:
        records = list(executor.map(one, jobs))
    (OUT / 'sources.json').write_text(json.dumps({'schema': 1, 'assets': records}, indent=2) + '\n')
    print(json.dumps(records, indent=2))


if __name__ == '__main__':
    main()
