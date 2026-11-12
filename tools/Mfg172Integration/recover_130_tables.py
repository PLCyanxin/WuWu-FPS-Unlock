"""Static recovery of every kernel table in the pinned upstream 1.3.0 asset.
No DLL execution. Unknown assets or incomplete tables fail closed.
"""
import argparse,hashlib,json,struct as s
from pathlib import Path
from recover_kernel_tables import Image,occurrences
SHA='7d742480613c58dc0eb698bf5dc76f2b32a6b4c607b2face5b87940494b9b229'
TABLES=[('blackwell',0x84340,6,40,24,'<IIIII4xQQ','CubinPatch','kCubinPatches'),('thin_geometry',0xe3c20,11,48,32,'<IIIIQI4xQQ','CubinVariant','kThinGeometryCubins')]
def records(path):
 im=Image(path);out=[]
 if hashlib.sha256(im.data).hexdigest()!=SHA:raise ValueError('Not the pinned original 1.3.0 asset')
 for kind,start,count,stride,delta,fmt,typ,name in TABLES:
  for i in range(count):
   row=start+i*stride;v=s.unpack_from(fmt,im.data,row);size,ptr,label=v[-3:];pos=im.offset(ptr);blob=im.data[pos:pos+size]
   assert blob[:4]==b'\x7fELF' and len(blob)==size
   phoff=s.unpack_from('<Q',blob,32)[0];phsize,phcount=s.unpack_from('<HH',blob,54)
   assert phoff+phsize*phcount==size
   for j in range(phcount):
    ph=phoff+j*phsize;assert s.unpack_from('<Q',blob,ph+8)[0]+s.unpack_from('<Q',blob,ph+32)[0]<=size
   for d in [delta,delta+8]:assert im.address(row+d)-im.base in im.relocations
   out.append(dict(kind=kind,index=i,fields=list(v[:-2]),label=im.string(label),pos=pos,sha256=hashlib.sha256(blob).hexdigest(),blob=blob))
 assert sorted(r['pos'] for r in out)==list(occurrences(im.data,b'\x7fELF'))
 return out
p=argparse.ArgumentParser();p.add_argument('--original',type=Path,required=True);p.add_argument('--output',type=Path);p.add_argument('--rebuilt',type=Path);p.add_argument('--report',type=Path);a=p.parse_args();rows=records(a.original)
if a.output:
 a.output.mkdir(parents=True,exist_ok=False)
 for kind,start,count,stride,delta,fmt,typ,name in TABLES:
  lines=['// Exact upstream 1.3.0 payloads. Local generated material.','#pragma once']
  if kind=='blackwell':lines+=['struct CubinPatch { unsigned text, shared, regs, orig_size, size; const unsigned char* data; const char* recovered_role; };','static_assert(sizeof(CubinPatch)==40);','static constexpr const char kCubinsBuiltFor[]="160_E658700.bin, nvngx_dlssg.dll";']
  else:lines+=['struct CubinVariant { unsigned source_text, source_shared, source_regs, slot_size; unsigned long long source_fnv1a64; unsigned size; const unsigned char* data; const char* mechanism; };','static_assert(sizeof(CubinVariant)==48);']
  selected=[r for r in rows if r['kind']==kind]
  for r in selected:
   lines.append(f"static const unsigned char recovered{r['index']}[]={{")
   blob=r['blob'];lines+=[' '+','.join(f'0x{x:02x}' for x in blob[i:i+24])+',' for i in range(0,len(blob),24)];lines.append('};')
  lines.append(f'static const {typ} {name}[]={{')
  for r in selected:
   fields=[str(v)+('ULL' if kind!='blackwell' and i==4 else 'u') for i,v in enumerate(r['fields'])]
   lines.append('{'+','.join(fields+[f"recovered{r['index']}",json.dumps(r['label'])])+'},')
  lines.append('};');(a.output/(kind+'_cubins.generated.hpp')).write_text('\n'.join(lines),encoding='utf-8')
if a.rebuilt:
 target=Image(a.rebuilt);seen=[]
 for kind,start,count,stride,delta,fmt,typ,name in TABLES:
  offsets=[]
  for r in [r for r in rows if r['kind']==kind]:
   matches=list(occurrences(target.data,r['blob']));assert len(matches)==1;pos=matches[0];seen.append(pos);candidates=[]
   for ref in occurrences(target.data,s.pack('<Q',target.address(pos))):
    q=ref-delta
    if q<0:continue
    v=s.unpack_from(fmt,target.data,q)
    try:
     if list(v[:-2])==r['fields'] and target.string(v[-1])==r['label'] and all(target.address(q+d)-target.base in target.relocations for d in [delta,delta+8]):candidates.append(q)
    except (ValueError,UnicodeError):pass
   assert len(candidates)==1,(kind,r['index'],'missing or ambiguous table row');offsets+=candidates
  assert offsets==[offsets[0]+i*stride for i in range(count)]
 assert sorted(seen)==list(occurrences(target.data,b'\x7fELF'))
report={'status':'PASS','originalSha256':SHA,'kernelCount':len(rows),'records':[{k:v for k,v in r.items() if k!='blob'} for r in rows]}
if a.rebuilt:report['rebuiltSha256']=hashlib.sha256(a.rebuilt.read_bytes()).hexdigest()
if a.report:a.report.write_text(json.dumps(report,indent=2),encoding='utf-8')
print('PASS: all 17 payloads, complete tables, pointer relocations and full ELF extents verified')
