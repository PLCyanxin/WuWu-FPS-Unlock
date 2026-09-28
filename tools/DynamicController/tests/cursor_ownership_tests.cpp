// SPDX-License-Identifier: MIT
#include "../cursor_hooks.hpp"
#include <iostream>
#include <thread>
#include <stdexcept>
using namespace wuwa::cursor_hooks;
static thread_local int fakeCount=-1,showCalls=0,setCalls=0;
static thread_local HCURSOR fakeCursor=nullptr;
__declspec(noinline) HCURSOR WINAPI FakeSet(HCURSOR value){++setCalls;auto old=fakeCursor;fakeCursor=value;return old;}
__declspec(noinline) HCURSOR WINAPI FakeGet(){return fakeCursor;}
__declspec(noinline) int WINAPI FakeShow(BOOL show){++showCalls;return fakeCount+=show?1:-1;}
static HWND testWindow=nullptr;
static HWND FakeForeground(){return testWindow;}
static BOOL FakeInfo(PCURSORINFO info){info->hCursor=fakeCursor;info->flags=fakeCount>=0?CURSOR_SHOWING:0;POINT point{20,20};ClientToScreen(testWindow,&point);info->ptScreenPos=point;return TRUE;}
#define GetForegroundWindow FakeForeground
#define GetCursorInfo FakeInfo
#include "../native_cursor.hpp"
#undef GetForegroundWindow
#undef GetCursorInfo
static int checks=0;
static void Check(bool condition,const char* message){++checks;if(!condition)throw std::runtime_error(message);std::cout<<"PASS "<<message<<'\n';}
static bool permitted=true;
static bool Permit(){return permitted&&wanted&&!blocked;}
static int aborts=0,attaches=0;
static LONG WINAPI Begin(){return NO_ERROR;}
static LONG WINAPI Enroll(HANDLE){return NO_ERROR;}
static LONG WINAPI FailSecond(PVOID*,PVOID){return ++attaches==2?ERROR_INVALID_FUNCTION:NO_ERROR;}
static LONG WINAPI Commit(){return ERROR_INVALID_FUNCTION;}
static LONG WINAPI Abort(){++aborts;return NO_ERROR;}
static bool Empty(std::vector<HANDLE>&){return true;}
int main(){try{
 realSet=FakeSet;realGet=FakeGet;realShow=FakeShow;eligible=Permit;
 mfgunlock::dynamiclive::TransactionOps failure{Begin,Enroll,FailSecond,Commit,Abort};
 Check(!Install(failure,Empty)&&aborts==1&&attaches==2&&!installed,"second hook failure aborts atomic group");
 Check(realSet==FakeSet&&realGet==FakeGet&&realShow==FakeShow,"failed group leaves all original pointers");
 // Real code patching targets inert local functions, never user32 or a game.
 const SetFn callSet=FakeSet;const GetFn callGet=FakeGet;const ShowFn callShow=FakeShow;
 attempted=false;
 Check(Install(),"real Detours installs all three local fixture hooks");
 HWND window=CreateWindowExW(0,L"STATIC",L"Invisible cursor fixture",WS_POPUP,0,0,100,100,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
 Check(window!=nullptr,"hidden same-process window created");
 HCURSOR gameA=reinterpret_cast<HCURSOR>(static_cast<UINT_PTR>(1)),gameB=reinterpret_cast<HCURSOR>(static_cast<UINT_PTR>(2)),hardware=reinterpret_cast<HCURSOR>(static_cast<UINT_PTR>(3));
 callSet(gameA);Check(callGet()==gameA,"inactive calls pass through");
 heartbeat=GetTickCount64();wanted=true;
 for(int baseline:{-3,1,-20}){fakeCount=baseline;Check(lease.Begin(window)==(baseline>=-8),"bounded lease acquisition");lease.End();Check(fakeCount==baseline,"lease restores exact initial count");int calls=showCalls;lease.End();Check(calls==showCalls,"lease End is idempotent");}
 fakeCount=-1;Check(lease.Begin(window),"owner acquires visibility with one increment");
 {Bypass guard;realSet(hardware);}Check(fakeCount==0&&lease.logical==-1,"physical and game logical count separated");
 int writes=setCalls,shows=showCalls;
 for(int i=0;i<100;++i){Check(callSet(i%2?gameA:gameB)==(i==0?gameA:(i%2?gameB:gameA)),"SetCursor returns previous game intent");Check(callGet()==(i%2?gameA:gameB),"GetCursor returns matching game intent");Check(callShow(TRUE)==0&&callShow(FALSE)==-1,"ShowCursor returns exact logical counts");}
 Check(fakeCursor==hardware&&fakeCount==0&&writes==setCalls&&shows==showCalls,"normal game requests never hide or replace hardware cursor");
 bool foreign=false;std::thread other([&]{foreign=callSet(gameB)==nullptr&&callGet()==gameB&&callShow(TRUE)==0&&!lease.active;});other.join();Check(foreign,"other thread remains untouched passthrough");
 callSet(nullptr);callShow(TRUE);wanted=false;Check(callGet()==nullptr,"gate close restores latest nullptr before forwarding GetCursor");
 Check(!lease.active&&fakeCursor==nullptr&&fakeCount==0,"exit preserves game cumulative show intent");
 heartbeat=GetTickCount64();wanted=true;Check(lease.Begin(window),"second session acquires");
 {Bypass guard;realSet(hardware);}
 int requests=0;while(lease.active&&requests<140){callShow(FALSE);++requests;}
 Check(blocked&&!lease.active&&requests<=130,"delta limit safely ends ownership before unbounded debt");
 Check(fakeCount==-requests,"overflow call forwarded after exact bounded reconciliation");
 blocked=false;wanted=true;fakeCount=-1;Check(lease.Begin(window),"lease for focus restoration");
 callSet(gameB);permitted=false;Check(callGet()==gameB&&!lease.active&&fakeCount==-1,"qualification loss restores latest game intent and count");permitted=true;
 blocked=false;wanted=true;fakeCount=-10;shows=showCalls;
 Check(!lease.Begin(window)&&fakeCount==-10&&showCalls-shows==16,"failed acquisition restores bounded eight increments");
 // Actual public Update/owner Tick path with injected display observations.
 // Detours still targets only FakeSet/FakeShow/FakeGet; no desktop mutation.
 testWindow=window;fakeCount=-1;blocked=false;wanted=false;
 Check(wuwa::native_cursor::Update(window,true),"public Update installs subclass and acquires hardware lease");
 const int beforeSet=setCalls,beforeShow=showCalls;
 for(int i=0;i<40;++i){callSet(i%2?nullptr:gameB);callShow(TRUE);callShow(FALSE);Check(wuwa::native_cursor::Update(window,true),"overlay retains hardware through competing game API intent");}
 Check(setCalls==beforeSet&&showCalls==beforeShow,"competing game calls cause no physical cursor changes");
 PostMessageW(window,WM_TIMER,wuwa::native_cursor::detail::timer,0);
 {MSG message{};while(PeekMessageW(&message,window,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}}
 Check(setCalls==beforeSet,"watchdog does not periodically reset same native shape");
 Check(wuwa::native_cursor::Update(window,true,wuwa::native_cursor::Shape::Text),"real owner Tick changes intended cursor shape");
 callSet(gameB);permitted=false;callGet();
 Check(!owned&&!lease.active,"hook gate exit publishes lost ownership to render state");permitted=true;
 Check(wuwa::native_cursor::Update(window,true),"owner resumes with a new lease after qualification returns");
 callSet(gameB);wuwa::native_cursor::Update(window,false);
 Check(!lease.active&&fakeCursor==gameB&&fakeCount==-1,"menu close restores latest game shape and accumulated count");
 Check(wuwa::native_cursor::Update(window,true),"next menu reacquires native ownership");
 heartbeat=GetTickCount64()-300;
 PostMessageW(window,WM_TIMER,wuwa::native_cursor::detail::timer,0);
 {MSG message{};while(PeekMessageW(&message,window,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}}
 Check(!lease.active&&fakeCount==-1&&blocked,"owner watchdog restores stalled session");
 wuwa::native_cursor::Stop();
 DestroyWindow(window);
 std::cout<<checks<<" cursor virtualization assertions passed; real Detours on inert functions only.\n";return 0;
 }catch(const std::exception& e){std::cerr<<"FAIL "<<e.what()<<'\n';return 1;}}
