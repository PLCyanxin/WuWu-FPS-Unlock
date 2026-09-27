// SPDX-License-Identifier: MIT
#pragma once
#include <string>
#include <string_view>
#include <vector>
#include <array>
#include <cctype>
#include <cstring>
#include <cstdint>
#include "translations.generated.hpp"
namespace wuwa_ui {
namespace detail {
inline constexpr size_t TranslationCount=std::size(kTranslations);
inline constexpr size_t TableSize=[] {size_t n=1;while(n<TranslationCount*2)n*=2;return n;}();
inline constexpr size_t Hash(std::string_view text) {
 size_t hash=14695981039346656037ull;
 for(unsigned char ch:text){hash^=ch;hash*=1099511628211ull;}
 return hash;
}
inline constexpr auto ExactIndex=[] {
 static_assert(TranslationCount<65535);
 std::array<std::uint16_t,TableSize> index{};
 for(size_t i=0;i<TranslationCount;++i){size_t slot=Hash(kTranslations[i].first)&(TableSize-1);while(index[slot])slot=(slot+1)&(TableSize-1);index[slot]=static_cast<std::uint16_t>(i+1);}
 return index;
}();
static_assert([] {
 for(const auto& pair:kTranslations)
  if(std::string_view(pair.first).size()>65535 || std::string_view(pair.second).size()>65535)return false;
 return true;
}());
struct Phrase {std::uint16_t keySize,valueSize;bool numeric;};
inline constexpr auto Phrases=[] {
 std::array<Phrase,TranslationCount> entries{};
 for(size_t i=0;i<TranslationCount;++i){
  std::string_view key=kTranslations[i].first;
  bool numeric=key=="x live" || key=="x max" || key=="% render scale";
  if(key.size()>=4 && (numeric || key.find('%')==key.npos) && key.find('#')==key.npos)
   entries[i]={static_cast<std::uint16_t>(key.size()),static_cast<std::uint16_t>(std::string_view(kTranslations[i].second).size()),numeric};
 }
 return entries;
}();
inline const char* Scratch(std::string_view value) {
 // Preserve the original 32-result lifetime for multiple printf arguments.
 static thread_local std::array<std::string,32> slots;
 static thread_local size_t next=0;
 auto& slot=slots[next++%slots.size()];slot.assign(value);return slot.c_str();
}
struct CachedPhrase {std::string input,output;bool occupied=false,changed=false;};
} // namespace detail
inline const char* Translate(const char* text) {
 if(!text)return text;
 const std::string_view input(text);
 const size_t hash=detail::Hash(input);
 size_t slot=hash&(detail::TableSize-1);
 while(detail::ExactIndex[slot]) {
  const auto& pair=kTranslations[detail::ExactIndex[slot]-1];
  if(input==pair.first)return pair.second;
  slot=(slot+1)&(detail::TableSize-1);
 }
 if(input.size()>4096)return text;
 // Short status caching has a fixed entry count and per-entry length limits.
 // Numeric values cannot create an ever-growing per-frame cache.
 static thread_local std::array<detail::CachedPhrase,16> cache;
 const bool cacheable=input.size()<=256;
 auto* entry=&cache[hash&(cache.size()-1)];
 if(cacheable)for(size_t probe=0;probe<4;++probe) {
  auto& candidate=cache[(hash+probe)&(cache.size()-1)];
  if(!candidate.occupied){entry=&candidate;break;}
  if(candidate.input==input)return candidate.changed?detail::Scratch(candidate.output):text;
 }
 auto& cached=*entry;
 std::string result(input);
 bool changed=false;
 const auto word=[](unsigned char ch) { return ch<128 && (std::isalnum(ch) || ch=='_'); };
 for(size_t i=0;i<detail::TranslationCount;++i) {
  const auto& phrase=detail::Phrases[i];
  if(!phrase.keySize)continue;
  const std::string_view key(kTranslations[i].first,phrase.keySize),value(kTranslations[i].second,phrase.valueSize);
  size_t at=0;
  while((at=result.find(key,at))!=std::string::npos) {
   const size_t end=at+key.size();
   const bool afterNumber=phrase.numeric && at && result[at-1]>='0' && result[at-1]<='9';
   if((at && word(key.front()) && word(result[at-1]) && !afterNumber) || (end<result.size() && word(key.back()) && word(result[end]))) {at=end;continue;}
   result.replace(at,key.size(),value);at+=value.size();changed=true;
  }
 }
 if(cacheable && result.size()<=1024) {
  cached.input.assign(input);cached.changed=changed;
  cached.output=changed?result:std::string{};cached.occupied=true;
 }
 return changed?detail::Scratch(result):text;
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
