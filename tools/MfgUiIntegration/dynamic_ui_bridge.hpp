// SPDX-License-Identifier: MIT
// UI-only bridge. No access to upstream private state or frame-generation logic.
#pragma once
extern "C" __declspec(dllexport) void WuWaDynamicMaximumUiBridgeV1() {}
inline void DrawDynamicMaximumCompanion(reshade::api::effect_runtime* runtime) {
 HMODULE companion = nullptr;
 if (!GetModuleHandleExW(0, L"wuwa-dynamicmax.addon64", &companion)) return;
 using Draw = void (*)(reshade::api::effect_runtime*);
 const auto draw = reinterpret_cast<Draw>(GetProcAddress(companion, "DrawWuWaDynamicMaximumInTableV1"));
 if (draw != nullptr) draw(runtime);
 FreeLibrary(companion);
}

inline bool DrawFixedMultiplierCompanion(int* multiplier) {
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
