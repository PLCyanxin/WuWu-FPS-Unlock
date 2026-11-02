// SPDX-License-Identifier: MIT
// Enumerate already-loaded Streamline modules without loading or patching them.
// NVIDIA NGX OTA can map sl.dlss_g under a hashed filename (e.g. 190_*.dll).
// Discovery never authorizes a private Hook: SHA256 and code anchors are mandatory.
#pragma once
#include <windows.h>
#include <tlhelp32.h>
#include <algorithm>
#include <cwctype>
#include <string>
#include <string_view>

namespace mfgunlock::dynamiclive::discovery {
inline std::wstring Normalize(std::wstring_view value) {
  std::wstring out(value);
  for (wchar_t& c : out) {
    if (c == L'/') c = L'\\';
    c = static_cast<wchar_t>(std::towlower(c));
  }
  return out;
}

inline bool CandidatePath(std::wstring_view path) {
  if (path.empty()) return false;
  const auto lower = Normalize(path);
  const size_t slash = lower.find_last_of(L'\\');
  const std::wstring_view base(lower.data() + (slash == std::wstring::npos ? 0 : slash + 1),
                               lower.size() - (slash == std::wstring::npos ? 0 : slash + 1));
  if (base == L"sl.dlss_g.dll") return true;
  // Only use this directory as a *search hint*. Path/name alone cannot authorize
  // a detour; the older private ABI still requires full SHA and instruction anchors.
  // The NVIDIA App can load either the normal or the override 2.14.16 NGX
  // wrapper. Both are ONLY search hints, not permission to hook. Full file
  // SHA256, all 28 native code anchors and all 3 target pointers remain gates.
  const bool ngx_model =
      lower.find(L"\\nvidia\\ngx\\models\\sl_dlss_g_0\\versions\\") != std::wstring::npos ||
      lower.find(L"\\nvidia\\ngx\\models\\sl_dlss_g_override_0\\versions\\") != std::wstring::npos;
  const bool in_files = lower.find(L"\\files\\", lower.find(L"\\versions\\")) !=
                        std::wstring::npos;
  return ngx_model && in_files && base.size() > 4 && base.ends_with(L".dll");
}

inline HMODULE AcquireLoaded(bool* alias = nullptr, bool* ambiguous = nullptr) {
  if (alias) *alias = false;
  if (ambiguous) *ambiguous = false;
  // Enumerate ordinary and OTA modules together to prevent a stale direct
  // module from shadowing the active versioned NGX Streamline wrapper.
  const HANDLE snapshot = CreateToolhelp32Snapshot(
      TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, GetCurrentProcessId());
  if (snapshot == INVALID_HANDLE_VALUE) return nullptr;
  MODULEENTRY32W entry{};
  entry.dwSize = sizeof(entry);
  HMODULE found = nullptr;
  bool found_alias = false;
  if (Module32FirstW(snapshot, &entry)) {
    do {
      if (!CandidatePath(entry.szExePath)) continue;
      HMODULE candidate = nullptr;
      if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(entry.modBaseAddr), &candidate))
        continue;
      if (!found) {
        found = candidate;
        const auto full = Normalize(entry.szExePath);
        const size_t separator = full.find_last_of(L'\\');
        found_alias = full.substr(separator == std::wstring::npos ? 0 : separator + 1)
                      != L"sl.dlss_g.dll";
      } else if (candidate != found) {
        // No confirmed API owner among multiple wrappers. Fail closed.
        FreeLibrary(candidate);
        FreeLibrary(found);
        CloseHandle(snapshot);
        if (ambiguous) *ambiguous = true;
        return nullptr;
      } else {
        FreeLibrary(candidate);
      }
    } while (Module32NextW(snapshot, &entry));
  }
  CloseHandle(snapshot);
  if (alias) *alias = found && found_alias;
  return found;
}} // namespace mfgunlock::dynamiclive::discovery
