"""Recover exact upstream 1.1.5 cubin tables; statically verify rebuilt tables.

No addon is loaded or executed. Requires the user's exact original binary and
provider plus upstream's read-only fatbin parser. Generated payloads stay local.
The original Blackwell table has no source hash: no hash is invented for it.
"""
import argparse
import hashlib
import json
import pathlib
import struct as s
import sys

# Validation uses assertions; refuse optimized Python that could remove them.
if not __debug__:
    raise RuntimeError("Run without -O: validation must remain enabled")


def recover(root, output, original):
    import pathlib,struct as s,hashlib,importlib.util,json
    ROOT=pathlib.Path(root).resolve()
    OUT=pathlib.Path(output).resolve()
    OUT.mkdir(parents=True, exist_ok=True)
    b=pathlib.Path(original).read_bytes()
    assert hashlib.sha256(b).hexdigest()=='0d04d858a62d3d19e7e3d478c0b8c46fe3ac43ec9fd11e4abb15617bd291d71a'
    p=s.unpack_from('<I',b,60)[0];base=s.unpack_from('<Q',b,p+48)[0];sec=p+24+s.unpack_from('<H',b,p+20)[0]
    sections=[s.unpack_from('<IIII',b,sec+40*i+8) for i in range(s.unpack_from('<H',b,p+6)[0])]
    def off(v):
     return next(r+v-base-a for _,a,z,r in sections if a<=v-base<a+z)
    def string(v):
     q=off(v);return b[q:b.index(0,q)].decode('ascii')
    spec=importlib.util.spec_from_file_location('variants',ROOT/'upstream/companion-origin/tools/build_thin_geometry_variants.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
    provider=ROOT/'payload/files/game/nvngx_dlssg.dll';pb,ps=m.pe_sections(provider); originals=[]
    for start,end in m.iter_fatbins(pb,ps):
     for kind,arch,pos,size,compressed,raw in m.iter_entries(pb,start,end):
      if kind==2 and arch==89:
       data=pb[pos:pos+size]
       if compressed:data=m.lz4_decompress(data,raw)
       originals.append(dict(offset=pos,size=len(data),fingerprint=m.fingerprint_elf(data),fnv=f'{m.fnv1a64(data):016x}'))
    records=[]
    for kind,start,count,stride in [('blackwell',0x756c0,6,40),('thin',0xb2890,7,48)]:
     for i in range(count):
      q=start+i*stride
      if kind=='blackwell':
       t,sh,r,slot,size,ptr,label=s.unpack_from('<IIIII4xQQ',b,q);fnv=None
      else:
       t,sh,r,slot,fnv,size,ptr,label=s.unpack_from('<IIIIQI4xQQ',b,q)
      pos=off(ptr); data=b[pos:pos+size];assert data[:4]==b'\x7fELF'; assert size<=slot
      elfend=s.unpack_from('<Q',data,40)[0]+s.unpack_from('<H',data,58)[0]*s.unpack_from('<H',data,60)[0];assert elfend<=size
      matches=[x for x in originals if x['size']==slot and x['fingerprint']==(t,sh,r) and (fnv is None or x['fnv']==f'{fnv:016x}')]
      record=dict(kind=kind,index=i,tableOffset=hex(q),dataOffset=hex(pos),size=size,elfEnd=elfend,sourceFingerprint=[t,sh,r],slotSize=slot,sourceFnv=None if fnv is None else f'{fnv:016x}',label=string(label),sha256=hashlib.sha256(data).hexdigest(),providerMatches=matches)
      records.append(record);(OUT/f'{kind}-{i}.cubin').write_bytes(data)
    (OUT/'records.json').write_text(json.dumps(dict(addonSha256=hashlib.sha256(b).hexdigest(),providerSha256=hashlib.sha256(pb).hexdigest(),originals=originals,records=records),indent=2),encoding='utf-8')

    # Validate both loops' compiled start/end/stride before trusting table boundaries.
    for at,expected in [(0x119d4,'488d05e5520600'),(0x119e6,'488d0dc3530600'),(0x11a22,'4883c028'),(0x18881,'488d1d08b60900'),(0x1889c,'4c8d253db70900'),(0x1890c,'4883c330')]:
     assert b[off(base+at):off(base+at)+len(bytes.fromhex(expected))]==bytes.fromhex(expected)
    # Prove every embedded ELF is represented exactly once, and the complete program
    # header table (after section headers in NVIDIA cubins) is inside the copied bytes.
    elf_offsets=[i for i in range(len(b)-4) if b[i:i+4]==b'\x7fELF']
    assert sorted(int(x['dataOffset'],16) for x in records)==elf_offsets
    for rec in records:
     data=(OUT/f"{rec['kind']}-{rec['index']}.cubin").read_bytes()
     phoff=s.unpack_from('<Q',data,32)[0];phsize,phcount=s.unpack_from('<HH',data,54)
     assert phoff+phsize*phcount==rec['size']
     for i in range(phcount):
      ph=phoff+i*phsize;po,filesz=s.unpack_from('<Q',data,ph+8)[0],s.unpack_from('<Q',data,ph+32)[0]
      assert po+filesz<=len(data)
     assert hashlib.sha256(data).hexdigest()==rec['sha256']
    assert all(r['providerMatches'] for r in records if r['kind']=='thin')
    # Pointer fields must have genuine PE DIR64 relocation records.
    rrva,rsize=s.unpack_from('<II',b,p+24+112+5*8);q=off(base+rrva);end=q+rsize;relocs=set()
    while q<end:
     page,sz=s.unpack_from('<II',b,q);assert sz>=8
     for at in range(q+8,q+sz,2):
      e=s.unpack_from('<H',b,at)[0]
      if e>>12==10:relocs.add(page+(e&4095))
     q+=sz
    for rec in records:
     start=int(rec['tableOffset'],16);ptrOff=24 if rec['kind']=='blackwell' else 32
     for delta in [ptrOff,ptrOff+8]:
      rva=next(a+start+delta-r for _,a,z,r in sections if r<=start<r+z)
      assert rva in relocs
    for kind,filename,typename,tablename in [('blackwell','blackwell_cubins.generated.hpp','CubinPatch','kCubinPatches'),('thin','thin_geometry_cubins.generated.hpp','CubinVariant','kThinGeometryCubins')]:
     rows=[r for r in records if r['kind']==kind]
     lines=['// Exact byte recovery from upstream 1.1.5 original addon SHA256 '+hashlib.sha256(b).hexdigest(), '// Local generated payload: do not commit. No CUDA/PTX instructions modified.', '#pragma once']
     if kind=='blackwell':
      lines+=['struct CubinPatch { unsigned text, shared, regs, orig_size, size; const unsigned char* data; const char* recovered_role; };','static_assert(sizeof(CubinPatch) == 40);','static constexpr const char kCubinsBuiltFor[] = "160_E658700.bin, nvngx_dlssg.dll";']
     else:
      lines+=['struct CubinVariant { unsigned source_text, source_shared, source_regs, slot_size; unsigned long long source_fnv1a64; unsigned size; const unsigned char* data; const char* mechanism; };','static_assert(sizeof(CubinVariant) == 48);']
     for r in rows:
      data=(OUT/f"{kind}-{r['index']}.cubin").read_bytes();lines.append(f"static const unsigned char recoveredCubin{r['index']}[] = {{")
      for i in range(0,len(data),24):lines.append('  '+','.join(f'0x{x:02x}' for x in data[i:i+24])+',')
      lines.append('};')
     lines.append(f'static const {typename} {tablename}[] = {{')
     for r in rows:
      fields=[str(x)+'u' for x in r['sourceFingerprint']+[r['slotSize']]]
      if kind=='thin':fields.append('0x'+r['sourceFnv']+'ULL')
      fields += [str(r['size'])+'u',f"recoveredCubin{r['index']}",json.dumps(r['label'])]
      lines.append('  {'+', '.join(fields)+'},')
     lines.append('};')
     (OUT/filename).write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print('VALIDATED: 13/13 original cubins; complete tables 6+7; 7/7 stored source hashes verified; original pointer relocations; both compiled iteration bounds; all program headers preserved.')

class Image:
    def __init__(self, path):
        self.data = pathlib.Path(path).read_bytes()
        b = self.data
        assert b[:2] == b'MZ'
        p = s.unpack_from('<I', b, 60)[0]
        assert b[p:p+4] == b'PE\0\0' and s.unpack_from('<H', b, p+4)[0] == 0x8664
        self.base = s.unpack_from('<Q', b, p+48)[0]
        first = p+24+s.unpack_from('<H', b, p+20)[0]
        self.sections = [s.unpack_from('<IIII', b, first+40*i+8)
                         for i in range(s.unpack_from('<H', b, p+6)[0])]
        rva, size = s.unpack_from('<II', b, p+24+112+5*8)
        q = self.offset(self.base+rva)
        end = q+size
        self.relocations = set()
        while q < end:
            page, length = s.unpack_from('<II', b, q)
            assert length >= 8 and q+length <= end
            for at in range(q+8, q+length, 2):
                entry = s.unpack_from('<H', b, at)[0]
                if entry >> 12 == 10:
                    self.relocations.add(page+(entry & 4095))
            q += length

    def offset(self, va):
        for _, start, length, raw in self.sections:
            if start <= va-self.base < start+length:
                return raw+va-self.base-start
        raise ValueError('VA outside file-backed PE sections')

    def address(self, offset):
        for _, start, length, raw in self.sections:
            if raw <= offset < raw+length:
                return self.base+start+offset-raw
        raise ValueError('File offset outside PE sections')

    def string(self, va):
        start = self.offset(va)
        return self.data[start:self.data.index(0, start)].decode('ascii')


def occurrences(data, needle):
    at = 0
    while True:
        at = data.find(needle, at)
        if at < 0:
            return
        yield at
        at += 1


def verify(original, rebuilt):
    """Verify bytes and both ordered tables without assuming rebuilt RVAs."""
    source, target = Image(original), Image(rebuilt)
    assert hashlib.sha256(source.data).hexdigest() == '0d04d858a62d3d19e7e3d478c0b8c46fe3ac43ec9fd11e4abb15617bd291d71a'
    results, seen = [], []
    for kind, start, count, stride, ptr_offset, fmt in [
        ('blackwell', 0x756c0, 6, 40, 24, '<IIIII4xQQ'),
        ('thin', 0xb2890, 7, 48, 32, '<IIIIQI4xQQ'),
    ]:
        rows = []
        for index in range(count):
            expected = s.unpack_from(fmt, source.data, start+index*stride)
            size, pointer, label = expected[-3:]
            pos = source.offset(pointer)
            blob = source.data[pos:pos+size]
            matches = list(occurrences(target.data, blob))
            assert len(matches) == 1, f'{kind}[{index}]: cubin missing/duplicated'
            blob_at = matches[0]
            seen.append(blob_at)
            candidates = []
            for ref in occurrences(target.data, s.pack('<Q', target.address(blob_at))):
                row = ref-ptr_offset
                if row < 0 or row+stride > len(target.data):
                    continue
                values = s.unpack_from(fmt, target.data, row)
                if values[:-2] != expected[:-2]:
                    continue
                try:
                    if target.string(values[-1]) != source.string(label):
                        continue
                    if any(target.address(row+d)-target.base not in target.relocations
                           for d in (ptr_offset, ptr_offset+8)):
                        continue
                except (ValueError, UnicodeError):
                    continue
                candidates.append(row)
            assert len(candidates) == 1, f'{kind}[{index}]: exact table row missing/ambiguous'
            rows.append(candidates[0])
            results.append(dict(kind=kind, index=index, size=size,
                                sha256=hashlib.sha256(blob).hexdigest(),
                                tableOffset=hex(candidates[0]), dataOffset=hex(blob_at),
                                label=source.string(label), fields=list(expected[:-2])))
        assert rows == [rows[0]+i*stride for i in range(count)], f'{kind}: table order/stride changed'
    assert sorted(seen) == list(occurrences(target.data, b'\x7fELF')), 'Unaccounted ELF blob'
    assert b'160_E658700.bin, nvngx_dlssg.dll\0' in target.data
    return dict(status='PASS', originalSha256=hashlib.sha256(source.data).hexdigest(),
                rebuiltSha256=hashlib.sha256(target.data).hexdigest(), records=results,
                limitation='Static table and byte equivalence only; no GPU/game execution.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    recovery = commands.add_parser('recover')
    recovery.add_argument('--root', required=True, type=pathlib.Path)
    recovery.add_argument('--original', required=True, type=pathlib.Path)
    recovery.add_argument('--output', required=True, type=pathlib.Path)
    verification = commands.add_parser('verify')
    verification.add_argument('--original', required=True, type=pathlib.Path)
    verification.add_argument('--rebuilt', required=True, type=pathlib.Path)
    verification.add_argument('--report', type=pathlib.Path)
    args = parser.parse_args()
    if args.command == 'recover':
        recover(args.root, args.output, args.original)
    else:
        report = json.dumps(verify(args.original, args.rebuilt), indent=2)
        if args.report:
            args.report.write_text(report+'\n', encoding='utf-8')
        print(report)


if __name__ == '__main__':
    main()
