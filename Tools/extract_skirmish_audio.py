#!/usr/bin/env python3
"""Select original vanilla cues; fetch exact audio.bag ranges, decode mono IMA.

No complete archive, synthesis, normalization or simulation mutation. Source INIs
and IDX must match the already retained, pinned original-audio manifest. Outputs
are isolated from the existing 41 clips and the ClassicExpansion visual scope.
"""
from __future__ import annotations
import argparse
from array import array
import configparser
from concurrent.futures import ThreadPoolExecutor
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import wave

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
REF = ROOT / 'Assets/Reference/Audio'
DEST = REF / 'SkirmishCues'
OUT = ROOT / 'Assets/Generated/Audio/SkirmishCues'
CS = ROOT / 'Scripts/GameAudio.ClassicCueCatalog.cs'
BUDGET = 4_000_000
UNIT_IDS = dict(zip('Gi Conscript Grizzly Rhino ChronoMiner WarMiner Engineer Dog Sniper TeslaTank PrismTank Apocalypse Ifv FlakTrack FlakTrooper TeslaTrooper Rocketeer Tanya Spy Yuri CrazyIvan Desolator Terrorist ChronoLegionnaire MirageTank TankDestroyer TerrorDrone V3Launcher DemolitionTruck Mcv Nighthawk Kirov Harrier BlackEagle AmphibiousTransport Destroyer AegisCruiser AircraftCarrier Dolphin AttackSubmarine SeaScorpion Dreadnought GiantSquid ChronoCommando PsiCommando ChronoIvan YuriPrime'.split(),
    'E1 E2 MTNK HTNK CMIN HARV ENGINEER ADOG SNIPE TTNK SREF APOC FV HTK FLAKT SHK JUMPJET TANY SPY YURI IVAN DESO TERROR CLEG MGTK TNKD DRON V3 DTRUCK AMCV SHAD ZEP ORCA BEAG LCRF DEST AEGIS CARRIER DLPH SUB HYD DRED SQD CCOMAND PTROOP CIVAN YURIPR'.split(), strict=True))
SOVIET_IDS = dict(Engineer='SENGINEER', Dog='DOG', Mcv='SMCV', AmphibiousTransport='SAPC')
DEFENCES = ['GAPILL', 'NATSLA', 'GTGCAN', 'NALASR', 'NASAM', 'ATESLA', 'NAFLAK']
EXTRA_WEAPONS = 'HoverMissile RepairBullet CRM60 CRFlakGuyGun CRMP5 AWPE CRElectricBolt CRNuke CRMindControl CRRadBeamWeapon CRNeutronRifle CRTerrorBomb CowShot CometFragment PsiWave RadEruptionWeapon Sapper FakeC4 TerrorBomb Demobomb IvanBomber Para M60 HornetBomb ASWBomb NeutronRifle'.split()
CRATES = dict(Credits='CrateMoney', Repair='HealCrate', Reveal='CrateReveal', Veteran='CratePromoted')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def retain(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if path.read_bytes() != data:
            raise ValueError(f'Refusing changed retained original/output: {path}')
    else:
        path.write_bytes(data)


def generated(path, data):
    if path.resolve() not in {CS.resolve(), (DEST / 'selection.json').resolve(), (DEST / 'manifest.json').resolve()}:
        raise ValueError('Unexpected generated output path')
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


def ini(path):
    parser = configparser.ConfigParser(strict=False, interpolation=None, allow_no_value=True,
        inline_comment_prefixes=(';',), comment_prefixes=(';', '#'), delimiters=('=',))
    # Westwood permits an inline comment containing another [name] after a
    # section header (e.g. [CowShot];[PrismShot]); ConfigParser is greedy there.
    text = re.sub(r'(?m)^(\s*\[[^\]\r\n]+\]).*$', r'\1', path.read_text(encoding='latin1'))
    parser.read_string(text)
    return {section.casefold(): {key.casefold(): (value or '').strip() for key, value in parser.items(section)} for section in parser.sections()}


def source_plan():
    parent = json.loads((REF / 'manifest.json').read_text())
    if parent['source_revision'] != 'acfa5b834899e7fab4c8499ccb4b49c402927b44' or parent.get('synthetic_audio') is not False:
        raise ValueError('Pinned original source manifest required')
    metadata = {}
    for name in ['rules.ini', 'sound.ini', 'audio.idx']:
        path = REF / 'Metadata' / name
        record = next(x for x in parent['assets'] if x['path'] == path.relative_to(ROOT).as_posix())
        digest = sha(path.read_bytes())
        if digest != record['sha256']:
            raise ValueError('Retained metadata hash mismatch: ' + name)
        metadata[name] = dict(path=record['path'], sha256=digest)
    data = (REF / 'Metadata/audio.idx').read_bytes()
    magic, version, count = struct.unpack_from('<4sII', data)
    if magic != b'GABA' or version not in (1, 2) or len(data) != 12 + 36 * count:
        raise ValueError('Invalid original audio IDX')
    index = {}
    for i in range(count):
        name, offset, size, rate, flags, block = struct.unpack_from('<16sIIIII', data, 12 + 36 * i)
        name = name.split(b'\0')[0].decode('ascii').lower()
        index[name] = dict(name=name, offset=offset, bytes=size, sample_rate=rate, flags=flags, chunk_size=block)
    rules, sounds = ini(REF / 'Metadata/rules.ini'), ini(REF / 'Metadata/sound.ini')
    prior = json.loads((REF / 'Metadata/selection.json').read_text())
    reuse = {v['sample'].lower(): key for key, v in prior['bag'].items()}
    aliases, selected = {}, {}

    def cue(sound_id):
        sound_id = sound_id.strip()
        if not sound_id or sound_id.casefold() in ('none', '<none>', 'dummy'):
            return ''
        sound = sounds.get(sound_id.casefold())
        if sound is None:
            raise ValueError('Original sound section missing: ' + sound_id)
        names = [x.lstrip('$').removesuffix('.wav').lower() for x in re.split(r'[\s,]+', sound.get('sounds', '')) if x]
        if not names and sound.get('volume') == '0':
            aliases[sound_id] = dict(cue='', sample='', original_sound=sound, all_sample_names=[], intentional_silence=True)
            return ''
        name = next((name for name in names if name in index), None)
        if not name:
            raise ValueError('No indexed original sample for sound: ' + sound_id)
        key = reuse.get(name, 'original_sample_' + name)
        aliases[sound_id] = dict(cue=key, sample=name, original_sound=sound, all_sample_names=names)
        if name not in reuse:
            selected[name] = index[name]
        return key

    units, soviet, weapons, weapon_ids = {}, {}, {}, set(EXTRA_WEAPONS)
    def unit(rule_id):
        fields = rules.get(rule_id.casefold())
        if fields is None:
            raise ValueError('Missing original unit: ' + rule_id)
        for key in ['primary', 'secondary', 'eliteprimary', 'elitesecondary']:
            if fields.get(key) and fields[key].casefold() not in ('none', '<none>'):
                weapon_ids.add(fields[key])
        return dict(rules_id=rule_id, select=cue(fields.get('voiceselect', '')),
            order=cue(fields.get('voicemove', '')), created=cue(fields.get('createsound', '')),
            aux1=cue(fields.get('auxsound1', '')) if fields.get('auxsound1') else '')
    for enum, rule_id in UNIT_IDS.items():
        units[enum] = unit(rule_id)
    for enum, rule_id in SOVIET_IDS.items():
        soviet[enum] = unit(rule_id)
    for rule_id in DEFENCES + ['HORNET', 'ASW', 'V3ROCKET', 'DMISL']:
        fields = rules.get(rule_id.casefold(), {})
        for key in ['primary', 'secondary', 'eliteprimary', 'elitesecondary']:
            if fields.get(key) and fields[key].casefold() not in ('none', '<none>'):
                weapon_ids.add(fields[key])
    weapon_sources = {}
    for weapon in sorted(weapon_ids, key=str.casefold):
        fields = rules.get(weapon.casefold())
        if fields is None:
            raise ValueError('Missing original weapon: ' + weapon)
        reports = [x.strip() for x in fields.get('report', '').split(',') if x.strip()]
        weapons[weapon] = cue(reports[0]) if reports else ''
        weapon_sources[weapon] = dict(report=reports, source_section=weapon)
    # These launcher weapons have no Report. Original child aircraft/missile
    # AuxSound1 supplies takeoff audio at actual launch rather than at impact.
    for weapon, child in dict(HornetLauncher='HORNET', ASWLauncher='ASW', DredLauncher='DMISL', V3Launcher='V3ROCKET').items():
        sound_id = rules[child.casefold()].get('auxsound1', '')
        weapons[weapon] = cue(sound_id)
        weapon_sources[weapon] = dict(report=[], source_section=child, source_field='AuxSound1', sound_id=sound_id)
    crates = {kind: cue(sound) for kind, sound in CRATES.items()}
    if len({key.casefold() for key in weapons}) != len(weapons):
        raise ValueError('Case-insensitive original weapon key collision')
    required_bytes = sum(x['bytes'] for x in selected.values())
    if required_bytes > BUDGET:
        raise ValueError(f'Planned byte ranges {required_bytes} exceed {BUDGET}')
    for entry in selected.values():
        if entry['flags'] not in (6,12) or entry['offset'] + entry['bytes'] > parent['bag_bytes']:
            raise ValueError('Selected sample is not bounded mono PCM/IMA: ' + entry['name'])
        if entry['flags'] == 6 and entry['bytes'] % 2:
            raise ValueError('Original mono PCM16 has an incomplete sample')
    return dict(schema=1, source_revision=parent['source_revision'], source_url='https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/' + parent['source_revision'] + '/game-res/language.mix',
        source_archive='language.mix/audio.mix/audio.bag', metadata=metadata, bag_offset=parent['bag_offset'],
        bag_bytes=parent['bag_bytes'], transfer_budget=BUDGET, planned_transfer_bytes=required_bytes,
        original_audio=True, synthetic_audio=False, units=units, soviet_units=soviet, weapons=weapons,
        weapon_sources=weapon_sources, crates=crates, sounds=aliases, samples=selected,
        reuse={name: reuse[name] for name in sorted({x['sample'] for x in aliases.values()} & reuse.keys())})


def fetch(plan, entry):
    begin = plan['bag_offset'] + entry['offset']
    size = entry['bytes']
    url = plan['source_url']
    proxy = os.environ.get('ALL_PROXY') or 'socks5h://127.0.0.1:10808'
    command = ['curl', '--proxy', proxy, '--fail', '--silent', '--show-error', '--include',
        '--max-time', '40', '--max-filesize', str(size), '--range', f'{begin}-{begin+size-1}',
        '--header', 'Accept-Encoding: identity', url + f'?skirmish-audio={begin}-{size}']
    result = subprocess.run(command, capture_output=True, timeout=45)
    if result.returncode:
        raise RuntimeError('Original range failed for ' + entry['name'] + ': ' + result.stderr.decode(errors='replace')[:250])
    remaining = result.stdout
    while remaining.startswith(b'HTTP/'):
        header, separator, remaining = remaining.partition(b'\r\n\r\n')
        if not separator:
            raise ValueError('Incomplete HTTP response headers')
    status = re.search(rb'HTTP/[^ ]+ (\d+)', header)
    content_range = re.search(rb'(?im)^content-range:\s*([^\r\n]+)', header)
    expected = f'bytes {begin}-{begin+size-1}/'
    if not status or status[1] != b'206' or not content_range or not content_range[1].decode().startswith(expected) or len(remaining) != size:
        raise ValueError('Exact HTTP206 range required; refusing archive/full response')
    return remaining, dict(source_url=url, archive_offset=begin, http_range=f'bytes={begin}-{begin+size-1}',
        content_range=content_range[1].decode(), bytes=size, sha256=sha(remaining))


def emit_catalog(plan):
    quote = lambda text: json.dumps(text, ensure_ascii=True)
    lines = ['// Generated by Tools/extract_skirmish_audio.py from hash-verified original INIs.',
        'using RedAlert.Simulation;', 'namespace RedAlert;', 'public sealed partial class GameAudio', '{',
        '    static void AddClassicFiles(Dictionary<string,string> files)', '    {']
    for name in sorted(plan['samples']):
        lines.append(f'        files.Add({quote("original_sample_"+name)}, {quote("res://Assets/Generated/Audio/SkirmishCues/"+name+".wav")});')
    lines += ['    }']
    for label, records in [('ClassicUnitCues', plan['units']), ('ClassicSovietUnitCues', plan['soviet_units'])]:
        lines += [f'    static readonly Dictionary<UnitType,ClassicUnitCue> {label}=new()', '    {']
        for enum, row in records.items():
            fields = ','.join(quote(row[key]) for key in ['select', 'order', 'created'])
            lines.append(f'        [UnitType.{enum}]=new({fields}), // {row["rules_id"]}')
        lines += ['    };']
    lines += ['    static readonly Dictionary<string,string> ClassicWeaponCues=new(StringComparer.OrdinalIgnoreCase)', '    {']
    for weapon, key in plan['weapons'].items():
        lines.append(f'        [{quote(weapon)}]={quote(key)},')
    lines += ['    };', '    static string ClassicCrateCue(ClassicCrateKind kind)=>kind switch', '    {']
    for kind, key in plan['crates'].items():
        lines.append(f'        ClassicCrateKind.{kind}=>{quote(key)},')
    lines += ['        _=>""', '    };', '}', '']
    generated(CS, '\n'.join(lines).encode())


def acquire(plan):
    generated(DEST / 'selection.json', (json.dumps(plan, indent=2, ensure_ascii=False)+'\n').encode())
    spec = importlib.util.spec_from_file_location('skirmish_original_ima', ROOT / 'Tools/prepare_original_audio.py')
    decoder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(decoder)
    def sample(entry):
        source = DEST / 'Original' / (entry['name'] + '.bag-sample')
        # Every invocation performs a fresh exact-range check; cached source may
        # not replace the remote provenance response with an invented HTTP proof.
        raw, provenance = fetch(plan, entry)
        retain(source, raw)
        if entry['flags'] == 12:
            pcm = decoder.decode_ima(raw, entry['chunk_size'])
        else:
            pcm = array('h');pcm.frombytes(raw)
            if sys.byteorder != 'little':
                pcm.byteswap()
        output = decoder.wav_bytes(pcm, entry['sample_rate'])
        target = OUT / (entry['name'] + '.wav')
        retain(target, output)
        return dict(name=entry['name'], cue='original_sample_' + entry['name'], source_path=source.relative_to(ROOT).as_posix(),
            source_sha256=sha(raw), resource_path='res://' + target.relative_to(ROOT).as_posix(), sha256=sha(output),
            rate=entry['sample_rate'], frames=len(pcm), seconds=round(len(pcm)/entry['sample_rate'],6),
            peak_pcm=max(abs(value) for value in pcm), codec='PCM16', channels=1, bytes=len(output), range=provenance)
    with ThreadPoolExecutor(max_workers=5) as pool:
        records = list(pool.map(sample, sorted(plan['samples'].values(), key=lambda x: x['name'])))
    manifest = {**plan, 'files': records, 'transferred_bytes': sum(x['range']['bytes'] for x in records),
        'conversion': 'Original mono IMA ADPCM decoded to PCM16, or original PCM16 wrapped unchanged; no resampling, normalization, synthesis or substitution',
        'bag_format_reference': 'https://github.com/OpenRA/OpenRA/pull/7573 (format flags only; independent standard PCM/IMA handling)'}
    generated(DEST / 'manifest.json', (json.dumps(manifest, indent=2, ensure_ascii=False)+'\n').encode())
    emit_catalog(plan)
    validate(manifest)


def validate(manifest=None):
    manifest = manifest or json.loads((DEST / 'manifest.json').read_text())
    if manifest['transferred_bytes'] > BUDGET or manifest['synthetic_audio'] is not False:
        raise ValueError('Original/bounded acquisition contract failed')
    prior = json.loads((REF / 'Metadata/selection.json').read_text())
    available = set(prior['bag']) | set(prior['wav']) | set(prior['music']) | {record['cue'] for record in manifest['files']}
    requested = set(manifest['weapons'].values()) | set(manifest['crates'].values())
    for row in list(manifest['units'].values()) + list(manifest['soviet_units'].values()):
        requested.update(row[key] for key in ('select', 'order', 'created'))
    if requested - available - {''}:
        raise ValueError('Audio binding references an unavailable original cue')
    if len({key.casefold() for key in manifest['weapons']}) != len(manifest['weapons']):
        raise ValueError('Case-insensitive original weapon key collision')
    for record in manifest['files']:
        source = (ROOT / record['source_path']).read_bytes()
        output = (ROOT / record['resource_path'].removeprefix('res://')).read_bytes()
        if sha(source) != record['source_sha256'] or sha(source) != record['range']['sha256'] or sha(output) != record['sha256']:
            raise ValueError('Source/runtime hash mismatch: ' + record['name'])
        with wave.open(io.BytesIO(output), 'rb') as stream:
            if (stream.getnchannels(), stream.getsampwidth(), stream.getframerate(), stream.getnframes()) != (1,2,record['rate'],record['frames']):
                raise ValueError('Decoded original PCM header mismatch')
            if manifest['samples'][record['name']]['flags'] == 6 and stream.readframes(record['frames']) != source:
                raise ValueError('Original PCM data was changed while wrapping WAV')
        if record['peak_pcm'] <= 0:
            raise ValueError('Silent original sample')
    print(json.dumps(dict(valid=True, new_originals=len(manifest['files']), reused_originals=len(manifest['reuse']),
        mapped_units=len(manifest['units']), mapped_weapons=len(manifest['weapons']), mapped_sound_ids=len(manifest['sounds']),
        transfer_bytes=manifest['transferred_bytes'], checks='source/runtime SHA256, exact HTTP206 ranges, mono PCM16 headers, nonzero original signal; no playback')), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--plan', action='store_true')
    parser.add_argument('--fetch', action='store_true')
    parser.add_argument('--validate', action='store_true')
    args = parser.parse_args()
    if args.validate:
        validate()
    else:
        plan = source_plan()
        if args.fetch:
            acquire(plan)
        else:
            generated(DEST / 'selection.json', (json.dumps(plan, indent=2, ensure_ascii=False)+'\n').encode())
            print(json.dumps(dict(new_samples=len(plan['samples']), reused_samples=len(plan['reuse']), sound_ids=len(plan['sounds']),
                units=len(plan['units']), weapons=len(plan['weapons']), planned_transfer_bytes=plan['planned_transfer_bytes'], budget=BUDGET)), flush=True)
