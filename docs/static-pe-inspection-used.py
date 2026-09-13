# Optional audit helper. Does not execute the input binary.
import argparse
from pathlib import Path
import struct,re,hashlib,json
parser=argparse.ArgumentParser()
parser.add_argument('exe',type=Path)
parser.add_argument('--output',type=Path,default=Path('pe-audit-output'))
a=parser.parse_args();a.output.mkdir(parents=True,exist_ok=True)
b=a.exe.read_bytes()
found=[]
for m in re.finditer(b'MZ', b):
 s=m.start()
 try:
  lf=struct.unpack_from('<I',b,s+0x3c)[0]
  if lf>4096 or lf<64:continue
  p=s+lf
  if b[p:p+4]!=b'PE\0\0':continue
  machine,ns=struct.unpack_from('<HH',b,p+4);ops=struct.unpack_from('<H',b,p+20)[0]
  if ns>32 or ops not in (224,240):continue
  sh=p+24+ops;end=0
  for i in range(ns):
   rawsize,rawptr=struct.unpack_from('<II',b,sh+40*i+16);end=max(end,rawsize+rawptr)
  if s+end>len(b):continue
  dat=b[s:s+end];pdb=re.findall(rb'[^\x00\r\n]{0,160}\.pdb',dat)
  found.append({'offset':s,'length':end,'machine':hex(machine),'pdb':[x.decode('ascii','replace') for x in pdb],'sha256':hashlib.sha256(dat).hexdigest()})
  if any(b'ww_plugin_base.pdb' in x for x in pdb):
   (a.output/'ww_plugin_base.original.dll').write_bytes(dat)
 except (struct.error,ValueError):pass
print(json.dumps(found,indent=2,ensure_ascii=False))
(a.output/'pe_scan.json').write_text(json.dumps(found,indent=2,ensure_ascii=False))
