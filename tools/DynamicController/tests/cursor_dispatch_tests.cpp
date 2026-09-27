// SPDX-License-Identifier: MIT
#include "../native_cursor.hpp"
#include <atomic>
#include <thread>
#include <iostream>
using namespace wuwa::native_cursor;
std::atomic<HWND> hwnd; std::atomic<int> wakes=0,ticks=0;HANDLE started=CreateEventW(nullptr,TRUE,FALSE,nullptr),resume=CreateEventW(nullptr,TRUE,FALSE,nullptr);
LRESULT CALLBACK Count(HWND h,UINT m,WPARAM w,LPARAM l,UINT_PTR,DWORD_PTR){if(m==detail::wake)++wakes;if(m==WM_TIMER)++ticks;return DefSubclassProc(h,m,w,l);}
LRESULT CALLBACK Proc(HWND h,UINT m,WPARAM w,LPARAM l){if(m==WM_APP+1){SetWindowSubclass(h,Count,123,0);SetEvent(started);WaitForSingleObject(resume,5000);return 0;}if(m==WM_APP+2){DestroyWindow(h);PostQuitMessage(0);return 0;}return DefWindowProcW(h,m,w,l);}
int main(){std::thread ui([]{WNDCLASSW wc{};wc.hInstance=GetModuleHandleW(nullptr);wc.lpfnWndProc=Proc;wc.lpszClassName=L"HiddenBench";RegisterClassW(&wc);hwnd=CreateWindowW(wc.lpszClassName,L"",0,0,0,10,10,nullptr,nullptr,wc.hInstance,nullptr);MSG msg;while(GetMessageW(&msg,nullptr,0,0)>0)DispatchMessageW(&msg);});while(!hwnd)Sleep(1);Update(hwnd,true);Sleep(100);PostMessageW(hwnd,WM_APP+1,0,0);WaitForSingleObject(started,2000);for(int i=0;i<1000;++i)Update(hwnd,true);SetEvent(resume);Sleep(500);const int stableWakes=wakes;std::cout<<"same-intent 1000 calls owner-wake-messages="<<wakes<<"\n";Update(hwnd,false);Sleep(100);ticks=0;Sleep(1000);std::cout<<"closed-menu timers per second="<<ticks<<"\n";const int idleTicks=ticks;Stop();Sleep(100);PostMessageW(hwnd,WM_APP+2,0,0);ui.join();CloseHandle(started);CloseHandle(resume);return stableWakes<=1&&idleTicks==0?0:1;}
