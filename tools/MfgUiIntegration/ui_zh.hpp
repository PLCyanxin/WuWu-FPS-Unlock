// SPDX-License-Identifier: MIT
#pragma once
#include <string>
#include <string_view>
#include <vector>
#include <array>
#include <cctype>
#include <cstring>
#include "translations.generated.hpp"
namespace wuwa_ui {
inline const char* Translate(const char* text) {
 if (!text) return text;
 for (const auto& pair : kTranslations) if (std::string_view(text) == pair.first) return pair.second;
 // Display-only composite statuses, e.g. "Preset C" or "4x live / 6x max".
 // Bounded scratch slots keep several printf arguments alive without an
 // ever-growing cache of per-frame FPS/latency values.
 if (strlen(text) > 4096) return text;
 std::string result(text);
 bool changed=false;
 const auto word=[](unsigned char ch) { return ch<128 && (std::isalnum(ch) || ch=='_'); };
 for (const auto& pair : kTranslations) {
  const std::string_view key(pair.first);
  const bool numericSuffix = key=="x live" || key=="x max" || key=="% render scale";
  if (key.size()<4 || (!numericSuffix && key.find('%')!=key.npos) || key.find('#')!=key.npos) continue;
  size_t at=0;
  while ((at=result.find(key,at))!=std::string::npos) {
   const size_t end=at+key.size();
   const bool afterNumber = numericSuffix && at && result[at-1]>='0' && result[at-1]<='9';
   if ((at && word(key.front()) && word(result[at-1]) && !afterNumber) || (end<result.size() && word(key.back()) && word(result[end]))) { at=end; continue; }
   result.replace(at,key.size(),pair.second); at+=strlen(pair.second); changed=true;
  }
 }
 if(changed) { static thread_local std::array<std::string,32> slots; static thread_local size_t next=0; auto& slot=slots[next++%slots.size()]; slot=std::move(result); return slot.c_str(); }
 return text;
}
template<class T> T Arg(T value) { return value; }
inline const char* Arg(const char* value) { return Translate(value); }
inline const char* Arg(char* value) { return Translate(value); }
inline std::string Label(const char* text) {
 const char* translated = Translate(text);
 if (translated == text || std::string_view(translated).find("###") != std::string_view::npos) return translated;
 return std::string(translated) + "###" + text;
}
template<class... A> void Text(const char* fmt, A... args) { ImGui::Text(Translate(fmt), Arg(args)...); }
template<class... A> void TextDisabled(const char* fmt, A... args) { ImGui::TextDisabled(Translate(fmt), Arg(args)...); }
template<class... A> void TextWrapped(const char* fmt, A... args) { ImGui::TextWrapped(Translate(fmt), Arg(args)...); }
template<class... A> void TextColored(const ImVec4& color, const char* fmt, A... args) { ImGui::TextColored(color, Translate(fmt), Arg(args)...); }
inline void TextUnformatted(const char* text, const char* end = nullptr) {
 if (end) { std::string value(text, end); ImGui::TextUnformatted(Translate(value.c_str())); }
 else ImGui::TextUnformatted(Translate(text));
}
inline bool Button(const char* label, const ImVec2& size = ImVec2(0,0)) { return ImGui::Button(Label(label).c_str(), size); }
inline bool Checkbox(const char* label, bool* value) { return ImGui::Checkbox(Label(label).c_str(), value); }
inline bool BeginTabItem(const char* label, bool* open = nullptr, ImGuiTabItemFlags flags = 0) { return ImGui::BeginTabItem(Label(label).c_str(), open, flags); }
inline bool CollapsingHeader(const char* label, ImGuiTreeNodeFlags flags = 0) { return ImGui::CollapsingHeader(Label(label).c_str(), flags); }
inline bool TreeNode(const char* label) { return ImGui::TreeNode(Label(label).c_str()); }
template<class... A> bool TreeNode(const char* id, const char* fmt, A... args) { return ImGui::TreeNode(id, Translate(fmt), Arg(args)...); }
inline bool BeginPopupModal(const char* name, bool* open = nullptr, ImGuiWindowFlags flags = 0) { return ImGui::BeginPopupModal(Label(name).c_str(), open, flags); }
inline void OpenPopup(const char* name, ImGuiPopupFlags flags = 0) { ImGui::OpenPopup(Label(name).c_str(), flags); }
inline bool IsPopupOpen(const char* name, ImGuiPopupFlags flags = 0) { return ImGui::IsPopupOpen(Label(name).c_str(), flags); }
inline void TableSetupColumn(const char* label, ImGuiTableColumnFlags flags = 0, float width = 0, ImGuiID id = 0) { ImGui::TableSetupColumn(Translate(label), flags, width, id); }
inline bool InputInt(const char* label, int* value, int step=1, int fast=100, ImGuiInputTextFlags flags=0) { return ImGui::InputInt(Label(label).c_str(),value,step,fast,flags); }
inline bool SliderInt(const char* label, int* value, int min, int max, const char* format="%d", ImGuiSliderFlags flags=0) { return ImGui::SliderInt(Label(label).c_str(),value,min,max,Translate(format),flags); }
inline bool Combo(const char* label, int* current, const char* const items[], int count, int height=-1) {
 std::vector<const char*> translated; for(int i=0;i<count;++i) translated.push_back(Translate(items[i]));
 return ImGui::Combo(Label(label).c_str(),current,translated.data(),count,height);
}
inline bool Combo(const char* label, int* current, const char* items, int height=-1) {
 std::string translated; for(const char* p=items;*p;p+=strlen(p)+1) { translated+=Translate(p); translated+='\0'; } translated+='\0';
 return ImGui::Combo(Label(label).c_str(),current,translated.c_str(),height);
}
}
