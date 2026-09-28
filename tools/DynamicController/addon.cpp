// SPDX-License-Identifier: MIT
// Independent companion. No imports of MFG Unlock's private state or source.
#define ImTextureID ImU64
#include <windows.h>
#include <deps/imgui/imgui.h>
#include <include/reshade.hpp>
#include "dynamiclive.hpp"
#include "native_cursor.hpp"
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
  config = ReadConfig(kSection, live::kLegacyConfigKey);
  if (!config.present) config = ReadConfig("RenoDX.MFGUnlock", live::kLegacyConfigKey);
  const int migrated=live::MigrateLegacy(config.count,config.first);
  if (config.present) reshade::set_config_value(nullptr, kSection, live::kConfigKey, migrated);
  config={true,1,std::to_string(migrated)};
 }
 live::requested.store(live::ParseConfig(config.count, config.first));
 auto fixed = ReadConfig(kSection, "FixedFrameGenerationEnabled");
 live::fixed_enabled.store(!(fixed.present && fixed.count==1 && fixed.first=="0"));
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
// -1 is the separate Off selection; 0 retains upstream game-decides semantics.
bool SaveFixedChoice(int selected,int* multiplier) {
 if(!multiplier||selected<0||selected>6)return false;
 const bool on=selected!=0;
 live::fixed_enabled.store(on);
 reshade::set_config_value(nullptr,kSection,"FixedFrameGenerationEnabled",on?1:0);
 if(!on)return false; // Preserve the upstream multiplier while disabled.
 *multiplier=selected==1?0:selected;
 return true;
}
bool DrawFixedMultiplier(int* multiplier) {
 if(!multiplier)return false;
 int selected=live::fixed_enabled.load() ? (*multiplier==0?1:*multiplier) : 0;
 if(!ImGui::Combo("##frame_multiplier", &selected, "关闭\0跟随游戏设置\0" "2x\0" "3x\0" "4x\0" "5x\0" "6x\0"))return false;
 const bool changed=SaveFixedChoice(selected,multiplier);
 if(!live::installed.load()){live::TryInstall(user_enabled,native_mode_guard);LogStatus();}
 return changed;
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
 int request = live::requested.load(), selected = request == live::kNativeBound ? 0 : (request == 0 ? 1 : request);
 if (table) { ImGui::TableNextRow(); ImGui::TableNextColumn(); }
 ImGui::TextUnformatted("Dynamic 最大倍率");
 if (table) { ImGui::TableNextColumn(); ImGui::SetNextItemWidth(-1.0f); }
 ImGui::BeginDisabled(!enabled);
 if (ImGui::Combo("##dynamic_max", &selected, "跟随原生上限\0" "关闭\0最高 2x\0最高 3x\0最高 4x\0最高 5x\0最高 6x\0")) {
  SaveRequest(selected == 0 ? live::kNativeBound : (selected == 1 ? 0 : selected));
 }
 ImGui::EndDisabled();
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
void OnOverlayCursor(reshade::api::effect_runtime* runtime) {
 // ReShade supplies a compatible IO snapshot. Never write through GetIO():
 // older ABI versions may receive a converted copy rather than the live IO.
 const auto cursor = ImGui::GetMouseCursor();
 using Shape = wuwa::native_cursor::Shape;
 Shape shape = Shape::Arrow;
 switch(cursor) {
  case ImGuiMouseCursor_TextInput: shape=Shape::Text; break;
  case ImGuiMouseCursor_ResizeAll: shape=Shape::ResizeAll; break;
  case ImGuiMouseCursor_ResizeNS: shape=Shape::ResizeNS; break;
  case ImGuiMouseCursor_ResizeEW: shape=Shape::ResizeEW; break;
  case ImGuiMouseCursor_ResizeNESW: shape=Shape::ResizeNESW; break;
  case ImGuiMouseCursor_ResizeNWSE: shape=Shape::ResizeNWSE; break;
  case ImGuiMouseCursor_Hand: shape=Shape::Hand; break;
  case ImGuiMouseCursor_NotAllowed: shape=Shape::NotAllowed; break;
 }
 const bool menuDrawsCursor=ImGui::GetIO().MouseDrawCursor;
 const bool wanted=menuDrawsCursor && cursor!=ImGuiMouseCursor_None;
 if(wuwa::native_cursor::Update(static_cast<HWND>(runtime->get_hwnd()),wanted,shape,menuDrawsCursor))
  ImGui::SetMouseCursor(ImGuiMouseCursor_None);
 // Report transitions outside the cursor lock/hooks, never once per frame.
 using CursorStatus=wuwa::cursor_hooks::Status;
 static std::atomic<CursorStatus> reported{CursorStatus::Idle};
 const auto status=wuwa::cursor_hooks::status.load();
 if(reported.exchange(status)!=status) {
  const char* message=nullptr;
  switch(status) {
   case CursorStatus::InstallFailed:message="Native cursor: atomic hook installation unavailable; no retry until restart.";break;
   case CursorStatus::AcquireFailed:message="Native cursor: bounded visibility acquisition failed.";break;
   case CursorStatus::DeltaLimit:message="Native cursor: display-count delta limit reached; game intent restored.";break;
   case CursorStatus::VisibilityLost:message="Native cursor: visibility changed outside managed APIs; ownership released.";break;
   case CursorStatus::WatchdogExpired:message="Native cursor: overlay heartbeat expired; game intent restored.";break;
   default:break;
  }
  if(message)reshade::log::message(reshade::log::level::warning,message);
 }
}
void OnDestroyRuntime(reshade::api::effect_runtime*) { wuwa::native_cursor::Stop(); }
}
extern "C" __declspec(dllexport) bool DrawWuWaFixedMultiplierV1(int* multiplier) { return DrawFixedMultiplier(multiplier); }
extern "C" __declspec(dllexport) void DrawWuWaDynamicMaximumInTableV1(reshade::api::effect_runtime*) { DrawControls(true); }
extern "C" __declspec(dllexport) constexpr const char* NAME = "WuWa Dynamic Maximum";
extern "C" __declspec(dllexport) constexpr const char* DESCRIPTION = "Version-checked native Dynamic frame maximum companion";
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID reserved) {
 if (reason == DLL_PROCESS_ATTACH) {
  if (!reshade::register_addon(module)) return FALSE;
  LoadConfig();
  reshade::register_overlay(kOverlayTitle, Overlay);
  reshade::register_event<reshade::addon_event::reshade_overlay>(OnOverlayCursor);
  reshade::register_event<reshade::addon_event::destroy_effect_runtime>(OnDestroyRuntime);
  reshade::register_event<reshade::addon_event::present>(OnPresent);
 } else if (reason == DLL_PROCESS_DETACH && !reserved) {
  // A successfully installed hook pins this module. Explicit unload is only
  // possible before installation; process termination needs no patch transaction.
  user_enabled.store(false);
  reshade::unregister_event<reshade::addon_event::reshade_overlay>(OnOverlayCursor);
  reshade::unregister_event<reshade::addon_event::destroy_effect_runtime>(OnDestroyRuntime);
  reshade::unregister_event<reshade::addon_event::present>(OnPresent);
  reshade::unregister_overlay(kOverlayTitle, Overlay);
  reshade::unregister_addon(module);
 }
 return TRUE;
}
