// SPDX-License-Identifier: MIT
// Independent companion. No imports of MFG Unlock's private state or source.
#define ImTextureID ImU64
#include <windows.h>
#include <deps/imgui/imgui.h>
#include <include/reshade.hpp>
#include "dynamiclive.hpp"
#include "native_cursor.hpp"
#include "menu_docking.hpp"
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
std::atomic<int> diagnostic_request{-2};
std::atomic<unsigned long long> diagnostic_after{0};
void LogFrameObservation() {
 const int pending=diagnostic_request.load();
 if(pending==-2||live::observed_frames.load(std::memory_order_acquire)<=diagnostic_after.load())return;
 const auto sample=live::observation.load();
 const int observed=(sample&255)==7?live::kNativeBound:static_cast<int>(sample&255);
 if(!(sample>>63)||observed!=pending)return;
 int expected=pending;
 if(!diagnostic_request.compare_exchange_strong(expected,-2))return;
 const std::string message="Dynamic 原生帧已读取请求：选择="+std::to_string(pending)+
   "，原生上限="+std::to_string((sample>>8)&255)+"，原生帧快照总帧数="+std::to_string((sample>>24)&255)+
   "。这是原生帧快照观测，不代表显示器实际输出；提高上限不强制调度器提高倍率。";
 reshade::log::message(reshade::log::level::info,message.c_str());
}
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
 LogFrameObservation();
}
// Explicit user actions may retry a bounded startup miss; never clear a guard failure.
void RetryForUserChoice(bool (*install)(const std::atomic_bool&,const std::atomic_bool&)=live::TryInstall) {
 if(!live::installed.load()&&!live::invalid.load()&&(user_enabled.load()||!live::fixed_enabled.load())) {
  install(user_enabled,native_mode_guard);
  LogStatus();
 }
}
void SaveRequest(int request) {
 request = live::NormalizeConfig(request);
 live::UiChoiceChanged();
 live::requested.store(request);
 live::UiChoiceCommitted();
 diagnostic_after.store(live::observed_frames.load());
 diagnostic_request.store(request);
 reshade::log::message(reshade::log::level::info,"Dynamic 选择已保存，等待后续原生 Dynamic 帧读取；游戏暂停提交或当前不是 Dynamic 模式时不会立即改变输出。");
 reshade::set_config_value(nullptr, kSection, live::kConfigKey, request);
 RetryForUserChoice();
}
// -1 is the separate Off selection; 0 retains upstream game-decides semantics.
bool SaveFixedChoice(int selected,int* multiplier) {
 if(!multiplier||selected<0||selected>6)return false;
 const bool on=selected!=0;
 live::UiChoiceChanged();
 live::fixed_enabled.store(on);
 live::UiChoiceCommitted();
 reshade::set_config_value(nullptr,kSection,"FixedFrameGenerationEnabled",on?1:0);
 if(!on)return false; // Preserve the upstream multiplier while disabled.
 *multiplier=selected==1?0:selected;
 reshade::log::message(reshade::log::level::info,"普通倍率已保存；倍率值沿原插件等待游戏下一次启用的 SetOptions 调用，尚不能认定已生效。普通关闭/恢复由独立逐帧路径处理。");
 return true;
}
bool DrawFixedMultiplier(int* multiplier) {
 if(!multiplier)return false;
 int selected=live::fixed_enabled.load() ? (*multiplier==0?1:*multiplier) : 0;
 if(!ImGui::Combo("##frame_multiplier", &selected, "关闭\0跟随游戏设置\0" "2x\0" "3x\0" "4x\0" "5x\0" "6x\0"))return false;
 const bool changed=SaveFixedChoice(selected,multiplier);
 RetryForUserChoice();
 return changed;
}
void DrawControls(bool table,bool mainDynamic=true) {
 ImGui::BeginDisabled(!mainDynamic);
 ImGui::PushID(kSection);
 if (table) { ImGui::TableNextRow(); ImGui::TableNextColumn(); ImGui::TextUnformatted("启用 Dynamic 最大倍率限制"); ImGui::TableNextColumn(); }
 else ImGui::Separator();
 bool enabled = user_enabled.load();
 if (ImGui::Checkbox(table ? "##enabled" : "启用 Dynamic 最大倍率限制", &enabled)) {
  live::UiChoiceChanged();
  user_enabled.store(enabled);
  live::UiChoiceCommitted();
  reshade::set_config_value(nullptr, kSection, "Enabled", enabled ? 1 : 0);
  if(enabled)RetryForUserChoice();
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
 ImGui::EndDisabled();
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
 wuwa::menu_docking::Configure([](const char* message) {
  reshade::log::message(reshade::log::level::warning,message);
 });
 // Cursor reads also support ReShade's converted IO snapshot.
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
extern "C" __declspec(dllexport) unsigned GetWuWaNativeFrameStatusV1() { return live::enabled.load()&&!live::invalid.load()?live::UiFrameStatus():0; }
extern "C" __declspec(dllexport) void DrawWuWaDynamicMaximumInTableV2(reshade::api::effect_runtime*,bool mainDynamic) { DrawControls(true,mainDynamic); }
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
