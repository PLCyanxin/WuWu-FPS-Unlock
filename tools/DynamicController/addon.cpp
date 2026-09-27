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
void Overlay(reshade::api::effect_runtime*) {
 bool enabled = user_enabled.load();
 if (ImGui::Checkbox("Enable Dynamic maximum", &enabled)) {
  user_enabled.store(enabled);
  reshade::set_config_value(nullptr, kSection, "Enabled", enabled ? 1 : 0);
 }
 if (!live::installed.load() && ImGui::Button("Retry")) { live::TryInstall(user_enabled, native_mode_guard); LogStatus(); }
 int request = live::requested.load(), selected = request == 0 ? 0 : request - 1;
 ImGui::TextUnformatted("Dynamic maximum multiplier");
 if (ImGui::Combo("##dynamic_max", &selected, "Native bound (restore)\0Up to 2x\0Up to 3x\0Up to 4x\0Up to 5x\0Up to 6x\0")) {
  SaveRequest(selected == 0 ? 0 : selected + 1);
 }
}
}
extern "C" __declspec(dllexport) constexpr const char* NAME = "WuWa Dynamic Maximum";
extern "C" __declspec(dllexport) constexpr const char* DESCRIPTION = "Version-checked native Dynamic frame maximum companion";
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID reserved) {
 if (reason == DLL_PROCESS_ATTACH) {
  if (!reshade::register_addon(module)) return FALSE;
  LoadConfig();
  reshade::register_overlay("WuWa Dynamic Maximum", Overlay);
  reshade::register_event<reshade::addon_event::present>(OnPresent);
 } else if (reason == DLL_PROCESS_DETACH && !reserved) {
  // A successfully installed hook pins this module. Explicit unload is only
  // possible before installation; process termination needs no patch transaction.
  user_enabled.store(false);
  reshade::unregister_event<reshade::addon_event::present>(OnPresent);
  reshade::unregister_overlay("WuWa Dynamic Maximum", Overlay);
  reshade::unregister_addon(module);
 }
 return TRUE;
}
