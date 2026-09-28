"""Apply display-only integration to the pinned upstream source; never edit input."""
import argparse, hashlib, json, re, shutil
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--generated',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
here=Path(__file__).resolve().parent
raw=(a.source/'addon.cpp').read_bytes()
expected=json.loads((here/'ui-scope.json').read_text(encoding='utf-8-sig'))['referenceFileSha256']
if hashlib.sha256(raw).hexdigest()!=expected: raise SystemExit('Upstream UI source differs from pinned 1.1.5; review the integration before building')
if a.output.exists(): raise SystemExit('Use a fresh output source directory')
shutil.copytree(a.source,a.output)
for name in ('blackwell_cubins.generated.hpp','thin_geometry_cubins.generated.hpp'):
 if not (a.generated/name).is_file(): raise SystemExit('Full upstream kernel tables required: '+name)
 shutil.copy2(a.generated/name,a.output/name)
for name in ('ui_zh.hpp','dynamic_ui_bridge.hpp'):shutil.copy2(here/name,a.output/name)
mapping=json.loads((here/'zh-CN.json').read_text(encoding='utf-8-sig'))
text=raw.decode('utf-8-sig')
# Keys are raw C++ literal contents. Adjacent literals form one visible string.
token=r'"(?:\\.|[^"\\])*"'
for match in re.finditer(r'(?:'+token+r'\s*){2,}',text):
 parts=re.findall(token,match.group()); keys=[part[1:-1] for part in parts]
 if any(key in mapping for key in keys):mapping[''.join(keys)]=''.join(mapping.get(key,key) for key in keys)
rows=[]
for key,value in sorted(mapping.items(),key=lambda item:len(item[0]),reverse=True):
 # Title ID suffixes belong only to widgets, not ordinary status text.
 value=value.split('###')[0]
 rows.append('{"'+key+'", "'+value.replace('"','\\"')+'"},')
(a.output/'translations.generated.hpp').write_text('#pragma once\nstatic constexpr std::pair<const char*,const char*> kTranslations[]={\n'+'\n'.join(rows)+'\n};\n',encoding='utf-8')
functions=re.findall(r'(?:void|bool)\s+(\w+)\(', (here/'ui_zh.hpp').read_text(encoding='utf-8'))
for name in functions:text=text.replace('ImGui::'+name+'(', 'wuwa_ui::'+name+'(')
anchor='namespace {'
text=text.replace(anchor,'#include "ui_zh.hpp"\n#include "dynamic_ui_bridge.hpp"\n\n'+anchor,1)
anchor='if (block_dynamic_enable) ImGui::EndDisabled();'
if text.count(anchor)!=1:raise SystemExit('Dynamic insertion anchor is ambiguous')
text=text.replace(anchor,anchor+'\n    DrawDynamicMaximumCompanion(runtime);',1)
anchor='if (wuwa_ui::Combo("##frame_multiplier", &force_choice,\n                     kMultiplierModes,\n                     static_cast<int>(std::size(kMultiplierModes)))) {\n      force = force_choice == 0 ? 0 : force_choice + 1;'
if text.count(anchor)!=1:raise SystemExit('Active fixed multiplier insertion anchor is ambiguous')
text=text.replace(anchor,'if (DrawFixedMultiplierCompanion(&force)) {',1)
(a.output/'addon.cpp').write_text(text,encoding='utf-8')
print(f'Applied {len(mapping)} display mappings and one Dynamic UI bridge; input source unchanged')
