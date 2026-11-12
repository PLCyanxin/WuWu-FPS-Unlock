"""Apply pinned UI/mode integration; never edit input source or kernel tables."""
import argparse, hashlib, json, re, shutil
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--generated',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
here=Path(__file__).resolve().parent
raw=(a.source/'addon.cpp').read_bytes()
expected=json.loads((here/'ui-scope.json').read_text(encoding='utf-8-sig'))['referenceFileSha256']
if hashlib.sha256(raw).hexdigest()!=expected: raise SystemExit('Upstream UI source differs from pinned upstream version; review the integration before building')
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
# Replace the redundant master/fixed/Dynamic selectors with one persistent mode.
start=text.index('    ImGui::TableNextRow();',text.index('if (ImGui::BeginTable("##frame_generation_settings"'))
end=text.index('    if (dynamic_mfg) {',start)
mode_ui=(here/'frame_mode_ui.inc').read_text(encoding='utf-8')
text=text[:start]+mode_ui+text[end:]
# Migrate existing values and apply the persisted mode before startup options.
anchor='  if (reshade::get_config_value(nullptr, kConfigSection, "DynamicTargetFPS", value)) {'
if text.count(anchor)!=1:raise SystemExit('Mode startup anchor is ambiguous')
text=text.replace(anchor,(here/'frame_mode_load.inc').read_text(encoding='utf-8')+anchor,1)
anchor='void OnRegisterOverlay(reshade::api::effect_runtime* runtime)'
if text.count(anchor)!=1:raise SystemExit('Mode application anchor is ambiguous')
text=text.replace(anchor,(here/'frame_mode_apply.inc').read_text(encoding='utf-8')+anchor,1)
# Replace only visible status rows. Diagnostic exports retain their upstream fields.
for label in ('MFG', 'Active FG multiplier'):
 anchor='StatusRow("'+label+'", multiplier_text.c_str(),'
 if text.count(anchor)!=1:raise SystemExit(label+' status anchor is ambiguous')
 text=text.replace(anchor,'StatusRow("'+label+'", CompanionFrameStatus(),',1)
# Remove local computations made obsolete by the two read-only status rows.
patterns=(
 r'    const bool dynamic_applied =\n        mfgunlock::framecount::g_dynamic_applied.load\(\n            std::memory_order_relaxed\);\n    const std::string multiplier_text =[\s\S]*?;\n(?=    const std::string sync_text)',
 r'      const unsigned int live_multiplier =\n          mfgunlock::framecount::g_latency_guard_live_multiplier.load\(\n              std::memory_order_relaxed\);\n',
 r'      const std::string multiplier_text =\n          live_multiplier < 2 \? "Not reported"\n                              : std::to_string\(live_multiplier\) \+ "x live";\n',
)
for pattern in patterns:
 text,count=re.subn(pattern,'',text)
 if count!=1:raise SystemExit('Obsolete visible status local anchor is ambiguous')
(a.output/'addon.cpp').write_text(text,encoding='utf-8')
print(f'Applied {len(mapping)} display mappings and one Dynamic UI bridge; input source unchanged')
