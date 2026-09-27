// SPDX-License-Identifier: MIT
#include "native_cursor.hpp"
#include <atomic>
#include <iostream>
#include <thread>
#include <stdexcept>
using namespace wuwa::native_cursor;
static int passed=0;
static void Check(bool value,const char* name){if(!value)throw std::runtime_error(name);++passed;std::cout<<"PASS "<<name<<'\n';}
template<class F> bool Wait(F f){for(int i=0;i<200;++i){if(f())return true;Sleep(10);}return false;}
static LRESULT CALLBACK WindowProc(HWND h,UINT m,WPARAM w,LPARAM l){if(m==WM_APP+1){DestroyWindow(h);return 0;}if(m==WM_DESTROY){PostQuitMessage(0);return 0;}return DefWindowProcW(h,m,w,l);}
int main(){try{
 Check(Fresh(true,1250,1000)&&!Fresh(true,1251,1000)&&!Fresh(false,1001,1000)&&!Fresh(true,999,1000),"watchdog boundaries and closed intent");
 for(int baseline:{-3,1,-20}){int counter=baseline,calls=0;VisibilityDebt debt;auto show=[&](bool yes){++calls;return counter+=yes?1:-1;};bool entered=debt.Enter(show);Check(entered==(baseline>=-8),"bounded activation");Check(debt.increments<=kMaximumShowCalls,"bounded counter debt");debt.Restore(show);Check(counter==baseline&&debt.increments==0,"exact restoration");int before=calls;debt.Restore(show);Check(before==calls,"idempotent restoration");}
 Check(!Update(GetDesktopWindow(),true),"reject foreign-process desktop window");
 std::atomic<HWND> window{};std::atomic<DWORD> owner{};
 std::thread ui([&]{owner=GetCurrentThreadId();WNDCLASSW wc{};wc.lpfnWndProc=WindowProc;wc.hInstance=GetModuleHandleW(nullptr);wc.lpszClassName=L"WuWa.NativeCursor.HiddenTest";RegisterClassW(&wc);window=CreateWindowExW(0,wc.lpszClassName,L"Hidden cursor test",WS_OVERLAPPED,0,0,100,100,nullptr,nullptr,wc.hInstance,nullptr);MSG msg{};while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);} });
 struct Join{std::thread& thread;std::atomic<HWND>& hwnd;~Join(){if(thread.joinable()){if(hwnd)PostMessageW(hwnd,WM_APP+1,0,0);thread.join();}}}join{ui,window};
 Check(Wait([&]{return window.load()!=nullptr;}),"hidden owner window created");
 Check(Wait([&]{Update(window,true);std::lock_guard lock(detail::mutex);return detail::installed;}),"cross-thread owner bootstrap installed");
 {std::lock_guard lock(detail::mutex);Check(detail::owner==owner&&detail::bootstrap==nullptr,"bootstrap unhooked after owner installation");Check(!detail::active&&detail::debt.increments==0,"hidden window never changes visibility count");}
 Check(!Update(window,true,Shape::Text),"unfocused hidden window falls back to software cursor");
 {std::lock_guard lock(detail::mutex);detail::last=GetTickCount64()-kWatchdogMs-1;}
 Sleep(100);
 {std::lock_guard lock(detail::mutex);Check(!detail::active&&detail::debt.increments==0,"owner watchdog handles stopped rendering");Check(detail::timer==0,"expired heartbeat stops idle timer");}
 Check(Wait([&]{Update(window,true);std::lock_guard lock(detail::mutex);return detail::timer!=0;}),"resumed rendering rearms watchdog");
 Update(window,false);Check(Wait([]{std::lock_guard lock(detail::mutex);return detail::timer==0;}),"closed menu stops timer");
 for(int i=0;i<1000;++i)Update(window,false);
 {std::lock_guard lock(detail::mutex);Check(!detail::wakePending&&detail::timer==0,"closed frames do not wake owner");}
 Stop();Check(Wait([]{std::lock_guard lock(detail::mutex);return !detail::installed&&!detail::target;}),"cross-thread Stop detaches on owner thread");
 Check(Wait([&]{Update(window,true);std::lock_guard lock(detail::mutex);return detail::installed;}),"safe reattachment");
 PostMessageW(window,WM_APP+1,0,0);ui.join();
 {std::lock_guard lock(detail::mutex);Check(!detail::installed&&!detail::target&&detail::debt.increments==0,"WM_NCDESTROY restores and detaches");}
 std::cout<<passed<<" checks passed; no game, visible window, mouse movement or OS ShowCursor calls.\n";return 0;
 }catch(const std::exception& e){std::cerr<<"FAIL "<<e.what()<<'\n';return 1;}}
