// SPDX-License-Identifier: MIT
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdio>
#include "core.hpp"
#include "signature.hpp"
namespace {
std::atomic<int> target{0}; // No memory writes before a valid client command.
// Single worker publishes; the receiver reads a coherent atomic snapshot.
struct Status {
 std::atomic<uint64_t> sequence{0},repairs{0};
 std::atomic<int> state{0},reason{0},applied{0};
 void Publish(const wuwa::fps::Maintenance& value){
  sequence.fetch_add(1);state.store(static_cast<int>(value.state));reason.store(static_cast<int>(value.reason));
  applied.store(value.applied);repairs.store(value.repairs);sequence.fetch_add(1);
 }
 wuwa::fps::Maintenance Read()const{
  for(;;){auto before=sequence.load();if(before&1)continue;
   wuwa::fps::Maintenance value{static_cast<wuwa::fps::State>(state.load()),static_cast<wuwa::fps::Reason>(reason.load()),applied.load(),repairs.load()};
   if(before==sequence.load())return value;}
 }
} status;
HMODULE self_module=nullptr;
HANDLE stop_event=nullptr;
bool StopRequested(){return WaitForSingleObject(stop_event,0)==WAIT_OBJECT_0;}
bool Delay(DWORD ms){return WaitForSingleObject(stop_event,ms)==WAIT_OBJECT_0;}
void Report(const char* message){OutputDebugStringA(message);}
bool ReadByte(uintptr_t p,uint8_t& value){__try{value=*reinterpret_cast<const volatile uint8_t*>(p);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
bool ReadFloat(uintptr_t p,float& value){__try{value=*reinterpret_cast<const volatile float*>(p);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
bool WriteFloat(uintptr_t p,float value){__try{*reinterpret_cast<volatile float*>(p)=value;return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
uintptr_t ScanChunk(uintptr_t p,size_t size,const std::vector<wuwa::fps::PatternByte>& pattern){
 __try{return reinterpret_cast<uintptr_t>(wuwa::fps::FindFirst(reinterpret_cast<const uint8_t*>(p),size,pattern));}
 __except(EXCEPTION_EXECUTE_HANDLER){return 0;}
}
bool Readable(DWORD protection){return protection==PAGE_EXECUTE_READ||protection==PAGE_EXECUTE_READWRITE||protection==PAGE_EXECUTE_WRITECOPY||protection==PAGE_EXECUTE||protection==PAGE_READONLY||protection==PAGE_READWRITE||protection==PAGE_WRITECOPY;}
struct Mapping {
 uintptr_t base=0,allocation=0;size_t size=0;DWORD type=0,allocationProtect=0;
 bool Capture(uintptr_t address,size_t length){MEMORY_BASIC_INFORMATION info{};
  if(!VirtualQuery(reinterpret_cast<void*>(address),&info,sizeof info)||info.State!=MEM_COMMIT)return false;
  base=reinterpret_cast<uintptr_t>(info.BaseAddress);size=info.RegionSize;
  if(size>UINTPTR_MAX-base||address<base||address>base+size||length>base+size-address)return false;
  allocation=reinterpret_cast<uintptr_t>(info.AllocationBase);type=info.Type;allocationProtect=info.AllocationProtect;return true;}
 bool Matches(uintptr_t address,size_t length)const{Mapping now;
  return now.Capture(address,length)&&now.base==base&&now.size==size&&now.allocation==allocation&&now.type==type&&now.allocationProtect==allocationProtect;}
};
struct TargetIdentity {
 Mapping targetMapping,anchorMapping;uintptr_t address=0,marker=0;
 std::vector<wuwa::fps::PatternByte> pattern;
 bool Capture(uintptr_t found,uintptr_t anchor){address=found;marker=anchor;pattern=wuwa::fps::ParsePattern(wuwa::fps::kSignature);
  return !pattern.empty()&&targetMapping.Capture(address,sizeof(float))&&anchorMapping.Capture(marker,pattern.size());}
 wuwa::fps::Reason Validate()const{
  if(!targetMapping.Matches(address,sizeof(float)))return wuwa::fps::Reason::TargetMappingChanged;
  if(!anchorMapping.Matches(marker,pattern.size()))return wuwa::fps::Reason::AnchorChanged;
  for(size_t i=0;i<pattern.size();++i){uint8_t value=0;if(!ReadByte(marker+i,value)||(!pattern[i].wildcard&&value!=pattern[i].value))return wuwa::fps::Reason::AnchorChanged;}
  return wuwa::fps::Reason::None;
 }
};
uintptr_t FindMarker(){
 const auto pattern=wuwa::fps::ParsePattern(wuwa::fps::kSignature);if(pattern.empty())return 0;
 SYSTEM_INFO si{};GetSystemInfo(&si);uintptr_t p=reinterpret_cast<uintptr_t>(si.lpMinimumApplicationAddress),end=reinterpret_cast<uintptr_t>(si.lpMaximumApplicationAddress);
 while(p<end&&!StopRequested()) {MEMORY_BASIC_INFORMATION info{};if(!VirtualQuery(reinterpret_cast<void*>(p),&info,sizeof info))break;
 const auto base=reinterpret_cast<uintptr_t>(info.BaseAddress);if(info.RegionSize>UINTPTR_MAX-base||base+info.RegionSize<=p)break;
 if(info.AllocationBase!=self_module&&info.State==MEM_COMMIT&&Readable(info.Protect)) {
  // Ordered overlapping chunks preserve first-match behavior and bound stop latency.
  for(size_t offset=0;offset<info.RegionSize&&!StopRequested();) {
   const size_t size=(std::min)(size_t(1024*1024),info.RegionSize-offset);
   if(auto found=ScanChunk(base+offset,size,pattern))return found;
   if(size<pattern.size()||offset+size==info.RegionSize)break;
   offset+=size-pattern.size()+1;
  }
 }
 p=base+info.RegionSize;
 }
 return 0;
}
BOOL CALLBACK WindowReady(HWND hwnd,LPARAM state){DWORD pid=0;GetWindowThreadProcessId(hwnd,&pid);char name[256]{};
 if(pid==GetCurrentProcessId()&&IsWindowVisible(hwnd)&&GetWindowTextLengthA(hwnd)>0&&GetClassNameA(hwnd,name,sizeof name)&&std::strcmp(name,"UnrealWindow")==0){*reinterpret_cast<bool*>(state)=true;return FALSE;}return TRUE;}
void RunLogic(){
 using namespace wuwa::fps;Maintenance current;
 auto publish=[&](State state,Reason reason=Reason::None){current.state=state;current.reason=reason;status.Publish(current);};
 auto stopped=[&](Reason reason){publish(State::Stopped,reason);};
 publish(State::WaitingRenderer);
 const auto start=GetTickCount64();bool ready=false;
 while(GetTickCount64()-start<900000&&!StopRequested()){EnumWindows(WindowReady,reinterpret_cast<LPARAM>(&ready));if(ready)break;if(Delay(100)){stopped(Reason::StopRequested);return;}}
 if(StopRequested()||(ready&&Delay(50))){stopped(Reason::StopRequested);return;}
 if(!ready){stopped(Reason::RendererTimeout);return;}
 publish(State::SearchingMarker);
 auto marker=FindMarker();if(!marker){stopped(StopRequested()?Reason::StopRequested:Reason::MarkerNotFound);return;}
 Mapping markerRegion;uintptr_t cursor=0;
 if(!markerRegion.Capture(marker,1)||!FindSecondMarker(marker,markerRegion.base+markerRegion.size,ReadByte,StopRequested,cursor)){
  stopped(StopRequested()?Reason::StopRequested:Reason::SecondMarkerUnavailable);return;}
 publish(State::SearchingCandidate);
 const auto deadline=GetTickCount64()+915000;uintptr_t address=0;
 while(!StopRequested()&&GetTickCount64()<deadline){auto result=FindSecond(cursor,ReadFloat,address);
 if(result==SearchResult::Found)break;
 if(result==SearchResult::Unreadable){stopped(Reason::CandidateUnreadable);return;}
 if(Delay(kCheckMs)){stopped(Reason::StopRequested);return;}
 }
 if(!address||StopRequested()){stopped(StopRequested()?Reason::StopRequested:Reason::CandidateTimeout);return;}
 TargetIdentity identity;if(!identity.Capture(address,marker)){stopped(Reason::TargetMappingChanged);return;}
 RunMaintenance(current,address,[]{return target.load(std::memory_order_relaxed);},[&]{return identity.Validate();},ReadFloat,WriteFloat,Delay,[](const Maintenance& value){status.Publish(value);});
}
// Cancels only this worker's pending operation; no thread termination or external process access.
bool CompleteIo(HANDLE pipe,OVERLAPPED& operation,DWORD& bytes){
 HANDLE events[]{stop_event,operation.hEvent};const auto result=WaitForMultipleObjects(2,events,FALSE,INFINITE);
 if(result!=WAIT_OBJECT_0+1){CancelIoEx(pipe,&operation);GetOverlappedResult(pipe,&operation,&bytes,TRUE);return false;}
 return GetOverlappedResult(pipe,&operation,&bytes,FALSE)!=FALSE;
}
bool ReplyStatus(HANDLE pipe,HANDLE event){
 const auto value=status.Read();char buffer[128];
 const int length=std::snprintf(buffer,sizeof buffer,"WUWA-FPS/1,%lu,%d,%d,%d,%d,%llu\n",static_cast<unsigned long>(GetCurrentProcessId()),
  static_cast<int>(value.state),static_cast<int>(value.reason),target.load(std::memory_order_relaxed),value.applied,static_cast<unsigned long long>(value.repairs));
 if(length<=0||length>=static_cast<int>(sizeof buffer))return false;
 OVERLAPPED operation{};operation.hEvent=event;ResetEvent(event);DWORD bytes=0;
 bool ok=WriteFile(pipe,buffer,static_cast<DWORD>(length),&bytes,&operation)!=FALSE;
 if(!ok&&GetLastError()==ERROR_IO_PENDING)ok=CompleteIo(pipe,operation,bytes);
 return ok&&bytes==static_cast<DWORD>(length);
}
DWORD WINAPI Receive(void* fixturePipe){
 const char* pipeName=fixturePipe?static_cast<const char*>(fixturePipe):wuwa::fps::kPipe;
 HANDLE event=CreateEventW(nullptr,TRUE,FALSE,nullptr);if(!event)return 1;
 while(!StopRequested()){
  HANDLE pipe=CreateNamedPipeA(pipeName,PIPE_ACCESS_DUPLEX|FILE_FLAG_OVERLAPPED,PIPE_TYPE_MESSAGE|PIPE_READMODE_MESSAGE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,PIPE_UNLIMITED_INSTANCES,4096,4096,0,nullptr);
  if(pipe==INVALID_HANDLE_VALUE){if(Delay(1000))break;continue;}
  OVERLAPPED operation{};operation.hEvent=event;ResetEvent(event);DWORD bytes=0;
  bool connected=ConnectNamedPipe(pipe,&operation)!=FALSE;
  if(!connected){const auto error=GetLastError();connected=error==ERROR_PIPE_CONNECTED||(error==ERROR_IO_PENDING&&CompleteIo(pipe,operation,bytes));}
  while(connected&&!StopRequested()){
   char buffer[129];operation={};operation.hEvent=event;ResetEvent(event);bytes=0;
   bool ok=ReadFile(pipe,buffer,sizeof buffer,&bytes,&operation)!=FALSE;
   if(!ok&&GetLastError()==ERROR_IO_PENDING)ok=CompleteIo(pipe,operation,bytes);
   // Oversize/partial messages (ERROR_MORE_DATA included) disconnect, never
   // accept a continuation as a fresh six-field command.
   if(!ok||bytes==0||bytes>128)break;
   const std::string_view message(buffer,bytes);
   if(message=="status-v1"){if(!ReplyStatus(pipe,event))break;continue;}
   int parsed=0;if(wuwa::fps::ParseMessage(message,parsed))target.store(parsed,std::memory_order_relaxed);
  }
  DisconnectNamedPipe(pipe);CloseHandle(pipe);
  if(!connected&&Delay(100))break;
 }
 CloseHandle(event);return 0;
}
DWORD WINAPI Start(void*){
 HMODULE pinned=nullptr;
 if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&Start),&pinned)){status.Publish({wuwa::fps::State::Stopped,wuwa::fps::Reason::InternalFailure});Report("WuWa FPS candidate: cannot retain module; initialization stopped.\n");return 1;}
 self_module=pinned;
 HANDLE receiver=CreateThread(nullptr,0,Receive,nullptr,0,nullptr);if(!receiver){status.Publish({wuwa::fps::State::Stopped,wuwa::fps::Reason::InternalFailure});return 1;}
 try{RunLogic();}catch(...){auto failed=status.Read();failed.state=wuwa::fps::State::Stopped;failed.reason=wuwa::fps::Reason::InternalFailure;status.Publish(failed);}
 // A terminal scanner remains diagnosable; only explicit stop/process exit ends
 // the receiver. The pinned module is never restarted by another FPS command.
 WaitForSingleObject(stop_event,INFINITE);WaitForSingleObject(receiver,INFINITE);CloseHandle(receiver);return 0;
}
}
// Optional diagnostic lifecycle API. Existing launcher does not invoke it.
extern "C" __declspec(dllexport) void RequestFpsCoreStop(){if(stop_event)SetEvent(stop_event);}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID){
 if(reason==DLL_PROCESS_ATTACH){DisableThreadLibraryCalls(module);
  wchar_t path[32768];const DWORD length=GetModuleFileNameW(nullptr,path,32768);
  if(!length||length>=32768)return TRUE;
  const wchar_t* leaf=wcsrchr(path,L'\\');leaf=leaf?leaf+1:path;
  if(_wcsicmp(leaf,L"Client-Win64-Shipping.exe")!=0)return TRUE;
  stop_event=CreateEventW(nullptr,TRUE,FALSE,nullptr);if(!stop_event)return FALSE;
  HANDLE worker=CreateThread(nullptr,0,Start,nullptr,0,nullptr);if(!worker){CloseHandle(stop_event);stop_event=nullptr;return FALSE;}CloseHandle(worker);
 }
 // No loader-lock waits. Successful initialization pins the candidate until
 // process exit; explicit FreeLibrary during bootstrap is unsupported.
 return TRUE;
}
