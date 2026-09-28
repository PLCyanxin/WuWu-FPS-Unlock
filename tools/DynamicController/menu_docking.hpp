// SPDX-License-Identifier: MIT
#pragma once
#include <cstring>
#include <deps/imgui/imgui.h>

namespace wuwa::menu_docking {
// Call on the overlay thread while a native ImGui context is current. Never
// touch a compatibility-copy IO structure from a different ReShade ImGui ABI.
class Policy {
    bool warned_ = false;
public:
    template<class GetIo, class Log>
    bool Apply(const char* version, GetIo&& get_io, Log&& log) {
        if (!version || std::strcmp(version, IMGUI_VERSION) != 0) {
            if (!warned_) {
                warned_ = true;
                log("Shift-to-dock unavailable: native ImGui version differs; docking behavior unchanged.");
            }
            return false;
        }
        get_io().ConfigDockingWithShift = true;
        return true;
    }
};
template<class Log> inline bool Configure(Log&& log) {
    static thread_local Policy policy;
    return policy.Apply(ImGui::GetVersion(), []() -> ImGuiIO& { return ImGui::GetIO(); }, log);
}
}
