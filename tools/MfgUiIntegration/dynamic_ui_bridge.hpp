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
