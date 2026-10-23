#!/usr/bin/env python3
"""Losslessly package native PNG frame captures as a standalone animated PNG."""
import argparse,hashlib,struct,zlib
from pathlib import Path


def chunks(path):
    data=path.read_bytes();assert data[:8]==b'\x89PNG\r\n\x1a\n',path
    cursor=8;result=[]
    while cursor<len(data):
        length=struct.unpack_from('>I',data,cursor)[0];kind=data[cursor+4:cursor+8];payload=data[cursor+8:cursor+8+length]
        assert len(payload)==length and zlib.crc32(kind+payload)&0xffffffff==struct.unpack_from('>I',data,cursor+8+length)[0],path
        result.append((kind,payload));cursor+=length+12
    assert cursor==len(data) and result[-1][0]==b'IEND'
    return result


def chunk(kind,payload):
    return struct.pack('>I',len(payload))+kind+payload+struct.pack('>I',zlib.crc32(kind+payload)&0xffffffff)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--prefix',required=True);parser.add_argument('--count',type=int,default=10);parser.add_argument('--delay-ms',type=int,default=150);parser.add_argument('--out',required=True)
    args=parser.parse_args();assert 2<=args.count<=120 and 1<=args.delay_ms<=65535
    frames=[chunks(Path(f'{args.prefix}{i:02d}.png')) for i in range(args.count)]
    header=frames[0][0][1];assert frames[0][0][0]==b'IHDR'
    assert all(frame[0]==(b'IHDR',header) for frame in frames)
    width,height=struct.unpack_from('>II',header);sequence=0
    image=bytearray(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',header)+chunk(b'acTL',struct.pack('>II',args.count,0)))
    compressed=[]
    for index,frame in enumerate(frames):
        control=struct.pack('>IIIIIHHBB',sequence,width,height,0,0,args.delay_ms,1000,0,0);sequence+=1
        image.extend(chunk(b'fcTL',control));payload=b''.join(data for kind,data in frame if kind==b'IDAT');assert payload
        compressed.append(hashlib.sha256(payload).hexdigest())
        if index==0:image.extend(chunk(b'IDAT',payload))
        else:
            image.extend(chunk(b'fdAT',struct.pack('>I',sequence)+payload));sequence+=1
    image.extend(chunk(b'IEND',b''));output=Path(args.out)
    if output.exists():raise FileExistsError(output)
    assert len(set(compressed))>1,'Native frame captures contain no animation changes.'
    output.write_bytes(image);verified=chunks(output)
    assert sum(kind==b'fcTL' for kind,_ in verified)==args.count
    print({'path':str(output),'size':[width,height],'frames':args.count,'distinctNativeFrames':len(set(compressed)),'delayMs':args.delay_ms,'bytes':len(image)})


if __name__=='__main__':main()
