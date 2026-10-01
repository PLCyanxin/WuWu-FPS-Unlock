// SPDX-License-Identifier: MIT
// Versioned UI bridge; mode changes use the upstream public configuration controls.
#pragma once
extern "C" __declspec(dllexport) void WuWaDynamicMaximumUiBridgeV1() {}
inline void DrawDynamicMaximumCompanion(reshade::api::effect_runtime* runtime,bool mainDynamic) {
 HMODULE companion = nullptr;
 if (!GetModuleHandleExW(0, L"wuwa-dynamicmax.addon64", &companion)) return;
 using Draw = void (*)(reshade::api::effect_runtime*,bool);
 const auto draw = reinterpret_cast<Draw>(GetProcAddress(companion, "DrawWuWaDynamicMaximumInTableV2"));
 if (draw != nullptr) draw(runtime,mainDynamic);
 else {
  using LegacyDraw=void(*)(reshade::api::effect_runtime*);
  const auto legacy=reinterpret_cast<LegacyDraw>(GetProcAddress(companion,"DrawWuWaDynamicMaximumInTableV1"));
  if(legacy){ImGui::BeginDisabled(!mainDynamic);legacy(runtime);ImGui::EndDisabled();}
 }
 FreeLibrary(companion);
}

inline bool DrawFixedMultiplierCompanionImpl(int* multiplier) {
 HMODULE companion=nullptr;
 if(GetModuleHandleExW(0,L"wuwa-dynamicmax.addon64",&companion)) {
  using Draw=bool(*)(int*);
  const auto draw=reinterpret_cast<Draw>(GetProcAddress(companion,"DrawWuWaFixedMultiplierV1"));
  if(draw){const bool changed=draw(multiplier);FreeLibrary(companion);return changed;}
  FreeLibrary(companion);
 }
 int selected=*multiplier==0?0:*multiplier-1;
 const char* modes[]={"Game controlled","2x","3x","4x","5x","6x"};
 if(!wuwa_ui::Combo("##frame_multiplier",&selected,modes,6))return false;
 *multiplier=selected==0?0:selected+1;return true;
}

inline bool DrawFixedMultiplierCompanion(int* multiplier,bool dynamicEnabled) {
 ImGui::BeginDisabled(dynamicEnabled);
 const bool changed=DrawFixedMultiplierCompanionImpl(multiplier);
 ImGui::EndDisabled();
 return !dynamicEnabled && changed;
}

inline const char* CompanionFrameStatus() {
 HMODULE companion=nullptr;
 if(!GetModuleHandleExW(0,L"wuwa-dynamicmax.addon64",&companion))return "未捕获";
 using Read=unsigned(*)();
 const auto read=reinterpret_cast<Read>(GetProcAddress(companion,"GetWuWaNativeFrameStatusV1"));
 const unsigned status=read?read():99;FreeLibrary(companion);
 if(status==99)return "未捕获";
 if(status==0)return "未捕获";
 if(status==1)return "关闭";
 if(status==7)return "1x";
 static constexpr const char* labels[]={"","","2x","3x","4x","5x","6x"};
 return status<=6?labels[status]:"未捕获";
}

// Main and companion exchange a small mode value, never private runtime pointers.
inline int wuwa_frame_mode = 0;
inline int wuwa_last_fixed = 4;
inline bool CanApplyCompanionFrameOff() {
 HMODULE companion=nullptr;
 if(!GetModuleHandleExW(0,L"wuwa-dynamicmax.addon64",&companion))return false;
 using Check=bool(*)();
 const auto check=reinterpret_cast<Check>(GetProcAddress(companion,"CanApplyWuWaFrameOffV1"));
 const bool result=check&&check();FreeLibrary(companion);return result;
}
inline void ConfigureCompanionFrameMode(int mode, bool activate) {
 HMODULE companion=nullptr;
 if(!GetModuleHandleExW(0,L"wuwa-dynamicmax.addon64",&companion))return;
 using Configure=void(*)(int,bool);
 const auto configure=reinterpret_cast<Configure>(GetProcAddress(companion,"ConfigureWuWaFrameModeV1"));
 if(configure)configure(mode,activate);
 FreeLibrary(companion);
}
inline void DrawDynamicDerivedCompanion(reshade::api::effect_runtime* runtime) {
 HMODULE companion=nullptr;
 if(!GetModuleHandleExW(0,L"wuwa-dynamicmax.addon64",&companion))return;
 using Draw=void(*)(reshade::api::effect_runtime*);
 const auto draw=reinterpret_cast<Draw>(GetProcAddress(companion,"DrawWuWaDynamicMaximumInTableV3"));
 if(draw)draw(runtime);
 FreeLibrary(companion);
}
inline bool DrawFixedDerivedCompanion(int* multiplier) {
 HMODULE companion=nullptr;
 if(GetModuleHandleExW(0,L"wuwa-dynamicmax.addon64",&companion)) {
  using Draw=bool(*)(int*);
  const auto draw=reinterpret_cast<Draw>(GetProcAddress(companion,"DrawWuWaFixedMultiplierV2"));
  if(draw){const bool result=draw(multiplier);FreeLibrary(companion);return result;}
  FreeLibrary(companion);
 }
 int choice=(*multiplier>=2&&*multiplier<=6)?*multiplier-2:2;
 const char* labels[]={"2x","3x","4x","5x","6x"};
 if(!ImGui::Combo("##frame_multiplier",&choice,labels,5))return false;
 *multiplier=choice+2;return true;
}
