"""Recover complete DLSS-G CUDA tables from the pinned upstream 1.7.2 asset.
Static PE parsing only. Never load the addon or modify vendor instructions.
"""
import argparse, hashlib, json, struct, sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'MfgUiIntegration'))
from recover_kernel_tables import Image, occurrences

p=argparse.ArgumentParser();p.add_argument('--original',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--rebuilt',type=Path);a=p.parse_args()
expected='18a242225bdf27edb4776911414b94760289be07a186284b3a620d24ff8d7be6'
im=Image(a.original)
if hashlib.sha256(im.data).hexdigest()!=expected:raise ValueError('Upstream 1.7.2 asset identity mismatch')
rows=[]
for kind,start,count,stride,delta,fmt in [('blackwell',0x1db7e0,6,40,24,'<IIIII4xQQ'),('thin_geometry',0x241d10,13,48,32,'<IIIIQI4xQQ')]:
 for i in range(count):
  row=start+i*stride;v=struct.unpack_from(fmt,im.data,row);size,ptr,label=v[-3:]
  pos=im.offset(ptr);blob=im.data[pos:pos+size]
  if len(blob)!=size or not blob.startswith(b'\x7fELF'):raise ValueError('Missing or incomplete CUDA payload')
  for d in (delta,delta+8):
   if im.address(row+d)-im.base not in im.relocations:raise ValueError('Unrelocated table pointer')
  phoff=struct.unpack_from('<Q',blob,32)[0];phsize,phcount=struct.unpack_from('<HH',blob,54)
  if phoff+phsize*phcount!=size:raise ValueError('Incomplete ELF program table')
  rows.append(dict(kind=kind,index=i,fields=v[:-2],label=im.string(label),sha256=hashlib.sha256(blob).hexdigest(),blob=blob))
a.output.mkdir(parents=True,exist_ok=True)
for kind in ('blackwell','thin_geometry'):
 group=[r for r in rows if r['kind']==kind]
 typ,name=('CubinPatch','kCubinPatches') if kind=='blackwell' else ('CubinVariant','kThinGeometryCubins')
 lines=['// Byte-exact upstream 1.7.2 DLSS-G payloads. Private generated build input.','#pragma once']
 if kind=='blackwell':lines+=['struct CubinPatch { unsigned text,shared,regs,orig_size,size; const unsigned char* data; const char* role; };','static_assert(sizeof(CubinPatch)==40);','static constexpr const char kCubinsBuiltFor[]="160_E658700.bin, nvngx_dlssg.dll";']
 else:lines+=['struct CubinVariant { unsigned source_text,source_shared,source_regs,slot_size; unsigned long long source_fnv1a64; unsigned size; const unsigned char* data; const char* mechanism; };','static_assert(sizeof(CubinVariant)==48);']
 for r in group:
  lines.append('static const unsigned char original%d[]={ '%r['index']+','.join('0x%02x'%b for b in r['blob'])+' };')
 lines.append('static const '+typ+' '+name+'[]={')
 for r in group:
  f=r['fields'];values=[str(x)+'u' for x in f] if kind=='blackwell' else [str(x)+'u' for x in f[:4]]+[hex(f[4])+'ULL',str(f[5])+'u']
  lines.append('{'+','.join(values+['original'+str(r['index']),json.dumps(r['label'])])+'},')
 lines.append('};')
 for suffix in (['blackwell_cubins.generated.hpp'] if kind=='blackwell' else ['thin_geometry_cubins.generated.hpp','thin_geometry_stability.generated.hpp']):
  (a.output/suffix).write_text('\n'.join(lines)+'\n',encoding='utf-8')
if a.rebuilt:
 target=a.rebuilt.read_bytes()
 for r in rows:
  if target.count(r['blob'])!=1:raise ValueError('Rebuilt DLSS-G kernel missing or duplicated: '+r['label'])
report={'upstreamSha256':expected,'backend':'DLSS-G','completeTables':{'blackwell':6,'thin_geometry':13},'records':[{k:v for k,v in r.items() if k!='blob'} for r in rows],'rebuiltVerified':bool(a.rebuilt)}
(a.output/'kernel-receipt.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('PASS: complete 6+13 DLSS-G CUDA tables and metadata'+('; rebuilt bytes identical' if a.rebuilt else ''))
