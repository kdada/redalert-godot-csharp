"""Independent decoders from published format descriptions, not game code.

LZO1X instruction prose: https://docs.kernel.org/staging/lzo.html
RA2 chunk/record data: https://modenc2.markjfox.net/index.php?title=IsoMapPack5&oldid=12044
LCW instruction data: https://moddingwiki.shikadi.net/wiki/Westwood_LCW
"""
import base64
import hashlib
import struct


def lzo1x(data, expected):
    output = bytearray(); cursor = 0; state = 0
    def byte():
        nonlocal cursor
        if cursor >= len(data):
            raise ValueError('Truncated LZO operand')
        value = data[cursor]; cursor += 1; return value
    def word():
        return byte() | byte() << 8
    def extended(value, mask):
        if value:
            return value
        value = mask
        while True:
            extra = byte()
            value += extra if extra else 255
            if extra:
                return value
    def literals(count):
        nonlocal cursor
        if cursor + count > len(data) or len(output) + count > expected:
            raise ValueError('LZO literal outside declared bounds')
        output.extend(data[cursor:cursor+count]); cursor += count
    if data and data[0] > 17:
        count = byte() - 17; literals(count); state = min(count, 4)
    while cursor < len(data):
        token = byte()
        if token < 16:
            if state == 0:
                literals(3 + extended(token, 15)); state = 4; continue
            length = 3 if state == 4 else 2
            distance = (2049 if state == 4 else 1) + (token >> 2) + (byte() << 2)
            tail = token & 3
        elif token < 32:
            length = 2 + extended(token & 7, 7); operand = word()
            distance = 16384 + ((token & 8) << 11) + (operand >> 2); tail = operand & 3
            if distance == 16384:
                if len(output) != expected or cursor != len(data) or length != 3:
                    raise ValueError('Invalid LZO end marker or output length')
                return bytes(output)
        elif token < 64:
            length = 2 + extended(token & 31, 31); operand = word()
            distance = 1 + (operand >> 2); tail = operand & 3
        else:
            length = (token >> 5) + 1
            distance = 1 + ((token >> 2) & 7) + (byte() << 3); tail = token & 3
        if distance <= 0 or distance > len(output) or len(output) + length > expected:
            raise ValueError('LZO dictionary match outside bounds')
        for _ in range(length):
            output.append(output[-distance])
        literals(tail); state = tail
    raise ValueError('Missing LZO end marker')


def lcw(data, expected):
    output = bytearray(); cursor = 0; relative = bool(data and data[0] == 0)
    if relative:
        cursor = 1
    def byte():
        nonlocal cursor
        if cursor >= len(data):
            raise ValueError('Truncated LCW operand')
        value = data[cursor]; cursor += 1; return value
    def word():
        return byte() | byte() << 8
    def match(start, count):
        if start < 0 or start >= len(output) or len(output) + count > expected:
            raise ValueError('LCW dictionary reference outside bounds')
        for i in range(count):
            output.append(output[start+i])
    while cursor < len(data):
        token = byte()
        if token == 128:
            if len(output) != expected or cursor != len(data):
                raise ValueError('Invalid LCW end marker or output length')
            return bytes(output)
        if token < 128:
            count = (token >> 4) + 3; distance = ((token & 15) << 8) | byte()
            match(len(output)-distance, count)
        elif token < 192:
            count = token & 63
            if cursor + count > len(data) or len(output) + count > expected:
                raise ValueError('LCW literal outside declared bounds')
            output.extend(data[cursor:cursor+count]); cursor += count
        elif token == 254:
            count = word(); value = byte()
            if len(output) + count > expected:
                raise ValueError('LCW fill outside declared bounds')
            output.extend(bytes([value]) * count)
        else:
            count = word() if token == 255 else (token & 63) + 3
            offset = word(); match(len(output)-offset if relative else offset, count)
    raise ValueError('Missing LCW end marker')


def unpack(section, codec):
    values = ''.join(value for key, value in sorted(section.items(), key=lambda item: int(item[0])))
    data = base64.b64decode(values, validate=True); cursor = 0; output = bytearray(); chunks = []
    while cursor < len(data):
        if cursor + 4 > len(data):
            raise ValueError('Truncated map chunk header')
        compressed, raw = struct.unpack_from('<HH', data, cursor); cursor += 4
        if not compressed or not raw or raw > 8192 or cursor + compressed > len(data):
            raise ValueError('Invalid map chunk size')
        payload = data[cursor:cursor+compressed]; cursor += compressed
        result = codec(payload, raw); output.extend(result)
        chunks.append({'compressedBytes': compressed, 'decodedBytes': raw, 'sha256': hashlib.sha256(result).hexdigest()})
    return bytes(output), {'compressedBytes': len(data), 'decodedBytes': len(output), 'chunks': chunks,
                           'decodedSha256': hashlib.sha256(output).hexdigest()}
