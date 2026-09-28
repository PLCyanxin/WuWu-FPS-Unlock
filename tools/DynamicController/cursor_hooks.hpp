// SPDX-License-Identifier: MIT
#pragma once
#include "dynamiclive.hpp"
#include <limits>
#pragma comment(lib,"user32.lib")
namespace wuwa::cursor_hooks {
using SetFn=HCURSOR(WINAPI*)(HCURSOR);
using GetFn=HCURSOR(WINAPI*)();
using ShowFn=int(WINAPI*)(BOOL);
enum class Status {Idle,Active,InstallFailed,AcquireFailed,DeltaLimit,VisibilityLost,WatchdogExpired};
inline std::atomic<Status> status=Status::Idle;
inline SetFn realSet=::SetCursor;
inline GetFn realGet=::GetCursor;
inline ShowFn realShow=::ShowCursor;
inline std::atomic_bool installed=false,attempted=false,owned=false,blocked=false,wanted=false;
inline std::atomic<ULONGLONG> heartbeat=0;
inline std::atomic_flag installing=ATOMIC_FLAG_INIT;
inline void Block(Status reason){status=reason;blocked=true;}
inline thread_local bool bypass=false;
struct Bypass {bool previous=bypass;Bypass(){bypass=true;}~Bypass(){bypass=previous;}};
inline constexpr int MaximumDelta=128;
struct Lease {
 bool active=false;int logical=0,physical=0;HCURSOR latest=nullptr;HWND window=nullptr;
 bool Begin(HWND hwnd) {
  Bypass guard;latest=realGet();
  const int first=realShow(TRUE);
  if(first==std::numeric_limits<int>::min()){realShow(FALSE);return false;}
  logical=first-1;physical=first;
  int calls=1;while(physical<0&&calls<8){physical=realShow(TRUE);++calls;}
  if(physical<0){while(calls-->0)realShow(FALSE);status=Status::AcquireFailed;return false;}
  window=hwnd;active=true;owned=true;status=Status::Active;return true;
 }
 void End() {
  if(!active)return;
  active=false;owned=false;if(status==Status::Active)status=Status::Idle;Bypass guard;
  // Delta is checked before every intercepted request, so this loop is bounded.
  int remaining=logical-physical;
  for(int i=0;i<MaximumDelta&&remaining!=0;++i){realShow(remaining>0);remaining+=remaining>0?-1:1;}
  realSet(latest);window=nullptr;
 }
};
inline thread_local Lease lease;
inline bool Eligible() {
 if(!lease.active)return false;
 const ULONGLONG now=GetTickCount64(),last=heartbeat.load();
 if(!wanted.load()||now<last||now-last>250||blocked.load())return false;
 // These read-only Win32 queries do not dispatch target-window messages.
 if(GetForegroundWindow()!=GetAncestor(lease.window,GA_ROOT))return false;
 CURSORINFO info{sizeof(info)};RECT rect{};
 if(!GetCursorInfo(&info)||!GetClientRect(lease.window,&rect))return false;
 POINT point=info.ptScreenPos;
 return ScreenToClient(lease.window,&point)&&PtInRect(&rect,point);
}
inline bool (*eligible)()=Eligible;
inline bool Gate() {
 if(bypass||!lease.active)return false;
 if(!eligible()){
  const auto now=GetTickCount64(),last=heartbeat.load();
  if(wanted.load()&&(now<last||now-last>250))Block(Status::WatchdogExpired);
  lease.End();return false;
 }
 return true;
}
inline HCURSOR WINAPI Set(HCURSOR cursor) {
 if(!Gate())return realSet(cursor);
 auto previous=lease.latest;lease.latest=cursor;return previous;
}
inline HCURSOR WINAPI Get() {
 if(!Gate())return realGet();
 return lease.latest;
}
inline int WINAPI Show(BOOL show) {
 if(!Gate())return realShow(show);
 const long long next=static_cast<long long>(lease.logical)+(show?1:-1);
 const long long delta=next-lease.physical;
 if(next<std::numeric_limits<int>::min()||next>std::numeric_limits<int>::max()||delta < -MaximumDelta||delta>MaximumDelta) {
  Block(Status::DeltaLimit);lease.End();return realShow(show);
 }
 lease.logical=static_cast<int>(next);return lease.logical;
}
inline bool Install(const mfgunlock::dynamiclive::TransactionOps& ops={},bool(*enumerate)(std::vector<HANDLE>&)=mfgunlock::dynamiclive::Threads) {
 if(installed.load())return true;
 if(installing.test_and_set())return false;
 struct Reset{~Reset(){installing.clear();}}reset;
 if(installed.load())return true;
 if(attempted.exchange(true))return false;
 status=Status::InstallFailed;
 HMODULE pinned=nullptr;
 if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&Set),&pinned))return false;
 std::vector<HANDLE> threads;threads.reserve(4096);
 struct Close {std::vector<HANDLE>& values;~Close(){for(auto h:values)CloseHandle(h);}}close{threads};
 if(!enumerate(threads)||ops.begin()!=NO_ERROR)return false;
 if(ops.update(GetCurrentThread())!=NO_ERROR){ops.abort();return false;}
 size_t enrolled=0;bool stable=false;
 for(unsigned round=0;round<4;++round){
  while(enrolled<threads.size())if(ops.update(threads[enrolled++])!=NO_ERROR){ops.abort();return false;}
  const size_t before=threads.size();if(!enumerate(threads)){ops.abort();return false;}
  if(before==threads.size()){stable=true;break;}
 }
 if(!stable||ops.attach(reinterpret_cast<void**>(&realSet),reinterpret_cast<void*>(&Set))!=NO_ERROR||
    ops.attach(reinterpret_cast<void**>(&realShow),reinterpret_cast<void*>(&Show))!=NO_ERROR||
    ops.attach(reinterpret_cast<void**>(&realGet),reinterpret_cast<void*>(&Get))!=NO_ERROR){ops.abort();return false;}
 if(ops.commit()!=NO_ERROR)return false;
 installed=true;status=Status::Idle;return true;
}
} // namespace wuwa::cursor_hooks
