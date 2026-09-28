// SPDX-License-Identifier: MIT
// Derived from 30launchers/WutheringWaves-FPS-unlocker, see SOURCE.json.
#pragma once
#include <atomic>
#include <charconv>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <span>
#include <string_view>
#include <vector>
namespace wuwa::fps {
inline constexpr int kDefaultFps=150, kMinFps=30, kMaxFps=420;
inline constexpr unsigned kCheckMs=51;
inline constexpr char kPipe[]=R"(\\.\pipe\55984705-F24C-45C2-B2B7-27F047B43A56)";
inline bool ParseMessage(std::string_view message,int& fps) {
 if(message.empty()||message.size()>128)return false;
 std::string_view fields[6];size_t start=0;
 for(int i=0;i<6;++i){auto end=message.find(',',start);if((i<5)==(end==message.npos))return false;
 fields[i]=message.substr(start,end==message.npos?message.size()-start:end-start);start=end+1;}
 int values[6]{};
 for(int i:{0,1,3,4,5}){auto s=fields[i];auto [end,error]=std::from_chars(s.data(),s.data()+s.size(),values[i]);if(error!=std::errc{}||end!=s.data()+s.size())return false;}
 float ignored;auto s=fields[2];auto [end,error]=std::from_chars(s.data(),s.data()+s.size(),ignored);
 if(error!=std::errc{}||end!=s.data()+s.size()||!std::isfinite(ignored)||values[5]<kMinFps||values[5]>kMaxFps)return false;
 fps=values[5];return true; // Other fields remain ignored, as in the original FPS-only core.
}
// Interleaved bytes, like upstream's parsed pattern: never materialize the
// complete raw signature inside our DLL, where an all-process scan could find it.
struct PatternByte{uint8_t value;bool wildcard;};
inline std::vector<PatternByte> ParsePattern(std::string_view text) {
 std::vector<PatternByte> pattern;
 while(!text.empty()) {auto end=text.find(' ');auto part=text.substr(0,end);
 if(part=="?"||part=="??")pattern.push_back({0,true});else{unsigned value;auto [p,e]=std::from_chars(part.data(),part.data()+part.size(),value,16);if(part.size()!=2||e!=std::errc{}||p!=part.data()+part.size()||value>255)return {};pattern.push_back({static_cast<uint8_t>(value),false});}
 if(end==text.npos)break;text.remove_prefix(end+1);}
 return pattern;
}
inline const uint8_t* FindFirst(const uint8_t* bytes,size_t size,std::span<const PatternByte> pattern,size_t* compared=nullptr) {
 if(pattern.empty()||size<pattern.size())return nullptr;
 const size_t last=size-pattern.size();
 for(size_t i=0;i<=last;++i){if(compared)++*compared;size_t j=0;
 for(;j<pattern.size();++j)if(!pattern[j].wildcard&&bytes[i+j]!=pattern[j].value)break;
 if(j==pattern.size())return bytes+i;}
 return nullptr;
}
enum class SearchResult{Found,Pending,Unreadable};
// One descending pass retains the original byte order/4KiB/float set. Each
// pass has fresh candidates: one address seen twice can never become target #2.
template<class Read> SearchResult FindSecond(uintptr_t start,Read read,uintptr_t& target) {
 const uintptr_t end=start>4096?start-4096:0;unsigned count=0;
 for(uintptr_t p=start;p>end;--p){float value;if(!read(p,value))return SearchResult::Unreadable;
 if(value==30||value==45||value==60||value==120){if(++count==2){target=p;return SearchResult::Found;}}}
 return SearchResult::Pending;
}
template<class Read,class Write> bool Maintain(uintptr_t address,int target,Read read,Write write,bool& changed) {
 float current;changed=false;if(!read(address,current))return false;
 if(current==static_cast<float>(target))return true;
 changed=write(address,static_cast<float>(target));return changed;
}
}
