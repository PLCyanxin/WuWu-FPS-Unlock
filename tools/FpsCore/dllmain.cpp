// SPDX-License-Identifier: MIT
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "core.hpp"
#include "signature.hpp"
namespace {
std::atomic<int> target{0}; // No memory writes before a valid client command.
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
 const auto start=GetTickCount64();bool ready=false;
 while(GetTickCount64()-start<900000&&!StopRequested()){EnumWindows(WindowReady,reinterpret_cast<LPARAM>(&ready));if(ready)break;if(Delay(100))return;}
 if(StopRequested()||(ready&&Delay(50)))return;
 auto marker=FindMarker();if(!marker){Report("WuWa FPS candidate: marker unavailable; no write.\n");return;}
 uintptr_t cursor=marker;
 for(;cursor<0x7fffffffffffULL;++cursor){if((cursor-marker)%4096==0&&StopRequested())return;uint8_t value=0;
 if(!ReadByte(cursor,value)||value==0xff){Report("WuWa FPS candidate: second marker unreadable.\n");return;}if(value==0x70)break;}
 if(cursor==0x7fffffffffffULL)return;
 const auto deadline=GetTickCount64()+915000;uintptr_t address=0;
 while(!StopRequested()&&GetTickCount64()<deadline){auto result=wuwa::fps::FindSecond(cursor,ReadFloat,address);
 if(result==wuwa::fps::SearchResult::Found)break;
 if(result==wuwa::fps::SearchResult::Unreadable)return;
 if(Delay(wuwa::fps::kCheckMs))return;
 }
 if(!address||StopRequested()){Report("WuWa FPS candidate: distinct FPS candidates unavailable; no write.\n");return;}
 do{const int requested=target.load(std::memory_order_relaxed);if(requested==0)continue;bool changed=false;if(!wuwa::fps::Maintain(address,requested,ReadFloat,WriteFloat,changed)){Report("WuWa FPS candidate: target inaccessible; maintenance stopped.\n");return;}}
 while(!Delay(wuwa::fps::kCheckMs));
}
// Cancels only this worker's pending operation; no thread termination or external process access.
bool CompleteIo(HANDLE pipe,OVERLAPPED& operation,DWORD& bytes){
 HANDLE events[]{stop_event,operation.hEvent};const auto result=WaitForMultipleObjects(2,events,FALSE,INFINITE);
 if(result!=WAIT_OBJECT_0+1){CancelIoEx(pipe,&operation);GetOverlappedResult(pipe,&operation,&bytes,TRUE);return false;}
 return GetOverlappedResult(pipe,&operation,&bytes,FALSE)!=FALSE;
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
   int parsed=0;if(wuwa::fps::ParseMessage(std::string_view(buffer,bytes),parsed))target.store(parsed,std::memory_order_relaxed);
  }
  DisconnectNamedPipe(pipe);CloseHandle(pipe);
  if(!connected&&Delay(100))break;
 }
 CloseHandle(event);return 0;
}
DWORD WINAPI Start(void*){
 HMODULE pinned=nullptr;
 if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&Start),&pinned)){Report("WuWa FPS candidate: cannot retain module; initialization stopped.\n");return 1;}
 self_module=pinned;
 HANDLE receiver=CreateThread(nullptr,0,Receive,nullptr,0,nullptr);if(!receiver)return 1;
 RunLogic();SetEvent(stop_event);WaitForSingleObject(receiver,INFINITE);CloseHandle(receiver);return 0;
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
