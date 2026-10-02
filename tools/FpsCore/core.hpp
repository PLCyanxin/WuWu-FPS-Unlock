// SPDX-License-Identifier: MIT
// Derived from 30launchers/WutheringWaves-FPS-unlocker, see SOURCE.json.
#pragma once
#include <algorithm>
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
inline constexpr unsigned kSuspendedCheckMs=250;
inline constexpr size_t kSecondMarkerBudget=64*1024;
enum class State:int {WaitingRenderer,SearchingMarker,SearchingCandidate,WaitingCommand,Maintaining,Suspended,Stopped};
enum class Reason:int {None,MarkerNotFound,SecondMarkerUnavailable,CandidateUnreadable,CandidateTimeout,TargetUnreadable,TargetValueInvalid,TargetWriteFailed,TargetMappingChanged,RendererTimeout,StopRequested,AnchorChanged,InternalFailure};
struct Maintenance { State state=State::WaitingCommand; Reason reason=Reason::None; int applied=0; uint64_t repairs=0; };
// These validators/readers operate on the original address only. Never select a
// replacement candidate during maintenance. Terminal identity failures latch.
template<class Validate,class Read,class Write>
inline void MaintenanceStep(Maintenance& state,uintptr_t address,int requested,Validate validate,Read read,Write write) {
 if(state.state==State::Stopped)return;
 const auto identity=validate();
 if(identity!=Reason::None){state.state=State::Stopped;state.reason=identity;return;}
 if(requested==0){state.state=State::WaitingCommand;state.reason=Reason::None;return;}
 float current=0;
 if(!read(address,current)){state.state=State::Suspended;state.reason=Reason::TargetUnreadable;return;}
 if(requested<kMinFps||requested>kMaxFps||!std::isfinite(current)||current<kMinFps||current>kMaxFps){state.state=State::Suspended;state.reason=Reason::TargetValueInvalid;return;}
 if(current!=static_cast<float>(requested)){
  if(!write(address,static_cast<float>(requested))){state.state=State::Suspended;state.reason=Reason::TargetWriteFailed;return;}
  ++state.repairs;
  if(!read(address,current)){state.state=State::Suspended;state.reason=Reason::TargetUnreadable;return;}
  if(current!=static_cast<float>(requested)){state.state=State::Suspended;state.reason=Reason::TargetWriteFailed;return;}
 }
 state.applied=requested;state.state=State::Maintaining;state.reason=Reason::None;
}
template<class Requested,class Validate,class Read,class Write,class Delay,class Publish>
inline void RunMaintenance(Maintenance& state,uintptr_t address,Requested requested,Validate validate,Read read,Write write,Delay delay,Publish publish){
 do{MaintenanceStep(state,address,requested(),validate,read,write);publish(state);if(state.state==State::Stopped)return;}
 while(!delay(state.state==State::Suspended?kSuspendedCheckMs:kCheckMs));
 state.state=State::Stopped;state.reason=Reason::StopRequested;publish(state);
}
// Exclusive end must be the end of the original readable region. A byte 0xff
// is ordinary data, not an exception sentinel. Both region and budget bound work.
template<class Read,class Stop>
inline bool FindSecondMarker(uintptr_t start,uintptr_t end,Read read,Stop stop,uintptr_t& result){
 result=0;if(end<=start)return false;
 const size_t count=static_cast<size_t>((std::min)(uintptr_t(kSecondMarkerBudget),end-start));
 for(size_t offset=0;offset<count;++offset){if(offset%4096==0&&stop())return false;uint8_t value=0;
  if(!read(start+offset,value))return false;if(value==0x70){result=start+offset;return true;}}
 return false;
}
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
 if(target<kMinFps||target>kMaxFps||!std::isfinite(current)||current<kMinFps||current>kMaxFps)return false;
 if(current==static_cast<float>(target))return true;
 changed=write(address,static_cast<float>(target));return changed;
}
}
