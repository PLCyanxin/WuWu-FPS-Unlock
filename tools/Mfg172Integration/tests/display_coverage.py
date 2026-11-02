"""Player-facing literal coverage; raw logs/configuration/protocols stay English."""
import argparse,json,re
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--prepared',type=Path,required=True);p.add_argument('--report',type=Path,required=True);a=p.parse_args()
token=r'"(?:\\.|[^"\\])*"'
generated=(a.prepared/'translations.generated.hpp').read_text(encoding='utf-8')
keys=set(re.findall(r'^\{"((?:\\.|[^"\\])*)",',generated,re.M))
technical={'DXGI','Vulkan','HDR','MFG','HDR10 PQ','HDR10 HLG','DLAA','DLSS-G','Streamline','MFG Unlock','0 MiB',' GiB',' MiB',' FPS','NVIDIA DLSS-G','Intel XeMFG'}
scalars={'active','cleared','disabled','enabled','motion-directional','motion-oriented diagonals','not observed','set','unverified','verified','viewport','waitable','unknown','yes','no','off','on','unavailable','unsupported','available','pending'}
lex=re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|'+token)
missing={};covered=set()
for name in ('addon_overlay.inl','panel_ui.hpp','dlss_preset.hpp','pacing_policy.hpp'):
 text=(a.prepared/name).read_text(encoding='utf-8');end=text.find('    report << "MFG Unlock Diagnostics')
 if end>=0:text=text[:end]
 # Preserve literals while masking comments, including quotes in comments.
 text=lex.sub(lambda m:m[0] if m[0].startswith('"') else re.sub('[^\n]',' ',m[0]),text)
 for mat in re.finditer(r'(?:'+token+r'\s*)+',text):
  raw=''.join(x[1:-1] for x in re.findall(token,mat[0]))
  chunks=raw.split('\\0') if '\\0' in raw else [raw]
  for value in chunks:
   value=value.split('##',1)[0]
   if value in technical or not re.search('[A-Za-z]{3}',value) or value.startswith(('##','mfgunlock:','http','../','./')):continue
   if '\\' in value.replace('\\n','') or value.startswith(('#','%','0x','static ','uniform ','[','GET ','NVSDK_','ImGui','DLSSG.','addon_overlay.inl is included')):continue
   if value not in scalars and ' ' not in value and (value[:1].islower() or '_' in value or re.search('[a-z][A-Z]',value)):continue
   if value not in keys:missing.setdefault(value,[]).append(f'{name}:{text[:mat.start()].count(chr(10))+1}')
   else:covered.add(value)
result={'coveredLiterals':len(covered),'untranslated':missing,'technicalNamesPreserved':sorted(technical),'scope':'visible labels, explanations, tooltips, presets and statuses in four display sources; config/log writers unchanged'}
a.report.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
if missing:raise SystemExit('FAIL: unmapped display literals: '+str(missing))
print('PASS:',len(covered),'complete display literals; no unmapped human text in audited UI sources')
