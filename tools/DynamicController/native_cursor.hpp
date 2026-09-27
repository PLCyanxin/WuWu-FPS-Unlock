// SPDX-License-Identifier: MIT
#pragma once
#include <windows.h>
#include <commctrl.h>
#include <mutex>
#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "user32.lib")

namespace wuwa::native_cursor {
enum class Shape { Arrow, Text, ResizeAll, ResizeNS, ResizeEW, ResizeNESW, ResizeNWSE, Hand, NotAllowed };
inline constexpr ULONGLONG kWatchdogMs = 250;
inline constexpr int kMaximumShowCalls = 8;
inline bool Fresh(bool requested, ULONGLONG now, ULONGLONG last) {
 return requested && now >= last && now-last <= kWatchdogMs;
}
// The counter tracks only increments made by this module, not the game's state.
struct VisibilityDebt {
 int increments=0;
 template<class Show> void Restore(Show show) { while(increments>0) { show(false); --increments; } }
 template<class Show> bool Enter(Show show) {
  for(int i=0;i<kMaximumShowCalls;++i) { ++increments; if(show(true)>=0) return true; }
  Restore(show); return false;
 }
};
namespace detail {
inline std::mutex mutex;
inline HWND target=nullptr;
inline DWORD owner=0;
inline HHOOK bootstrap=nullptr;
inline PTP_TIMER timeout=nullptr;
inline bool installed=false, requested=false, stopping=false, active=false, failed=false, wakePending=false;
inline ULONGLONG last=0, bootstrapDeadline=0, retryAfter=0;
inline Shape shape=Shape::Arrow;
inline VisibilityDebt debt;
inline HCURSOR previous=nullptr, current=nullptr;
inline UINT wake=0;
inline UINT_PTR timer=0;
inline int anchor;
inline UINT_PTR Id() { return reinterpret_cast<UINT_PTR>(&anchor); }
inline bool InClient(HWND hwnd) {
 if(!IsWindow(hwnd) || GetForegroundWindow()!=GetAncestor(hwnd,GA_ROOT)) return false;
 CURSORINFO info{sizeof(info)}; RECT client{};
 if(!GetCursorInfo(&info))return false;
 POINT point=info.ptScreenPos;
 return ScreenToClient(hwnd,&point) && GetClientRect(hwnd,&client) && PtInRect(&client,point);
}
inline HCURSOR Cursor(Shape value) {
 WORD id=32512;
 switch(value) {
  case Shape::Text:id=32513;break;case Shape::ResizeAll:id=32646;break;
  case Shape::ResizeNS:id=32645;break;case Shape::ResizeEW:id=32644;break;
  case Shape::ResizeNESW:id=32643;break;case Shape::ResizeNWSE:id=32642;break;
  case Shape::Hand:id=32649;break;case Shape::NotAllowed:id=32648;break;default:break;
 }
 return LoadCursorW(nullptr,MAKEINTRESOURCEW(id));
}
inline bool Visible() { CURSORINFO info{sizeof(info)}; return GetCursorInfo(&info) && current!=nullptr && info.hCursor==current && (info.flags&CURSOR_SHOWING)!=0; }
inline void Restore() {
 active=false;
 debt.Restore([](bool show){return ShowCursor(show);});
 if(current && GetCursor()==current) SetCursor(previous);
 current=nullptr;previous=nullptr;
}
inline void CancelBootstrap() { if(bootstrap){UnhookWindowsHookEx(bootstrap);bootstrap=nullptr;} }
inline void CALLBACK BootstrapTimeout(PTP_CALLBACK_INSTANCE,void*,PTP_TIMER) {
 std::lock_guard lock(mutex);
 if(!installed && GetTickCount64()>=bootstrapDeadline) {CancelBootstrap();target=nullptr;owner=0;requested=false;}
}
inline LRESULT CALLBACK Subclass(HWND hwnd,UINT message,WPARAM wp,LPARAM lp,UINT_PTR,DWORD_PTR);
inline void Detach(HWND hwnd) {
 Restore();if(timer){KillTimer(hwnd,timer);timer=0;}
 RemoveWindowSubclass(hwnd,Subclass,Id());
 installed=false;target=nullptr;owner=0;requested=false;stopping=false;failed=false;wakePending=false;
}
inline void Tick(HWND hwnd) {
 if(stopping){Detach(hwnd);return;}
 const bool fresh=Fresh(requested,GetTickCount64(),last);
 if(!fresh) {
  Restore();failed=false;
  if(timer){KillTimer(hwnd,timer);timer=0;}
  return;
 }
 if(!timer)timer=SetTimer(hwnd,Id(),50,nullptr);
 // A cursor must never activate without an owner-thread restoration watchdog.
 if(!timer){Restore();failed=true;return;}
 const bool eligible=InClient(hwnd);
 if(!eligible){Restore();failed=false;return;}
 if(failed)return;
 if(!active) {
  previous=GetCursor();
  if(!debt.Enter([](bool show){return ShowCursor(show);})){previous=nullptr;failed=true;return;}
  active=true;
 }
 current=Cursor(shape);
 if(!current){Restore();failed=true;return;}
 SetCursor(current);
 if(!Visible()){Restore();failed=true;}
}
inline LRESULT CALLBACK Subclass(HWND hwnd,UINT message,WPARAM wp,LPARAM lp,UINT_PTR,DWORD_PTR) {
 bool handled=false;
 {
  std::lock_guard lock(mutex);
  if(hwnd==target) {
   if(message==WM_NCDESTROY) Detach(hwnd);
   else if(message==wake){wakePending=false;Tick(hwnd);handled=true;}
   else if(message==WM_TIMER && timer && wp==timer){Tick(hwnd);handled=true;}
   else if(message==WM_KILLFOCUS || (message==WM_ACTIVATEAPP && !wp) || (message==WM_ACTIVATE && LOWORD(wp)==WA_INACTIVE)) Restore();
   else if(message==WM_SETCURSOR && LOWORD(lp)==HTCLIENT) {Tick(hwnd);handled=active;}
  }
 }
 return handled?TRUE:DefSubclassProc(hwnd,message,wp,lp);
}
inline bool Install(HWND hwnd) {
 DWORD pid=0;
 if(GetCurrentThreadId()!=owner || GetWindowThreadProcessId(hwnd,&pid)!=owner || pid!=GetCurrentProcessId() || !SetWindowSubclass(hwnd,Subclass,Id(),0))return false;
 timer=SetTimer(hwnd,Id(),50,nullptr);
 if(!timer){RemoveWindowSubclass(hwnd,Subclass,Id());return false;}
 installed=true;Tick(hwnd);return true;
}
inline LRESULT CALLBACK Bootstrap(int code,WPARAM wp,LPARAM lp) {
 {
  std::lock_guard lock(mutex);
  if(code>=0 && target && owner==GetCurrentThreadId()) {
   CancelBootstrap();
   if(!stopping && IsWindow(target)) {if(!Install(target)){target=nullptr;owner=0;requested=false;}}
   else {target=nullptr;owner=0;requested=false;stopping=false;}
  }
 }
 return CallNextHookEx(nullptr,code,wp,lp);
}
inline bool Begin(HWND hwnd) {
 DWORD pid=0;DWORD thread=GetWindowThreadProcessId(hwnd,&pid);
 if(!thread || pid!=GetCurrentProcessId())return false;
 HMODULE module=nullptr;
 // Both window and one-shot bootstrap callbacks remain executable until process exit.
 if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
   reinterpret_cast<LPCWSTR>(&anchor),&module))return false;
 if(!wake)wake=RegisterWindowMessageW(L"WuWa.DynamicMax.NativeCursor.Wake.v1");
 if(!wake)return false;
 target=hwnd;owner=thread;stopping=false;failed=false;
 if(thread==GetCurrentThreadId()) {if(Install(hwnd))return true;target=nullptr;owner=0;return false;}
 if(!timeout)timeout=CreateThreadpoolTimer(BootstrapTimeout,nullptr,nullptr);
 if(!timeout){target=nullptr;owner=0;return false;}
 bootstrap=SetWindowsHookExW(WH_GETMESSAGE,Bootstrap,module,thread);
 if(!bootstrap){target=nullptr;owner=0;return false;}
 FILETIME due{};const LONGLONG relative=-10000000LL;due.dwLowDateTime=static_cast<DWORD>(relative);due.dwHighDateTime=static_cast<DWORD>(relative>>32);
 bootstrapDeadline=GetTickCount64()+1000;
 SetThreadpoolTimer(timeout,&due,0,0);
 if(!PostMessageW(hwnd,wake,0,0)){CancelBootstrap();target=nullptr;owner=0;return false;}
 return true;
}
inline void WakeOwner(HWND hwnd) {
 if(!wakePending)wakePending=PostMessageW(hwnd,wake,0,0)!=FALSE;
}
} // namespace detail
// Call once per overlay frame. True alone permits hiding that frame's software cursor.
inline bool Update(HWND hwnd,bool softwareRequested,Shape cursor=Shape::Arrow) {
 std::lock_guard lock(detail::mutex);
 if(detail::target && detail::target!=hwnd)return false;
 if(detail::stopping)return false;
 const bool changed=detail::requested!=softwareRequested || detail::shape!=cursor;
 detail::requested=softwareRequested;detail::last=GetTickCount64();detail::shape=cursor;
 if(!softwareRequested && !detail::target)return false;
 if(!detail::target) {
  if(!softwareRequested || GetTickCount64()<detail::retryAfter)return false;
  detail::retryAfter=GetTickCount64()+1000;
  if(!detail::Begin(hwnd))return false;
 }
 if(detail::installed && (changed || (softwareRequested && !detail::timer))) {
  if(detail::owner==GetCurrentThreadId())detail::Tick(hwnd);
  else detail::WakeOwner(hwnd);
 }
 return softwareRequested && detail::active && detail::InClient(hwnd) && detail::Visible();
}
// Requests owner-thread restoration/removal. Never call from DllMain or while holding the loader lock.
inline void Stop() {
 std::lock_guard lock(detail::mutex);
 detail::requested=false;detail::stopping=true;
 if(!detail::installed){detail::CancelBootstrap();detail::target=nullptr;detail::owner=0;detail::stopping=false;}
 else if(detail::owner==GetCurrentThreadId())detail::Detach(detail::target);
 else detail::WakeOwner(detail::target);
}
} // namespace wuwa::native_cursor

