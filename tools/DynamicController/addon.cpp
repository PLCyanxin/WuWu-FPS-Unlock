// SPDX-License-Identifier: MIT
// Independent companion. No imports of MFG Unlock's private state or source.
#define ImTextureID ImU64
#include <windows.h>
#include <deps/imgui/imgui.h>
#include <include/reshade.hpp>
#include "dynamiclive.hpp"
namespace {
namespace live = mfgunlock::dynamiclive;
constexpr const char* kSection = "WuWa.DynamicMax";
constexpr const char* kOverlayTitle = "MFG Unlock";
std::atomic_bool user_enabled{true};
// Native callsite and snapshot mode, not a second UI toggle, decide Dynamic.
const std::atomic_bool native_mode_guard{true};
struct ConfigText { bool present; unsigned count; std::string first; };
ConfigText ReadConfig(const char* section, const char* key) {
 size_t size = 0;
 if (!reshade::get_config_value(nullptr, section, key, nullptr, &size)) return {};
 if (size == 0 || size > 128) return {true, 0, {}};
 std::string raw(size, '\0');
 if (!reshade::get_config_value(nullptr, section, key, raw.data(), &size)) return {true, 0, {}};
 ConfigText result{true, 0, {}};
 size_t offset = 0;
 while (offset < raw.size() && raw[offset]) {
  const auto end = raw.find('\0', offset);
  if (end == std::string::npos) return {true, 0, {}};
  if (++result.count == 1) result.first = raw.substr(offset, end - offset);
  offset = end + 1;
 }
 return result;
}
void LoadConfig() {
 auto config = ReadConfig(kSection, live::kConfigKey);
 if (!config.present) {
  config = ReadConfig("RenoDX.MFGUnlock", live::kConfigKey);
  if (config.present) reshade::set_config_value(nullptr, kSection, live::kConfigKey, live::ParseConfig(config.count, config.first));
 }
 live::requested.store(live::ParseConfig(config.count, config.first));
 auto enabled = ReadConfig(kSection, "Enabled");
 user_enabled.store(!enabled.present || (enabled.count == 1 && enabled.first == "1"));
}
std::atomic<const char*> logged_status{nullptr};
void LogStatus() {
 const char* current = live::invalid.load() ? "Dynamic maximum: native frame guard failed; companion disabled until restart" : live::status.load();
 if (logged_status.exchange(current) != current)
  reshade::log::message(live::invalid.load() || (live::auto_finished.load() && !live::installed.load())
      ? reshade::log::level::warning : reshade::log::level::info, current);
}
void OnPresent(reshade::api::command_queue*, reshade::api::swapchain*, const reshade::api::rect*,
 const reshade::api::rect*, uint32_t, const reshade::api::rect*) {
 live::AutoInstall(user_enabled, native_mode_guard);
 LogStatus();
}
void SaveRequest(int request) {
 request = live::NormalizeConfig(request);
 live::requested.store(request);
 reshade::set_config_value(nullptr, kSection, live::kConfigKey, request);
}
void DrawControls(bool table) {
 ImGui::PushID(kSection);
 if (table) { ImGui::TableNextRow(); ImGui::TableNextColumn(); ImGui::TextUnformatted("启用 Dynamic 最大倍率限制"); ImGui::TableNextColumn(); }
 else ImGui::Separator();
 bool enabled = user_enabled.load();
 if (ImGui::Checkbox(table ? "##enabled" : "启用 Dynamic 最大倍率限制", &enabled)) {
  user_enabled.store(enabled);
  reshade::set_config_value(nullptr, kSection, "Enabled", enabled ? 1 : 0);
 }
 if (!live::installed.load() && ImGui::Button("重试")) { live::TryInstall(user_enabled, native_mode_guard); LogStatus(); }
 int request = live::requested.load(), selected = request == 0 ? 0 : request - 1;
 if (table) { ImGui::TableNextRow(); ImGui::TableNextColumn(); }
 ImGui::TextUnformatted("Dynamic 最大倍率");
 if (table) { ImGui::TableNextColumn(); ImGui::SetNextItemWidth(-1.0f); }
 if (ImGui::Combo("##dynamic_max", &selected, "恢复原生上限\0最高 2x\0最高 3x\0最高 4x\0最高 5x\0最高 6x\0")) {
  SaveRequest(selected == 0 ? 0 : selected + 1);
 }
 ImGui::PopID();
}
void Overlay(reshade::api::effect_runtime*) {
 HMODULE host = nullptr;
 if (GetModuleHandleExW(0, L"renodx-mfgunlock.addon64", &host)) {
  const bool integrated = GetProcAddress(host, "WuWaDynamicMaximumUiBridgeV1") != nullptr;
  FreeLibrary(host);
  if (integrated) return; // The host draws these controls beside native Dynamic.
 }
 DrawControls(false);
}
}
extern "C" __declspec(dllexport) void DrawWuWaDynamicMaximumInTableV1(reshade::api::effect_runtime*) { DrawControls(true); }
extern "C" __declspec(dllexport) constexpr const char* NAME = "WuWa Dynamic Maximum";
extern "C" __declspec(dllexport) constexpr const char* DESCRIPTION = "Version-checked native Dynamic frame maximum companion";
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID reserved) {
 if (reason == DLL_PROCESS_ATTACH) {
  if (!reshade::register_addon(module)) return FALSE;
  LoadConfig();
  reshade::register_overlay(kOverlayTitle, Overlay);
  reshade::register_event<reshade::addon_event::present>(OnPresent);
 } else if (reason == DLL_PROCESS_DETACH && !reserved) {
  // A successfully installed hook pins this module. Explicit unload is only
  // possible before installation; process termination needs no patch transaction.
  user_enabled.store(false);
  reshade::unregister_event<reshade::addon_event::present>(OnPresent);
  reshade::unregister_overlay(kOverlayTitle, Overlay);
  reshade::unregister_addon(module);
 }
 return TRUE;
}
