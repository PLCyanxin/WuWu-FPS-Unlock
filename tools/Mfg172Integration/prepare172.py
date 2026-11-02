"""Pinned 1.7.2 DLSS-G integration. Inputs remain immutable; patches fail closed."""
import argparse, hashlib, json, re, shutil
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--generated',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();here=Path(__file__).resolve().parent
if a.output.exists():raise ValueError('Prepared output must be fresh')
pin=json.loads((here/'upstream172.json').read_text())
for name,digest in pin['sources'].items():
 if hashlib.sha256((a.source/name).read_bytes()).hexdigest()!=digest:raise ValueError('Source identity changed: '+name)
shutil.copytree(a.source,a.output)
for n in ('blackwell_cubins.generated.hpp','thin_geometry_cubins.generated.hpp','thin_geometry_stability.generated.hpp'):
 shutil.copy2(a.generated/n,a.output/n)
for n in ('ui_zh.hpp','dynamic_ui_bridge.hpp','capability_epoch.hpp'):shutil.copy2(here/n,a.output/n)
def replace(name,old,new):
 f=a.output/name;s=f.read_text(encoding='utf-8-sig')
 if s.count(old)!=1:raise ValueError(f'{name}: ambiguous patch {old[:60]}')
 f.write_text(s.replace(old,new),encoding='utf-8')
replace('runtime_version.hpp','return Is(packed, 310, 9, 1) || Is(packed, 310, 9, 16);','return packed >= Pack(310, 9, 1, 0);')
replace('runtime_version.hpp','return Is(packed, 2, 14, 1) || Is(packed, 2, 14, 16);','return packed >= Pack(2, 14, 1, 0);')
replace('framecount.hpp','#include "./runtime_version.hpp"','#include "./runtime_version.hpp"\n#include "capability_epoch.hpp"')
replace('framecount.hpp','inline std::atomic<unsigned int> g_dynamic_target_fps{0};','inline std::atomic<unsigned int> g_dynamic_target_fps{160};')
replace('framecount.hpp','inline std::atomic_bool g_streamline_function_owner_seen{false};','''inline std::atomic_bool g_streamline_function_owner_seen{false};
inline std::atomic<std::uintptr_t> wuwa_streamline_identity{0},wuwa_provider_identity{0};
inline std::atomic<uint64_t> wuwa_capability_epoch{1},wuwa_capability_sample{0};
inline std::array<wuwa::capability::Binding,64> wuwa_state_bindings{};
inline std::atomic<unsigned> wuwa_state_binding_count{0};
inline std::atomic<const wuwa::capability::Binding*> wuwa_state_binding{nullptr};
inline void WuWaBindGetState(const void* function) {
 HMODULE owner=nullptr;
 if(!function || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(function),&owner)) {
  wuwa_state_binding.store(nullptr,std::memory_order_release);return;
 }
 const auto f=reinterpret_cast<std::uintptr_t>(function),o=reinterpret_cast<std::uintptr_t>(owner);
 if(wuwa::capability::Matches(wuwa_state_binding.load(std::memory_order_acquire),f,o))return;
 unsigned slot=wuwa_state_binding_count.load(std::memory_order_relaxed);
 while(slot<wuwa_state_bindings.size() && !wuwa_state_binding_count.compare_exchange_weak(slot,slot+1,std::memory_order_acq_rel)){}
 if(slot>=wuwa_state_bindings.size()){wuwa_state_binding.store(nullptr,std::memory_order_release);return;}
 wuwa_state_bindings[slot]={f,o};
 wuwa_state_binding.store(&wuwa_state_bindings[slot],std::memory_order_release);
}
inline void WuWaRuntimeChanged(std::atomic<std::uintptr_t>& identity,HMODULE module) {
 if(identity.exchange(reinterpret_cast<std::uintptr_t>(module),std::memory_order_acq_rel)==reinterpret_cast<std::uintptr_t>(module))return;
 wuwa_capability_epoch.fetch_add(1,std::memory_order_acq_rel);
 wuwa_capability_sample.store(0,std::memory_order_release);
 g_dynamic_support_seen.store(false,std::memory_order_release);
 g_dynamic_supported.store(false,std::memory_order_release);
 g_dynamic_state_probe_failures.store(0,std::memory_order_release);
}''')
replace('framecount.hpp','''  if (supported || !g_dlssg_dynamic_version_seen.load(std::memory_order_acquire)) {
    g_last_dlssg_version.store(packed, std::memory_order_release);
  }''','''  WuWaRuntimeChanged(wuwa_provider_identity,module);
  g_last_dlssg_version.store(packed, std::memory_order_release);
  g_dlssg_dynamic_version_seen.store(supported,std::memory_order_release);''')
replace('framecount.hpp','''  const uint64_t previous = g_active_streamline_version.exchange(
      packed, std::memory_order_acq_rel);''','''  WuWaRuntimeChanged(wuwa_streamline_identity,module);
  const uint64_t previous = g_active_streamline_version.exchange(
      packed, std::memory_order_acq_rel);''')
replace('framecount.hpp','''         g_dlssg_dynamic_version_seen.load(std::memory_order_acquire);
}''','''         g_dlssg_dynamic_version_seen.load(std::memory_order_acquire) &&
         wuwa::capability::Valid(wuwa_capability_sample.load(std::memory_order_acquire),
                                 wuwa_capability_epoch.load(std::memory_order_acquire));
}''')
replace('framecount.hpp','''  const size_t caller_version = state.structVersion;''','''  const uint64_t wuwa_observed_epoch=wuwa_capability_epoch.load(std::memory_order_acquire);
  const auto* wuwa_observed_binding=wuwa_state_binding.load(std::memory_order_acquire);
  const size_t caller_version = state.structVersion;''')
replace('framecount.hpp','''    if (observed_state->structVersion >= sl::kStructVersion4 &&''','''    if (wuwa_observed_epoch==wuwa_capability_epoch.load(std::memory_order_acquire) &&
        wuwa::capability::Matches(wuwa_observed_binding,reinterpret_cast<std::uintptr_t>(real),wuwa_streamline_identity.load(std::memory_order_acquire)) &&
        observed_state->structVersion >= sl::kStructVersion4 &&''')
replace('framecount.hpp','''      g_real_get_state.store(reinterpret_cast<GetStateFn>(function),''','''      WuWaBindGetState(function);
      g_real_get_state.store(reinterpret_cast<GetStateFn>(function),''')
replace('framecount.hpp','''      const bool previous =
          g_dynamic_supported.exchange(supported, std::memory_order_acq_rel);''','''      wuwa_capability_sample.store(wuwa::capability::Encode(wuwa_observed_epoch,supported),std::memory_order_release);
      const bool previous =
          g_dynamic_supported.exchange(supported, std::memory_order_acq_rel);''')
replace('framecount.hpp','''      std::wcsstr(path.data(), L"sl.interposer") != nullptr;''','''      std::wcsstr(path.data(), L"\\\\models\\\\sl_dlss_g_override_0\\\\") != nullptr ||
      std::wcsstr(path.data(), L"sl.interposer") != nullptr;''')
replace('addon.cpp','#include "./framecount.hpp"','#include "ui_zh.hpp"\n#include "./framecount.hpp"\n#include "dynamic_ui_bridge.hpp"')
replace('panel_ui.hpp','#include <deps/imgui/imgui.h>','#include <deps/imgui/imgui.h>\n#include "ui_zh.hpp"')
replace('panel_ui.hpp','''  const float pad=(width-ImGui::CalcTextSize(label).x)*.5f-ImGui::GetStyle().FramePadding.x;''','''  const char* visible=wuwa_ui::Translate(label);
  const float pad=(width-ImGui::CalcTextSize(visible).x)*.5f-ImGui::GetStyle().FramePadding.x;''')
replace('panel_ui.hpp','''  text.append(label).append("###").append(label);''','''  text.append(visible).append("###").append(label);''')
replace('panel_ui.hpp','''inline void Centered(const char* text,ImVec4 color) {''','''inline void Centered(const char* text,ImVec4 color) {
  text=wuwa_ui::Translate(text);''')
replace('panel_ui.hpp','''inline void Hero(const char* multiplier,const char* backend,const char* renderer,const char* state,bool active) {''','''inline void Hero(const char* multiplier,const char* backend,const char* renderer,const char* state,bool active) {
  state=wuwa_ui::Translate(state);''')
replace('panel_ui.hpp','''ImGui::CalcTextSize("GitHub repository").x''','''ImGui::CalcTextSize(wuwa_ui::Translate("GitHub repository")).x''')
replace('addon.cpp','Release 1.4.5 - Engineering & Reliability; Local Stable; V2 Compatibility; in-game validation pending','MFG Unlock 1.7.2 - WuWa Chinese UI, persistent modes and live maximum; DLSS-G backend')
replace('addon.cpp','  if (reshade::get_config_value(nullptr, kConfigSection, "DynamicTargetFPS", value)) {',(here/'frame_mode_load.inc').read_text(encoding='utf-8')+'\n  if (reshade::get_config_value(nullptr, kConfigSection, "DynamicTargetFPS", value)) {')
replace('addon_overlay.inl','void OnRegisterOverlay(reshade::api::effect_runtime* runtime)',(here/'frame_mode_apply.inc').read_text(encoding='utf-8')+'\nvoid OnRegisterOverlay(reshade::api::effect_runtime* runtime)')
f=a.output/'addon_overlay.inl';s=f.read_text(encoding='utf-8')
old='''  const std::string multiplier_text=dynamic_applied?DynamicMultiplierText(driver_multiplier,dynamic_max_generated):
      effective_seen?std::to_string(effective_multiplier)+"x":game_request_seen?std::to_string(game_multiplier)+"x":"Waiting";'''
if s.count(old)!=1:raise ValueError('Actual multiplier display anchor changed')
s=s.replace(old,'''  const char* wuwa_native_status=CompanionFrameStatus();
  const std::string multiplier_text=std::strcmp(wuwa_native_status,"未捕获")!=0 ? wuwa_native_status :
      (sleep_status_available && driver_multiplier>=1 && driver_multiplier<=6 ? std::to_string(driver_multiplier)+"x" : "未捕获");''')
s=s.replace('StatusRow("Active FG multiplier", multiplier_text.c_str(),','StatusRow("Active FG multiplier", CompanionFrameStatus(),')
start=s.index('    ImGui::TableNextRow();',s.index('if (ImGui::BeginTable("##frame_generation_settings"'))
end=s.index('    if (dynamic_mfg) {',start)
s=s[:start]+(here/'frame_mode_ui.inc').read_text(encoding='utf-8')+s[end:]
s=s.replace('DLSS-G 310.9.1/310.9.16 and Streamline 2.14.1/2.14.16','DLSS-G >= 310.9.1 and Streamline >= 2.14.1')
f.write_text(s,encoding='utf-8')
# Keep the actual interface owner in diagnostic output when slInit is rejected.
replace('framecount.hpp','"mfgunlock: runtime selection was not applied because sl::Preferences uses an unknown ABI; preserving the game\'s slInit request."','"mfgunlock: runtime selection not applied: unsupported sl::Preferences ABI; preserving slInit. This is not a minimum-runtime-version rejection."')
mapping=json.loads((here/'zh-CN.json').read_text(encoding='utf-8-sig'))
extra=here/'zh-172.json'
if extra.exists():mapping.update(json.loads(extra.read_text(encoding='utf-8')))
extra=here/'zh-complete.json'
if extra.exists():mapping.update(json.loads(extra.read_text(encoding='utf-8')))
for key,value in list(mapping.items()):
 if '##' in key and not key.startswith('##'):
  visible=key.split('##',1)[0]
  mapping.setdefault(visible,value.split('##',1)[0])
token=r'"(?:\\.|[^"\\])*"'
for f in a.output.iterdir():
 if f.suffix not in ('.cpp','.inl','.hpp') or f.name in ('ui_zh.hpp','dynamic_ui_bridge.hpp') or '.generated.' in f.name:continue
 text=f.read_text(encoding='utf-8-sig')
 for match in re.finditer(r'(?:'+token+r'\s*){2,}',text):
  parts=re.findall(token,match.group());keys=[x[1:-1] for x in parts]
  joined=''.join(keys)
  if joined not in mapping and all(x in mapping or not re.search('[A-Za-z]',x) for x in keys):
   mapping[joined]=''.join(mapping.get(x,x) for x in keys)
 # Translate UI calls, preserving configuration keys, log strings and widget IDs.
 functions=re.findall(r'(?:void|bool)\s+(\w+)\(', (here/'ui_zh.hpp').read_text(encoding='utf-8'))
 for name in functions:text=text.replace('ImGui::'+name+'(', 'wuwa_ui::'+name+'(')
 f.write_text(text,encoding='utf-8')
rows=[]
specifier=re.compile(r'%(?:[-+ #0]*)(?:\d+|\*)?(?:\.(?:\d+|\*))?(?:ll|l|z|I64|h|L)?[diuoxXfFeEgGaAcsp%]')
for k,v in sorted(mapping.items(),key=lambda x:len(x[0]),reverse=True):
 if specifier.findall(k)!=specifier.findall(v):raise ValueError('Translation format contract mismatch: '+k[:100])
 rows.append('{"'+k+'", "'+v.split('###')[0].replace('"','\\"')+'"},')
(a.output/'translations.generated.hpp').write_text('#pragma once\nstatic constexpr std::pair<const char*,const char*> kTranslations[]={\n'+'\n'.join(rows)+'\n};\n',encoding='utf-8')
print(f'Prepared upstream 1.7.2: four modes, persistent companion bridge, minimum runtime API policy, {len(mapping)} translation entries')
