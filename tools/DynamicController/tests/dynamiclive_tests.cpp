// Self-built fake native function only. Does not load NVIDIA, Streamline or a game.
#include "../dynamiclive.hpp"
#include <iostream>
#include <thread>
#include <stdexcept>
#include <fstream>
#include <filesystem>
namespace live=mfgunlock::dynamiclive;
struct Snapshot {alignas(8) unsigned char bytes[0x58]{};Snapshot(unsigned bound=3,unsigned mode=3){std::memcpy(bytes+0x44,&bound,4);std::memcpy(bytes+0x20,&mode,4);}unsigned cap()const{unsigned n;std::memcpy(&n,bytes+0x44,4);return n;}};
std::atomic<unsigned> calls{0};void* return_address=nullptr;
__declspec(noinline) void FakeNative(void* context,void* memory){
 if(!return_address)return_address=_ReturnAddress();
 auto frame=static_cast<Snapshot*>(memory);unsigned selected=(std::min)(4u,frame->cap());std::memcpy(frame->bytes,&selected,4);std::memcpy(frame->bytes+4,&selected,4);unsigned total=selected+1;std::memcpy(frame->bytes+8,&total,4);
 if(context)live::requested.store(*static_cast<int*>(context)); // simulate UI update DURING native call
 calls.fetch_add(1);
}
live::NativeFn volatile entry=&FakeNative; volatile unsigned completed=0;
__declspec(noinline) void Invoke(Snapshot& snapshot,void* context=nullptr){entry(context,&snapshot);completed=completed+1;}
unsigned checks=0;void Check(bool value,const char* name){if(!value)throw std::runtime_error(name);++checks;std::cout<<"PASS "<<name<<'\n';}
void* validatorReturn=nullptr;unsigned validateCalls=0;
__declspec(noinline) bool FakeValidate(void* ctx,void* memory,void* third,void* fourth) {
 if(!validatorReturn)validatorReturn=_ReturnAddress();++validateCalls;
 if(ctx!=third||third!=fourth)throw std::runtime_error("validator arguments changed");
 unsigned mode=0;std::memcpy(&mode,static_cast<char*>(memory)+0x20,4);return mode!=0;
}
live::ValidateFn volatile validatorEntry=&FakeValidate;
__declspec(noinline) bool InvokeValidate(Snapshot& frame) {bool result=validatorEntry(nullptr,&frame,nullptr,nullptr);completed=completed+1;return result;}
int fail_at=0,stage=0,aborts=0;
LONG WINAPI Step(){return ++stage==fail_at?ERROR_ACCESS_DENIED:NO_ERROR;}
LONG WINAPI Update(HANDLE){return Step();}LONG WINAPI Patch(PVOID*,PVOID){return Step();}LONG WINAPI Abort(){++aborts;return 0;}
int enumeration=0,scan_mode=0;
bool Scan(std::vector<HANDLE>& threads){++enumeration;if(scan_mode==1)return false;if(scan_mode==2)threads.push_back(GetCurrentThread());if(scan_mode==3&&enumeration==2)threads.push_back(GetCurrentThread());return true;}
bool Validate(HMODULE){return scan_mode!=4;}
int wmain(int argc,wchar_t** argv){
 std::cout.setf(std::ios::unitbuf);
 std::atomic_bool addon{true},dynamic{true};
 Check(live::requested.load()==4,"live request defaults to 4 without legacy configuration");
 Check(std::string_view(live::kConfigKey)=="DynamicFrameGenerationChoiceV2","persistent key distinct from retired static cap");
 Check(live::ParseConfig(0,"")==4,"first use without new key defaults to 4");
 Check(live::ParseConfig(2,"6")==4,"multi-value corrupt config falls back to 4");
 for(int value:{-1,0,2,3,4,5,6}){const auto saved=std::to_string(live::NormalizeConfig(value));Check(live::ParseConfig(1,saved)==value,"saved live cap round-trip including 0 and 6");}
 for(const char* bad:{"","1","7","-2","999999999999","4x","6,4","nan","+6","6.0"})Check(live::ParseConfig(1,bad)==4,"malformed live config uses safe 4 default");
 Check(live::ParseConfig(1," \t6 ")==6,"trimmed persisted value accepted");
 {Snapshot f(5);unsigned long long sample=0;Check(live::Policy(&f,live::ParseConfig(1,"6"),sample)&&f.cap()==5,"persisted 6 does not restore static 4 clamp");}

 for(int request:{4,5,6}){Snapshot f(5);unsigned long long sample=0;Check(live::Policy(&f,request,sample)&&f.cap()==static_cast<unsigned>(request-1),"native 6x bound permits live 4-5-6 without static clamp");}
 for(unsigned i=0;i<60;++i){live::next_auto_check=0;live::AutoInstall(addon,dynamic);}
 Check(live::auto_finished&&live::auto_checks==60&&!live::installed,"automatic missing-module checks stop after 60 attempts");
 live::AutoInstall(addon,dynamic);Check(live::auto_checks==60,"bounded discovery does not continuously retry");

 if(argc==2){
  Check(live::HashFile(argv[1])==live::kHash,"reference bytes exact SHA (never executed)");
  std::ifstream file(std::filesystem::path(argv[1]),std::ios::binary);std::vector<char> raw((std::istreambuf_iterator<char>(file)),{});
  auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(raw.data());auto nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(raw.data()+dos->e_lfanew);
  // Test-only data reconstruction of an already hash-verified file, not a loader.
  std::vector<unsigned char> image(nt->OptionalHeader.SizeOfImage);std::memcpy(image.data(),raw.data(),nt->OptionalHeader.SizeOfHeaders);
  auto section=IMAGE_FIRST_SECTION(nt);for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i)
   std::memcpy(image.data()+section[i].VirtualAddress,raw.data()+section[i].PointerToRawData,section[i].SizeOfRawData);
  auto module=reinterpret_cast<HMODULE>(image.data());
  const ULONG_PTR cb1=reinterpret_cast<ULONG_PTR>(image.data())+0x40830,cb2=reinterpret_cast<ULONG_PTR>(image.data())+0x40870;
  std::memcpy(image.data()+0x77858,&cb1,sizeof cb1);std::memcpy(image.data()+0x77988,&cb2,sizeof cb2);
  const ULONG_PTR producer=reinterpret_cast<ULONG_PTR>(image.data())+0x4d5c0;std::memcpy(image.data()+0x79d98,&producer,sizeof producer);
  Check(live::Anchors(module),"exact reference instruction anchors accepted as DATA");
  for(unsigned rva:{0x77858u,0x77988u,0x79d98u}){image[rva]^=1;Check(!live::Anchors(module),"redirected callback vtable rejected");image[rva]^=1;}
  for(auto anchor:live::anchors){image[anchor.rva]^=1;Check(!live::Anchors(module),"modified loaded chain anchor rejected");image[anchor.rva]^=1;}
 }else std::cout<<"Reference image DATA audit skipped (optional path not supplied).\n";
 addon=false;live::fixed_enabled=false;Check(!live::TryInstall(addon,dynamic)&&std::string_view(live::status.load()).find("not mapped")!=std::string_view::npos,"ordinary Off reaches install checks with Dynamic checkbox disabled");addon=true;live::fixed_enabled=true;
 Check(!live::TryInstall(addon,dynamic)&&!live::installed,"no Streamline module: no hook");
 Check(!live::Anchors(GetModuleHandleW(nullptr)),"unrelated executable fails image anchors");
 wchar_t own[MAX_PATH];GetModuleFileNameW(nullptr,own,MAX_PATH);Check(live::HashFile(own)!=live::kHash,"own fixture fails exact file hash");
 Check(live::IsOurLiveThread(GetCurrentThread()),"held current thread belongs to this process");
 Check(!live::IsOurLiveThread(INVALID_HANDLE_VALUE),"invalid thread handle rejected");
 std::wstring child=L"\""+std::wstring(own)+L"\"";STARTUPINFOW startup{};startup.cb=sizeof startup;PROCESS_INFORMATION pi{};
 Check(CreateProcessW(own,child.data(),nullptr,nullptr,FALSE,CREATE_SUSPENDED,nullptr,nullptr,&startup,&pi)!=FALSE,"create suspended self-built child fixture");
 Check(!live::IsOurLiveThread(pi.hThread),"held foreign process thread rejected");TerminateProcess(pi.hProcess,0);WaitForSingleObject(pi.hProcess,5000);CloseHandle(pi.hThread);CloseHandle(pi.hProcess);
 unsigned long long sample=0;
 for(int request:{-1,0,2,3,4,5,6}){Snapshot f;Check(live::Policy(&f,request,sample)&&f.cap()==live::Limit(3,request),"native bound clamps requested cap");}
 for(int bad:{-2,1,7,100}){Snapshot f;Check(!live::Policy(&f,bad,sample)&&f.cap()==3,"invalid request leaves bytes unchanged");}
 for(unsigned mode:{0u,1u,2u,4u}){Snapshot f(3,mode);Check(!live::Policy(&f,2,sample)&&f.cap()==3,"non Dynamic leaves bytes unchanged");}
 for(unsigned mode:{0u,1u,2u,4u}){Snapshot f(3,mode);Snapshot before=f;Check(!live::Policy(&f,0,sample)&&!live::SubmitOff(&f)&&std::memcmp(&f,&before,sizeof f)==0,"off choice never rewrites non Dynamic game mode");}
 Check(live::MigrateLegacy(1,"0")==-1&&live::ParseConfig(1,"0")==0,"legacy0 and new0 have distinct semantics");
 Snapshot zero(0);Check(!live::Policy(&zero,2,sample),"unknown zero native bound rejected");
 auto heap=new Snapshot;Check(!live::StackPolicy(heap,2,sample)&&heap->cap()==3,"heap snapshot rejected");delete heap;
 Snapshot frame;Check(live::StackPolicy(&frame,2,sample)&&frame.cap()==1,"current writable stack accepted");
 auto memory=VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);Check(live::Writable(memory,88),"committed writable range accepted");
 DWORD old;VirtualProtect(memory,4096,PAGE_READONLY,&old);Check(!live::Writable(memory,88),"read-only range rejected before write");VirtualProtect(memory,4096,PAGE_READWRITE|PAGE_GUARD,&old);Check(!live::Writable(memory,88),"guard page rejected without consuming guard");VirtualFree(memory,0,MEM_RELEASE);
 live::TransactionOps fake{Step,Update,Patch,Step,Abort};void* target=reinterpret_cast<void*>(&FakeNative);
 for(fail_at=1;fail_at<=4;++fail_at){stage=aborts=0;Check(!live::Attach(&target,reinterpret_cast<void*>(&live::Wrapped),{},fake),"transaction failure returned");Check(target==reinterpret_cast<void*>(&FakeNative),"failed transaction preserves original pointer");Check(aborts==((fail_at==2||fail_at==3)?1:0),"failed transaction cleanup exactly once");}
 fail_at=0;stage=aborts=0;Check(live::Attach(&target,reinterpret_cast<void*>(&live::Wrapped),{},fake)&&aborts==0,"successful transaction commits once");
 std::vector<HANDLE> cohort;cohort.reserve(16);
 for(scan_mode=0;scan_mode<=4;++scan_mode){cohort.clear();enumeration=stage=aborts=0;fail_at=0;
  bool result=live::AttachStable(&target,reinterpret_cast<void*>(&live::Wrapped),nullptr,cohort,fake,Scan,Validate);
  Check(result==(scan_mode==0||scan_mode==3),"bounded cohort discovery gate");
  if(scan_mode==2)Check(aborts==1&&enumeration==5,"nonconverging cohort aborts after four rounds");
  if(scan_mode==3)Check(enumeration==3,"newly appeared thread enlisted before stable commit");
 }
 cohort.clear();enumeration=stage=aborts=0;scan_mode=0;fail_at=2;
 Check(!live::AttachStable(&target,reinterpret_cast<void*>(&live::Wrapped),nullptr,cohort,fake,Scan,Validate)&&aborts==1,"thread enrollment failure aborts stable transaction");
 fail_at=0;
 // Independent ordinary Off via the real shared-validation wrapper and Detours.
 Snapshot validationInitial(3,1);InvokeValidate(validationInitial);
 live::owner=reinterpret_cast<HMODULE>(reinterpret_cast<ULONG_PTR>(validatorReturn)-live::kValidateReturn);
 live::original_validate=&FakeValidate;
 Check(live::Attach(reinterpret_cast<void**>(&live::original_validate),reinterpret_cast<void*>(&live::WrappedValidate),{}),"real Detours shared validator fixture");
 live::enabled=true;addon=false;
 for(bool allow:{false,true,false,true})for(unsigned mode:{0u,1u,2u,3u}) {
  live::fixed_enabled=allow;Snapshot f(3,mode);const Snapshot previous=f;unsigned before=validateCalls;
  const bool result=InvokeValidate(f);unsigned actual=99,total=99;std::memcpy(&actual,f.bytes+0x20,4);std::memcpy(&total,f.bytes+8,4);
  const bool off=!allow&&(mode==1||mode==2);
  Check(actual==(off?0:mode)&&result==(actual!=0)&&validateCalls==before+1,"shared validator mode isolation and exact original call/return");
  Check(off?total==1:std::memcmp(&f,&previous,sizeof f)==0,"ordinary Off total1; nativeOff/Dynamic bytes untouched");
 }
 Snapshot badFixed(3,4);Snapshot badBefore=badFixed;
 Check(!live::StackFixedOff(&badFixed,false)&&std::memcmp(&badFixed,&badBefore,sizeof badFixed)==0,"unknown ordinary mode fails before mutation");
 auto fixedHeap=new Snapshot(3,1);Check(!live::StackFixedOff(fixedHeap,false),"shared validator rejects nonstack snapshot");delete fixedHeap;
 live::fixed_enabled=true;addon=true;
 // Both hooks attach in one transaction; second failure aborts the first.
 cohort.clear();enumeration=stage=aborts=0;scan_mode=0;fail_at=4;
 void* second=reinterpret_cast<void*>(&FakeValidate);
 Check(!live::AttachStable(&target,reinterpret_cast<void*>(&live::Wrapped),nullptr,cohort,fake,Scan,Validate,&second,reinterpret_cast<void*>(&live::WrappedValidate))&&aborts==1,"second hook failure aborts pair atomically");fail_at=0;
 Snapshot initial;Invoke(initial);live::owner=reinterpret_cast<HMODULE>(reinterpret_cast<ULONG_PTR>(return_address)-live::kReturn);
 live::original=&FakeNative;live::addon_enabled=&addon;live::dynamic_enabled=&dynamic;
 Check(live::Attach(reinterpret_cast<void**>(&live::original),reinterpret_cast<void*>(&live::Wrapped),{}),"real Detours on self-built native fixture");live::enabled=true;
 for(int requested:{4,0,2,0,3,4,6,0,-1,6}){Snapshot f;live::requested=requested;unsigned before=calls;Invoke(f);Check(f.cap()==live::Limit(3,requested)&&calls==before+1,"fresh frame change 4-2-3-4-6-0, one native call");Snapshot queued=f;Check(queued.cap()==f.cap(),"copied frame retains same RSYNC/NGX cap model");
 unsigned mode=0,generated=0,total=0;std::memcpy(&mode,queued.bytes+0x20,4);std::memcpy(&generated,queued.bytes,4);std::memcpy(&total,queued.bytes+8,4);
 Check(requested==0 ? mode==0&&generated==0&&total==1 : mode==3,"off submits native mode0/count0/total1 and later frame restores Dynamic");
 // Consumer model matches the pinned branches: mode0 skips Evaluate entirely.
 Check((mode!=0)==(requested!=0),"queued off snapshot takes native Evaluate bypass");}
 Snapshot schedulerLimited(5);live::requested=6;Invoke(schedulerLimited);
 Check(((live::observation.load()>>16)&255)==6&&((live::observation.load()>>24)&255)==5,"requested6 cap and native-selected5 are reported separately");
 live::requested=2;Snapshot lowered(5);Invoke(lowered);
 Check(((live::observation.load()>>24)&255)==2&&lowered.cap()==1,"lower maximum is consumed on next selector invocation without options refresh");
 live::requested=0;Snapshot disabledNext(5);Invoke(disabledNext);
 Check(((live::observation.load()>>24)&255)==1,"Off submits native-only total1 on next selector invocation");
 Snapshot changingOff;int nextOn=6;live::requested=0;Invoke(changingOff,&nextOn);unsigned offMode=99;std::memcpy(&offMode,changingOff.bytes+0x20,4);
 Check(offMode==0&&live::requested==6,"off decision is sampled once despite mid-call UI resume");
 Snapshot resumed;Invoke(resumed);unsigned resumedMode=0;std::memcpy(&resumedMode,resumed.bytes+0x20,4);Check(resumedMode==3,"next fresh frame resumes after off");
 Snapshot changing;int next=4;live::requested=2;Invoke(changing,&next);Check(changing.cap()==1&&live::requested==4,"mid-frame UI change deferred until next frame");
 Snapshot after;Invoke(after);Check(after.cap()==3,"next frame takes new request");
 addon=false;live::requested=2;Snapshot off;Invoke(off);Check(off.cap()==3,"addon off bypasses live cap");addon=true;dynamic=false;Snapshot fixed;Invoke(fixed);Check(fixed.cap()==3,"Dynamic toggle off bypasses live cap");dynamic=true;
 Snapshot malformed(3,1);Invoke(malformed);Check(live::invalid&&!live::enabled&&malformed.cap()==3,"invalid frame disables live adapter and calls original");Snapshot safe;Invoke(safe);Check(safe.cap()==3,"disabled adapter remains passthrough");
 // No unload: production pins modules; fixture exits with its trampoline alive.
 std::cout<<checks<<"/"<<checks<<" native runtime-adapter fixture assertions passed. Not game validation.\n";
}
