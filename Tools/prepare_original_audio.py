#!/usr/bin/env python3
"""Decode original RA2 mono IMA ADPCM to PCM WAV; copy official MP3 unchanged.

Independent standard IMA ADPCM decoder. No synthesis, resampling, normalization,
melody changes, or external codec/build is used. Only the selected original assets
listed in Assets/Reference/Audio/manifest.json become active runtime audio.
"""
from __future__ import annotations
import argparse
from array import array
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
import wave

ROOT = Path(__file__).resolve().parents[1]
REF = ROOT / 'Assets/Reference/Audio'
OUT = ROOT / 'Assets/Generated/Audio'
STEP = (7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767)
INDEX = (-1,-1,-1,-1,2,4,6,8)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def decode_ima(data: bytes, block_align: int, frames: int | None = None) -> array:
    if not 4 <= block_align <= 65536:
        raise ValueError('Invalid mono IMA block size')
    pcm = array('h')
    for begin in range(0, len(data), block_align):
        block = data[begin:begin+block_align]
        if len(block) < 4:
            raise ValueError('Truncated IMA predictor header')
        predictor, index, reserved = struct.unpack_from('<hBB', block)
        if index > 88 or reserved != 0:
            raise ValueError('Invalid standard IMA state header')
        pcm.append(predictor)
        for encoded in block[4:]:
            for nibble in (encoded & 15, encoded >> 4):
                step = STEP[index]
                delta = step >> 3
                if nibble & 1:
                    delta += step >> 2
                if nibble & 2:
                    delta += step >> 1
                if nibble & 4:
                    delta += step
                predictor += -delta if nibble & 8 else delta
                predictor = max(-32768, min(32767, predictor))
                index = max(0, min(88, index + INDEX[nibble & 7]))
                pcm.append(predictor)
    if frames is not None:
        if not 0 < frames <= len(pcm):
            raise ValueError('Invalid WAV fact sample count')
        del pcm[frames:]
    return pcm


def decode_wav(raw: bytes) -> tuple[array, int]:
    if raw[:4] != b'RIFF' or raw[8:12] != b'WAVE':
        raise ValueError('Expected original RIFF WAVE')
    chunks = {}
    offset = 12
    while offset + 8 <= len(raw):
        name, size = struct.unpack_from('<4sI', raw, offset)
        begin = offset + 8
        if begin + size > len(raw):
            raise ValueError('Truncated original WAV chunk')
        chunks[name] = raw[begin:begin+size]
        offset = begin + size + (size & 1)
    fmt = chunks[b'fmt ']
    codec, channels, rate, _, align, bits = struct.unpack_from('<HHIIHH', fmt)
    if channels != 1 or not 8000 <= rate <= 48000:
        raise ValueError('Selected original WAV must be mono')
    if codec == 0x11:
        frames = struct.unpack_from('<I', chunks[b'fact'])[0] if b'fact' in chunks else None
        return decode_ima(chunks[b'data'], align, frames), rate
    if codec == 1 and bits == 16:
        pcm = array('h')
        pcm.frombytes(chunks[b'data'])
        if sys.byteorder != 'little':
            pcm.byteswap()
        return pcm, rate
    raise ValueError(f'Unsupported original WAV codec {codec}')


def wav_bytes(pcm: array, rate: int) -> bytes:
    output = io.BytesIO()
    data = array('h', pcm)
    if sys.byteorder != 'little':
        data.byteswap()
    with wave.open(output, 'wb') as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(rate)
        stream.writeframes(data.tobytes())
    return output.getvalue()


def generate():
    # Raw EVA WAVs use IMA ADPCM and must never be imported by Godot.
    # Repair older source downloads before producing the PCM runtime assets.
    ignore = REF / '.gdignore'
    if not ignore.exists():
        ignore.write_text('# Source audio and provenance only; import Assets/Generated/Audio/Original instead.\n')
    source = json.loads((REF/'manifest.json').read_text())
    if source.get('synthetic_audio') is not False:
        raise ValueError('Original-only source manifest required')
    records = []
    for asset in sorted(source['assets'], key=lambda x: x.get('cue', '')):
        cue = asset.get('cue')
        if not cue:
            continue
        path = ROOT / asset['path']
        raw = path.read_bytes()
        if sha(raw) != asset['sha256']:
            raise ValueError('Original source hash mismatch')
        original = asset['original_filename']
        if original.endswith('.mp3'):
            if raw[:3] != b'ID3' and raw[:2] not in (b'\xff\xfb', b'\xff\xf3', b'\xff\xf2'):
                raise ValueError('Expected original MP3 header')
            name, output, method = original, raw, 'Unmodified composer-provided MP3 bytes'
            extra = dict(codec='MP3', loop=True)
        else:
            if path.suffix == '.bag-sample':
                if asset['flags'] != 12:
                    raise ValueError('Expected selected 16-bit mono IMA ADPCM BAG sample')
                pcm, rate = decode_ima(raw, asset['chunk_size']), asset['sample_rate']
            else:
                pcm, rate = decode_wav(raw)
            name = original
            output = wav_bytes(pcm, rate)
            method = 'Original mono IMA ADPCM decoded to signed PCM16; no resampling, normalization or synthesis'
            extra = dict(codec='PCM16', channels=1, rate=rate, frames=len(pcm), seconds=round(len(pcm)/rate,6),
                         peak_pcm=max(abs(x) for x in pcm), loop=False)
        relative = 'Original/' + name
        target = OUT/relative
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists() and target.read_bytes() != output:
            raise ValueError(f'Refusing changed converted original {target}')
        if not target.exists():
            target.write_bytes(output)
        records.append(dict(cue=cue, file=relative, resource_path='res://Assets/Generated/Audio/'+relative,
            original_filename=original, original_source_path=asset['path'], source_sha256=asset['sha256'],
            source_url=asset.get('source_url'), source_archive=asset.get('source_archive'),
            archive_offset=asset.get('archive_offset'), bytes=len(output), sha256=sha(output), conversion=method, **extra))
    manifest = dict(schema=2, active=True, original_audio=True, synthetic_audio=False,
        rights='EA/Westwood, Frank Klepacki and respective original rights holders; no CC0 or open-source claim.',
        source_manifest='Assets/Reference/Audio/manifest.json', generator='Tools/prepare_original_audio.py',
        generator_sha256=sha(Path(__file__).read_bytes()), files=records,
        missing=source['selection'].get('missing', []), legacy_synthesized_files='Inactive; not referenced by GameAudio.ActiveFiles. No generated fallback is used.')
    (OUT/'original-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    print(json.dumps(dict(prepared=len(records), bytes=sum(x['bytes'] for x in records), active_manifest=str(OUT/'original-manifest.json'))))


def validate():
    if not (REF/'.gdignore').is_file():
        raise ValueError('Original compressed audio must be excluded from Godot import with Assets/Reference/Audio/.gdignore')
    manifest = json.loads((OUT/'original-manifest.json').read_text())
    for entry in manifest['files']:
        raw = (OUT/entry['file']).read_bytes()
        if sha(raw) != entry['sha256']:
            raise ValueError('Runtime original hash mismatch')
        if entry['codec'] == 'PCM16':
            with wave.open(io.BytesIO(raw), 'rb') as stream:
                if stream.getnframes() != entry['frames'] or stream.getframerate() != entry['rate'] or stream.getsampwidth() != 2:
                    raise ValueError('Converted WAV header mismatch')
            if entry['peak_pcm'] <= 0:
                raise ValueError('Silent original effect')
        elif sha(raw) != entry['source_sha256']:
            raise ValueError('MP3 was changed from the downloaded original')
    print(json.dumps(dict(valid=True, active_originals=len(manifest['files']), pcm_wavs=sum(x['codec']=='PCM16' for x in manifest['files']),
        unchanged_original_mp3s=sum(x['codec']=='MP3' for x in manifest['files']), checks='Original/runtime hashes, mono IMA states, RIFF PCM headers, signal presence; no playback')))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--validate', action='store_true')
    args = parser.parse_args()
    if not args.validate:
        generate()
    validate()
