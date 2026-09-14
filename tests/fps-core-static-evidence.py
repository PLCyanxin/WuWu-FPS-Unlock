"""Static-only evidence checks for the exact user core. Never loads/executes a DLL."""
import hashlib, struct, sys
from pathlib import Path
p = Path(sys.argv[1]) if len(sys.argv)>1 else Path('components/ww_plugin_base.dll')
b = p.read_bytes()
assert hashlib.sha256(b).hexdigest() == '844d7552692f53e8a1bfe45edf360a094597c5bf2b26dc058ff59b21d2250c3a'
pe = struct.unpack_from('<I', b, 0x3c)[0]
n, opt = struct.unpack_from('<H', b, pe+6)[0], struct.unpack_from('<H', b, pe+20)[0]
sections = [struct.unpack_from('<IIII', b, pe+24+opt+i*40+8) for i in range(n)]
def at(rva, size):
    for virtual_size, va, raw_size, raw in sections:
        if va <= rva and rva+size <= va+raw_size:
            return b[raw+rva-va:raw+rva-va+size]
    raise AssertionError('RVA unavailable')
checks = {
 'six-field parser skips all five advanced values': (0x64e0, b'%*d,%*d,%*f,%*d,%*d,%d\0'),
 'pipe receiver stores FPS in persistent global RVA90C4': (0x1d26, bytes.fromhex('4c 8d 05 97 73 00 00')),
 'receiver loops back to blocking ReadFile': (0x1d4c, bytes.fromhex('75 a2')),
 'independent writer sleeps 51 ms': (0x21b5, bytes.fromhex('b9 33 00 00 00 ff 15 60 3e 00 00')),
 'writer reloads persistent FPS global': (0x21c0, bytes.fromhex('66 0f 6e 0d fc 6e 00 00')),
 'successful write loops without pipe-read dependency': (0x21ce, bytes.fromhex('e8 2d fc ff ff 85 c0 75 de')),
}
for name,(rva,expected) in checks.items():
    assert at(rva,len(expected)) == expected, name
    print('PASS static:', name)
print('6/6 static byte evidence checks passed; NOT a DLL runtime or game test.')
