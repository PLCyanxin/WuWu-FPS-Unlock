"""Read-only RT_MANIFEST parser: never loads or executes the target PE."""
import struct, pathlib, json, hashlib, xml.etree.ElementTree as ET
p=pathlib.Path(r'E:\yanxin_ws\wuwa-fps-unlock\components\unlocker\unlock.exe')
b=p.read_bytes()
u16=lambda o:struct.unpack_from('<H',b,o)[0]
u32=lambda o:struct.unpack_from('<I',b,o)[0]
pe=u32(0x3c)
assert b[pe:pe+4]==b'PE\0\0'
coff=pe+4;opt=coff+20
assert u16(opt)==0x20b
resource_rva=u32(opt+112+2*8)
sections=[]
for i in range(u16(coff+2)):
 o=opt+u16(coff+16)+40*i
 sections.append((b[o:o+8].rstrip(b'\0').decode(),u32(o+12),u32(o+8),u32(o+20),u32(o+16)))
def offset(rva):
 for name,va,vs,raw,rs in sections:
  if va<=rva<va+max(vs,rs): return raw+rva-va
 raise ValueError('RVA outside sections')
base=offset(resource_rva)
def label(value):
 if value&0x80000000:
  o=base+(value&0x7fffffff)
  return b[o+2:o+2+2*u16(o)].decode('utf-16-le')
 return value
found=[]
def walk(rel,path):
 o=base+rel
 for i in range(u16(o+12)+u16(o+14)):
  e=o+16+i*8; name=label(u32(e)); child=u32(e+4); new=path+[name]
  if child&0x80000000: walk(child&0x7fffffff,new)
  elif new[0]==24:
   de=base+child;rva=u32(de);size=u32(de+4);fo=offset(rva);raw=b[fo:fo+size]
   text=raw.decode('utf-8-sig').rstrip('\0')
   root=ET.fromstring(text)
   levels=[x.attrib for x in root.iter() if x.tag.split('}')[-1]=='requestedExecutionLevel']
   found.append(dict(resource_path=new,rva=rva,file_offset=fo,size=size,sha256=hashlib.sha256(raw).hexdigest(),active_requestedExecutionLevel=levels))
   pathlib.Path('knowledge/launch/unlocker-manifest.xml').write_text(text,encoding='utf-8')
walk(0,[])
print(json.dumps(dict(path=str(p),sha256=hashlib.sha256(b).hexdigest(),pe_header_offset=pe,resource_rva=resource_rva,sections=sections,manifests=found),indent=2))
