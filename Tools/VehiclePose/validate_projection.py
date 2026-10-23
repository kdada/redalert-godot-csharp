#!/usr/bin/env python3
"""Bounded, standalone geometry/image check; never invokes Godot or C#."""
from pathlib import Path
import gzip,hashlib,json,math,struct,zlib
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Reports/vehicle-slope'
W,H,AX,AY=128,96,64,70

def add(a,b):return tuple(x+y for x,y in zip(a,b))
def mul(a,k):return tuple(x*k for x in a)
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def cross(a,b):return(a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def norm(a):return mul(a,1/math.sqrt(dot(a,a)))
def png(path,w,h,rgba):
 def chunk(t,b):return struct.pack('>I',len(b))+t+b+struct.pack('>I',zlib.crc32(t+b)&0xffffffff)
 scan=b''.join(b'\0'+rgba[y*w*4:(y+1)*w*4] for y in range(h))
 path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(scan,9))+chunk(b'IEND',b''))

def main():
 OUT.mkdir(parents=True,exist_ok=True)
 catalog=json.loads((ROOT/'Assets/Generated/VehiclePoses/catalog.json').read_text());entry=catalog['models']['grizzly']
 packed=(ROOT/'Assets/Generated/VehiclePoses'/entry['file']).read_bytes();assert hashlib.sha256(packed).hexdigest()==entry['sha256']
 raw=gzip.decompress(packed);assert raw[:8]==b'RAVP1\0\0\0';count,scale,elev,az=struct.unpack_from('<ifff',raw,8)
 palette=raw[24:792];offset=792;parts=[];bottom=math.inf
 for _ in range(count):
  role,n=struct.unpack_from('<Bi',raw,offset);offset+=5;faces=[]
  yaw=(az+(4 if role==0 else 20)*360/32)*math.pi/180;co,si=math.cos(yaw),math.sin(yaw)
  def rotate(v):return(co*v[0]-si*v[1],si*v[0]+co*v[1],v[2])
  for i in range(n):
   values=struct.unpack_from('<15fB',raw,offset);offset+=61
   vertices=[rotate(values[j:j+3]) for j in (0,3,6,9)];normal=rotate(values[12:15]);index=values[15]
   faces.append((vertices,normal,index))
   if role==0:bottom=min(bottom,*(v[2] for v in vertices))
  parts.extend(faces)
 assert offset==len(raw)
 sin_e,cos_e=math.sin(math.radians(elev)),math.cos(math.radians(elev));camera=(0,-cos_e,sin_e);light=norm((-.6,-.8,1.2));pivot=(0,0,bottom)
 def render(gx,gy):
  su=(gx-gy)*15/(60*cos_e);sv=-(gx+gy)*15/(60*cos_e);normal=norm((-su,-sv,1));axis=(-normal[1],normal[0],0);sine=math.sqrt(dot(axis,axis))
  if sine>1e-12:axis=mul(axis,1/sine)
  def tilt(v):return v if sine<1e-12 else add(add(mul(v,normal[2]),mul(cross(axis,v),sine)),mul(axis,dot(axis,v)*(1-normal[2])))
  columns=[tilt(v) for v in ((1,0,0),(0,1,0),(0,0,1))];det=dot(columns[0],cross(columns[1],columns[2]));assert abs(det-1)<1e-6
  assert all(abs(dot(columns[i],columns[j])-(i==j))<1e-6 for i in range(3) for j in range(3))
  assert abs(dot(normal,columns[0]))<1e-6 and abs(dot(normal,columns[1]))<1e-6
  rgba=bytearray(W*H*4);depth=[-math.inf]*(W*H)
  def project(v):
   p=add(tilt(add(v,mul(pivot,-1))),pivot)
   return(p[0]*scale+AX,(-sin_e*p[1]-cos_e*p[2])*scale+AY,-cos_e*p[1]+sin_e*p[2])
  def edge(a,b,x,y):return(x-a[0])*(b[1]-a[1])-(y-a[1])*(b[0]-a[0])
  def tri(a,b,c,rgb):
   area=edge(a,b,*c[:2])
   if abs(area)<1e-8:return
   for y in range(max(0,math.floor(min(a[1],b[1],c[1]))),min(H-1,math.ceil(max(a[1],b[1],c[1])))+1):
    for x in range(max(0,math.floor(min(a[0],b[0],c[0]))),min(W-1,math.ceil(max(a[0],b[0],c[0])))+1):
     u=edge(b,c,x+.5,y+.5)/area;v=edge(c,a,x+.5,y+.5)/area;w=1-u-v
     if min(u,v,w)<-1e-5:continue
     z=u*a[2]+v*b[2]+w*c[2];i=y*W+x
     if z<=depth[i]:continue
     depth[i]=z;rgba[i*4:i*4+4]=bytes((*rgb,255))
  for vertices,n,index in parts:
   n=tilt(n)
   if dot(n,camera)<=0:continue
   shade=.38+.62*max(0,dot(n,light));rgb=tuple(max(0,min(255,int(palette[index*3+c]*shade+.5))) for c in range(3));a,b,c,d=map(project,vertices)
   tri(a,b,c,rgb);tri(a,c,d,rgb)
  return rgba,det
 def coverage(rgba):
  pts=[(i%W,i//W) for i in range(W*H) if rgba[i*4+3]]
  return {'pixels':len(pts),'height':max(y for x,y in pts)-min(y for x,y in pts)+1,'width':max(x for x,y in pts)-min(x for x,y in pts)+1}
 flat,_=render(0,0);cases=[('FLAT',0,0),('E',1,0),('W',-1,0),('S',0,1),('N',0,-1),('D+',1,1),('D-',-1,-1),('CROSS',1,-1)]
 gallery=bytearray([34,40,46,255])*(W*len(cases)*H*2);rows=[]
 for column,(name,gx,gy) in enumerate(cases):
  old=bytearray(W*H*4);by=1-(gx+gy)*.5;bx=-(gx-gy)*.25
  for y in range(H):
   for x in range(W):
    i=(y*W+x)*4
    if not flat[i+3]:continue
    yy=round(AY+(x-AX)*bx+(y-AY)*by)
    if 0<=yy<H:old[(yy*W+x)*4:(yy*W+x)*4+4]=flat[i:i+4]
  current,det=render(gx,gy);old_stats=coverage(old);new_stats=coverage(current)
  assert new_stats['height']>=4 and new_stats['pixels']>=32
  if (gx,gy)==(1,1):assert old_stats['height']==1 and new_stats['height']>old_stats['height']
  rows.append({'name':name,'gradient':[gx,gy],'oldAffineDeterminant':by,'rotationDeterminant':det,'old':old_stats,'trueVxl':new_stats})
  for row,tile in enumerate((old,current)):
   for y in range(H):
    for x in range(W):
     i=(y*W+x)*4
     if tile[i+3]:
      j=((row*H+y)*(W*len(cases))+column*W+x)*4;gallery[j:j+4]=tile[i:i+4]
 png(OUT/'voxel-pose-comparison.png',W*len(cases),H*2,gallery)
 report={'passed':True,'kind':'independent-original-vxl-projection-check','native':False,'model':'grizzly','modelSha256':entry['sha256'],'bodyFacing':4,'turretFacing':20,'rotation':'rigid minimal rotation to support normal; independent part yaw; shared triangle depth','galleryRows':['old whole-billboard affine approximation','actual original voxel faces rotated in 3D'],'cases':rows,'officialPositiveDiagonalAvailable':False,'note':'The (1,1) case is an explicit mathematical stress case, not an invented official map cell.'}
 (OUT/'pose-projection-checks.json').write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps({'passed':True,'cases':len(rows),'positiveDiagonal':rows[5],'png':str(OUT/'voxel-pose-comparison.png')}))

if __name__=='__main__':main()
