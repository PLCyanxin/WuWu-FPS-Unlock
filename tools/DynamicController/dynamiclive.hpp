// SPDX-License-Identifier: MIT
// Experimental adapter for ONE audited Streamline image; not a generic ABI.
#pragma once
#include <windows.h>
#include <bcrypt.h>
#include <tlhelp32.h>
#include <detours.h>
#include <intrin.h>
#include <atomic>
#include <array>
#include <vector>
#include <string>
#include <algorithm>
#include <cstring>
#include "dynamiclive_config.hpp"
#pragma comment(lib, "bcrypt.lib")
namespace mfgunlock::dynamiclive {
constexpr unsigned kEntry=0x464a0, kReturn=0x47275;
constexpr char kHash[]="F4A6B2B14DCC0B1485989E430D3B4E3A44AC1800B92BA1AD74F476E64FB2B09C";
using NativeFn=void(*)(void*,void*);
using ValidateFn=bool(*)(void*,void*,void*,void*);
constexpr unsigned kValidateEntry=0x4bbc0,kValidateReturn=0x476f4;
inline ValidateFn original_validate=nullptr;
inline std::atomic_bool fixed_enabled{true};
inline std::atomic_bool generation_off{false};
// Unified game-default mode observes accepted frames without imposing a limit.
inline std::atomic_bool observation_enabled{false};
inline NativeFn original=nullptr;
inline HMODULE owner=nullptr;
inline const std::atomic_bool* addon_enabled=nullptr;
inline const std::atomic_bool* dynamic_enabled=nullptr;
inline std::atomic<int> requested{kDefaultRequest}; // V2: -1 follows native bound; 0 disables generated output for this frame
inline std::atomic<bool> installed{false}, enabled{false}, invalid{false};
inline std::atomic<unsigned long long> observation{0}; // coherent request/native bound/cap + observed native total (bits24..31)
inline std::atomic<unsigned long long> observed_frames{0};
inline std::atomic<unsigned> ui_revision{2};
inline std::atomic<unsigned long long> ui_frame{0};
inline thread_local const void* ui_dynamic_snapshot=nullptr;
inline thread_local unsigned ui_dynamic_revision=0;
inline void UiChoiceChanged(){ui_revision.fetch_add(1);ui_frame.store(0);}
inline void UiChoiceCommitted(){ui_revision.fetch_add(1);}
inline void PublishUiFrame(const void* snapshot,unsigned revision) {
 unsigned mode=0,total=0;std::memcpy(&mode,static_cast<const char*>(snapshot)+0x20,4);std::memcpy(&total,static_cast<const char*>(snapshot)+8,4);
 if((revision&1)||revision!=ui_revision.load())return;
 if(mode>3||total<1||total>6||(mode==0&&total!=1)){ui_frame.store(0);return;}
 ui_frame.store((static_cast<unsigned long long>(GetTickCount())<<32)|(static_cast<unsigned long long>(revision&0xffffff)<<8)|(total<<2)|mode);
}
// 0 = no current observation; 1 = native Off; 2..6 = observed snapshot total.
inline unsigned UiFrameStatus(unsigned now=GetTickCount()) {
 const auto sample=ui_frame.load();
 if(!sample||((sample>>8)&0xffffff)!=(ui_revision.load()&0xffffff)||now-static_cast<unsigned>(sample>>32)>2000)return 0;
 const auto mode=sample&3,total=(sample>>2)&7;
 return mode==0&&total==1?1:(total==1?7:static_cast<unsigned>(total));
}

inline std::atomic_flag installing=ATOMIC_FLAG_INIT;
inline std::atomic<const char*> status{"Waiting for supported Streamline; configured live request ready"};
inline unsigned Limit(unsigned native, int request) {
 return request>=2&&request<=6 ? (std::min)(native,static_cast<unsigned>(request-1)) : native;
}
inline bool Policy(void* snapshot,int request,unsigned long long& sample) {
 // Caller validates the stack allocation first. memcpy avoids alignment/alias assumptions.
 unsigned mode=0,bound=0;
 std::memcpy(&mode,static_cast<char*>(snapshot)+0x20,4);
 std::memcpy(&bound,static_cast<char*>(snapshot)+0x44,4);
 if(mode!=3||bound<1||bound>5||request<kNativeBound||request==1||request>6)return false;
 unsigned applied=Limit(bound,request);
 if(request>=2)std::memcpy(static_cast<char*>(snapshot)+0x44,&applied,4);
 sample=(1ull<<63)|static_cast<unsigned>(request==kNativeBound?7:request)|(static_cast<unsigned long long>(bound+1)<<8)|(static_cast<unsigned long long>(request==0?1:applied+1)<<16);
 return true;
}
// Called after the original selector, before the caller queues/copies this frame.
// This is the audited frame's native Off mode, not a zero maximum or SetOptions call.
inline bool SubmitOff(void* snapshot) {
 unsigned mode=0;std::memcpy(&mode,static_cast<char*>(snapshot)+0x20,4);
 if(mode!=3)return false;
 const unsigned zero=0,one=1;
 std::memcpy(static_cast<char*>(snapshot)+0x20,&zero,4);
 std::memcpy(static_cast<char*>(snapshot)+0x00,&zero,4);
 std::memcpy(static_cast<char*>(snapshot)+0x04,&zero,4);
 std::memcpy(static_cast<char*>(snapshot)+0x08,&one,4);
 return true;
}
inline bool Writable(const void* snapshot,size_t size) {
 auto p=reinterpret_cast<ULONG_PTR>(snapshot);if(!p||p+size<p)return false;
 const auto end=p+size;
 while(p<end){MEMORY_BASIC_INFORMATION info{};
  if(!VirtualQuery(reinterpret_cast<const void*>(p),&info,sizeof info)||info.State!=MEM_COMMIT||
     (info.Protect&PAGE_GUARD)||(info.Protect&0xff)!=PAGE_READWRITE)return false;
  auto next=reinterpret_cast<ULONG_PTR>(info.BaseAddress)+info.RegionSize;
  if(next<=p)return false;p=next;
 }return true;
}
inline bool StackPolicy(void* snapshot,int request,unsigned long long& sample) {
 ULONG_PTR low=0,high=0;GetCurrentThreadStackLimits(&low,&high);
 auto p=reinterpret_cast<ULONG_PTR>(snapshot);
 if(p<low||p>high||high-p<0x58||!Writable(snapshot,0x58))return false;
 __try{return Policy(snapshot,request,sample);}__except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
__declspec(noinline) inline void Wrapped(void* context,void* snapshot) {
 // The native caller owns and serializes this per-frame stack object. Do not retain it,
 // restore the cap afterwards, call SL APIs, or read UI state again during this frame.
 const void* caller=_ReturnAddress();
 bool submitOff=false;unsigned long long sample=0;
 ui_dynamic_snapshot=snapshot;ui_dynamic_revision=ui_revision.load();
 if(enabled.load(std::memory_order_acquire)&&addon_enabled&&dynamic_enabled&&
    addon_enabled->load(std::memory_order_relaxed)&&dynamic_enabled->load(std::memory_order_relaxed)){
  int value=requested.load(std::memory_order_relaxed);
  if(reinterpret_cast<ULONG_PTR>(caller)!=reinterpret_cast<ULONG_PTR>(owner)+kReturn||!StackPolicy(snapshot,value,sample)){
   enabled.store(false,std::memory_order_release);invalid.store(true); // retain safe trampoline
  }else {submitOff=value==0;}
 }
 original(context,snapshot);
 if(submitOff) {
  if(!SubmitOff(snapshot)){enabled.store(false,std::memory_order_release);invalid.store(true);sample=0;}
 }
 if(sample) {
  // A limit is not the scheduler's selected multiplier. Observe only after the
  // native selector and our Off policy have completed for this snapshot.
  unsigned total=0;std::memcpy(&total,static_cast<char*>(snapshot)+8,4);
  observation.store(sample|(static_cast<unsigned long long>(total&255)<<24),std::memory_order_relaxed);
  observed_frames.fetch_add(1,std::memory_order_release);
 }
}
// The shared native validator runs after frame counts are finalized. Unified
// Off covers modes 1/2/3; the legacy fixed-only path remains compatible.
inline bool FixedOffPolicy(void* snapshot,bool allowGeneration,bool allModes=false) {
 unsigned mode=0;std::memcpy(&mode,static_cast<char*>(snapshot)+0x20,4);
 if(mode>3)return false;
 if(!allowGeneration&&(mode==1||mode==2||(allModes&&mode==3))) {
  const unsigned zero=0,one=1;
  std::memcpy(static_cast<char*>(snapshot)+0x20,&zero,4);
  std::memcpy(static_cast<char*>(snapshot),&zero,4);
  std::memcpy(static_cast<char*>(snapshot)+4,&zero,4);
  std::memcpy(static_cast<char*>(snapshot)+8,&one,4);
 }
 return true;
}
inline bool StackFixedOff(void* snapshot,bool allowGeneration,bool allModes=false) {
 ULONG_PTR low=0,high=0;GetCurrentThreadStackLimits(&low,&high);
 auto p=reinterpret_cast<ULONG_PTR>(snapshot);
 if(p<low||p>high||high-p<0x58||!Writable(snapshot,0x58))return false;
 __try{return FixedOffPolicy(snapshot,allowGeneration,allModes);}__except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
__declspec(noinline) inline bool WrappedValidate(void* context,void* snapshot,void* third,void* fourth) {
 const void* caller=_ReturnAddress();
 const unsigned revision=ui_dynamic_snapshot==snapshot?ui_dynamic_revision:ui_revision.load();
 std::array<unsigned char,0x58> observed{};bool captured=false;
 if(enabled.load(std::memory_order_acquire)) {
  const bool off=generation_off.load(std::memory_order_relaxed);
  const bool allowGeneration=fixed_enabled.load(std::memory_order_relaxed)&&!off;
  if(reinterpret_cast<ULONG_PTR>(caller)!=reinterpret_cast<ULONG_PTR>(owner)+kValidateReturn||!StackFixedOff(snapshot,allowGeneration,off)) {
   enabled.store(false,std::memory_order_release);invalid.store(true);ui_frame.store(0);
  } else {
   // Preserve the input snapshot without retaining the caller's stack pointer.
   std::memcpy(observed.data(),snapshot,observed.size());captured=true;
  }
 }
 ui_dynamic_snapshot=nullptr;
 const bool accepted=original_validate(context,snapshot,third,fourth);
 if(captured) {
  unsigned mode=0,total=0;
  std::memcpy(&mode,observed.data()+0x20,4);std::memcpy(&total,observed.data()+8,4);
  // Native Off deliberately returns false. Other rejected frames are not an
  // accepted multiplier observation, even if their input requested several frames.
  if(accepted||(mode==0&&total==1))PublishUiFrame(observed.data(),revision);
  else if(revision==ui_revision.load())ui_frame.store(0);
 }
 return accepted;
}
inline std::string HashHandle(HANDLE f) {
 if(f==INVALID_HANDLE_VALUE)return {};
 BCRYPT_ALG_HANDLE alg=nullptr;BCRYPT_HASH_HANDLE hash=nullptr;std::string result;
 if(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0&&BCryptCreateHash(alg,&hash,nullptr,0,nullptr,0,0)>=0){
  std::array<unsigned char,65536> buffer{};DWORD n=0;bool ok=true;
  for(;;){if(!ReadFile(f,buffer.data(),static_cast<DWORD>(buffer.size()),&n,nullptr)){ok=false;break;}if(!n)break;if(BCryptHashData(hash,buffer.data(),n,0)<0){ok=false;break;}}
  unsigned char digest[32];if(ok&&BCryptFinishHash(hash,digest,32,0)>=0){const char* hex="0123456789ABCDEF";for(auto c:digest){result+=hex[c>>4];result+=hex[c&15];}}
 }
 if(hash)BCryptDestroyHash(hash);if(alg)BCryptCloseAlgorithmProvider(alg,0);return result;
}
inline std::string HashFile(const wchar_t* path) {
 HANDLE f=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
 auto result=HashHandle(f);if(f!=INVALID_HANDLE_VALUE)CloseHandle(f);return result;
}
struct Bytes {unsigned rva;const unsigned char* bytes;size_t size;};
// Generated from the SHA-pinned file. Entry + native caller + cap consumption +
// snapshot copy + Evaluate forwarding anchors all must still match loaded code.
#include "dynamiclive_anchors.inc"
inline bool Anchors(HMODULE module) {
 __try{
  auto base=reinterpret_cast<const unsigned char*>(module);
  auto dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
  if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<=0||dos->e_lfanew>0x1000)return false;
  auto nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(base+dos->e_lfanew);
  if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC)return false;
  if(nt->OptionalHeader.SizeOfImage<0x79da0)return false;
  if(*reinterpret_cast<const ULONG_PTR*>(base+0x77858)!=reinterpret_cast<ULONG_PTR>(base)+0x40830||
     *reinterpret_cast<const ULONG_PTR*>(base+0x77988)!=reinterpret_cast<ULONG_PTR>(base)+0x40870||
     *reinterpret_cast<const ULONG_PTR*>(base+0x79d98)!=reinterpret_cast<ULONG_PTR>(base)+0x4d5c0)return false;
  for(const auto& a:anchors){if(a.rva+a.size>nt->OptionalHeader.SizeOfImage||std::memcmp(base+a.rva,a.bytes,a.size))return false;}
  return true;
 }__except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
inline bool IsOurLiveThread(HANDLE h) {
 return h&&GetProcessIdOfThread(h)==GetCurrentProcessId()&&WaitForSingleObject(h,0)==WAIT_TIMEOUT;
}
inline bool Threads(std::vector<HANDLE>& list) {
 // Capacity is reserved BEFORE any thread is suspended. Never allocate/log here.
 HANDLE snap=CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD,0);if(snap==INVALID_HANDLE_VALUE)return false;
 THREADENTRY32 entry{};entry.dwSize=sizeof entry;bool ok=Thread32First(snap,&entry)!=FALSE;
 if(ok)do{if(entry.th32OwnerProcessID!=GetCurrentProcessId()||entry.th32ThreadID==GetCurrentThreadId())continue;
  bool known=false;for(auto h:list)if(GetThreadId(h)==entry.th32ThreadID){known=true;break;}if(known)continue;
  if(list.size()==list.capacity()){ok=false;break;}
  HANDLE h=OpenThread(THREAD_SUSPEND_RESUME|THREAD_GET_CONTEXT|THREAD_SET_CONTEXT|SYNCHRONIZE|THREAD_QUERY_LIMITED_INFORMATION,FALSE,entry.th32ThreadID);
  if(!h){ok=false;break;}
  // TID can be reused after the snapshot: validate the held object's owner.
  if(!IsOurLiveThread(h)){CloseHandle(h);ok=false;break;}
  list.push_back(h);
 }while(Thread32Next(snap,&entry));
 if(ok&&GetLastError()!=ERROR_NO_MORE_FILES)ok=false;CloseHandle(snap);return ok;
}
// Injectable transaction boundary for deterministic failure tests. Production uses
// only these Detours defaults; no untrusted callback is accepted by activation.
struct TransactionOps {
 LONG (WINAPI* begin)()=DetourTransactionBegin;
 LONG (WINAPI* update)(HANDLE)=DetourUpdateThread;
 LONG (WINAPI* attach)(PVOID*,PVOID)=DetourAttach;
 LONG (WINAPI* commit)()=DetourTransactionCommit;
 LONG (WINAPI* abort)()=DetourTransactionAbort;
};
inline bool Attach(void** target,void* replacement,const std::vector<HANDLE>& threads,
                   const TransactionOps& ops={}) {
 if(ops.begin()!=NO_ERROR)return false;
 for(auto thread:threads)if(ops.update(thread)!=NO_ERROR){ops.abort();return false;}
 if(ops.update(GetCurrentThread())!=NO_ERROR){ops.abort();return false;}
 if(ops.attach(target,replacement)!=NO_ERROR){ops.abort();return false;}
 return ops.commit()==NO_ERROR; // Detours ends/rolls back a failed commit itself.
}
inline bool AttachStable(void** target,void* replacement,HMODULE module,std::vector<HANDLE>& threads,
 const TransactionOps& ops={},bool(*enumerate)(std::vector<HANDLE>&)=Threads,bool(*validate)(HMODULE)=Anchors,void** secondTarget=nullptr,void* secondReplacement=nullptr) {
 if(!enumerate(threads)||ops.begin()!=NO_ERROR)return false;
 if(ops.update(GetCurrentThread())!=NO_ERROR){ops.abort();return false;}
 size_t enrolled=0;bool stable=false;
 for(unsigned round=0;round<4;++round){
  while(enrolled<threads.size())if(ops.update(threads[enrolled++])!=NO_ERROR){ops.abort();return false;}
  const size_t before=threads.size();
  if(!enumerate(threads)){ops.abort();return false;}
  if(threads.size()==before){stable=true;break;}
 }
 if(!stable||!validate(module)||ops.attach(target,replacement)!=NO_ERROR){ops.abort();return false;}
 if(secondTarget&&ops.attach(secondTarget,secondReplacement)!=NO_ERROR){ops.abort();return false;}
 return ops.commit()==NO_ERROR;
}
// Called from normal Present or manual overlay retry, outside DllMain. Never loads Streamline/NVAPI.
inline bool TryInstall(const std::atomic_bool& addon,const std::atomic_bool& dynamic) {
 if((!addon.load()&&fixed_enabled.load()&&!observation_enabled.load())||!dynamic.load()){status="Enable addon and Dynamic first; native behavior unchanged";return false;}
 if(installed.load())return !invalid.load();
 if(installing.test_and_set())return false;
 struct Reset{~Reset(){installing.clear();}} reset;
 if(installed.load())return !invalid.load(); // another installer may have finished before our guard
 HMODULE module=nullptr; GetModuleHandleExW(0,L"sl.dlss_g.dll",&module);
 struct Release{HMODULE m;~Release(){if(m)FreeLibrary(m);}} release{module};
 if(!module){status="Unavailable: sl.dlss_g.dll not mapped; native behavior unchanged";return false;}
 wchar_t path[32768];DWORD n=GetModuleFileNameW(module,path,32768);
 HANDLE file=n&&n<32768?CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr):INVALID_HANDLE_VALUE;
 struct FileRelease{HANDLE h;~FileRelease(){if(h!=INVALID_HANDLE_VALUE)CloseHandle(h);}} file_release{file};
 if(!n||n>=32768||HashHandle(file)!=kHash||!Anchors(module)){status="Unsupported file hash or loaded code; native behavior unchanged";return false;}
 // Retain cached callbacks for process lifetime; no hot detach/reinstall races.
 HMODULE pinned=nullptr;
 if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(module),&pinned)||
    !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&Wrapped),&pinned)){
  status="Cannot retain adapter modules; native behavior unchanged";return false;
 }
 std::vector<HANDLE> threads;threads.reserve(4096);bool ok;
 owner=module;original=reinterpret_cast<NativeFn>(reinterpret_cast<char*>(module)+kEntry);
 original_validate=reinterpret_cast<ValidateFn>(reinterpret_cast<char*>(module)+kValidateEntry);
 addon_enabled=&addon;dynamic_enabled=&dynamic;
 ok=AttachStable(reinterpret_cast<void**>(&original),reinterpret_cast<void*>(&Wrapped),module,threads,{},Threads,Anchors,reinterpret_cast<void**>(&original_validate),reinterpret_cast<void*>(&WrappedValidate));
 for(auto h:threads)CloseHandle(h);
 if(!ok){original_validate=nullptr;original=nullptr;owner=nullptr;addon_enabled=nullptr;dynamic_enabled=nullptr;status="Detour transaction refused; native behavior unchanged";return false;}
 installed.store(true);enabled.store(true,std::memory_order_release);status="Armed; waiting for a matching native Dynamic frame";return true;
}
// Normal Present callback only, never loader callbacks. One installation attempt,
// at most 60 module checks spaced one second apart; manual retry remains available.
inline std::atomic<unsigned> auto_checks{0};
inline std::atomic<ULONGLONG> next_auto_check{0};
inline std::atomic_bool auto_finished{false};
inline void AutoInstall(const std::atomic_bool& addon,const std::atomic_bool& dynamic) {
 if(installed.load()||auto_finished.load()||(!addon.load()&&fixed_enabled.load()&&!observation_enabled.load())||!dynamic.load())return;
 auto now=GetTickCount64(),due=next_auto_check.load();
 if(now<due||!next_auto_check.compare_exchange_strong(due,now+1000))return;
 if(GetModuleHandleW(L"sl.dlss_g.dll")){
  if(!auto_finished.exchange(true))TryInstall(addon,dynamic);
 }else if(auto_checks.fetch_add(1)+1>=60){
  auto_finished.store(true);status="Streamline not found in bounded startup checks; use manual retry";
 }
}
} // namespace mfgunlock::dynamiclive
