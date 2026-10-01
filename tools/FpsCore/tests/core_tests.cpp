// SPDX-License-Identifier: MIT
// Includes the candidate's real receiver/IO implementation. Never calls its
// DllMain, Start, RunLogic, process scan, or any installed/game component.
#include "../dllmain.cpp"
#include <iostream>
#include <stdexcept>
#include <chrono>
#include <thread>
int checks=0;
void Check(bool value,const char* what){if(!value)throw std::runtime_error(what);++checks;std::cout<<"PASS "<<what<<"\n";}
int main(){try{
 using namespace wuwa::fps;
 Check(target.load()==0,"no target published before first valid command");
 int value=150;
 for(int n:{30,150,420})Check(ParseMessage("0,0,45.0,0,0,"+std::to_string(n),value)&&value==n,"valid launcher command range");
 for(auto invalid:{"0,0,45.0,0,0,29","0,0,45.0,0,0,421","0,0,45.0,0,0,150junk","0,0,nan,0,0,150","0,0,45.0,0,0,999999999999999","0,0,45.0,0,0,","0,0,45.0,0,150","0,0,45.0,0,0,150,200","x,0,45.0,0,0,150"}){value=170;Check(!ParseMessage(invalid,value)&&value==170,"malformed command leaves target unchanged");}
 Check(!ParseMessage(std::string(4096,'0'),value),"oversize command rejected");
 Check(!ParseMessage(std::string("0,0,45.0,0,0,150\0x",19),value),"embedded NUL rejected");
 auto pattern=ParsePattern("12 34 56");uint8_t tiny[2]={0x12,0x34};Check(!FindFirst(tiny,2,pattern),"short region cannot underflow");Check(!FindFirst(tiny,2,{}),"empty pattern rejected");
 uint8_t bytes[]{0,0x12,0x34,0x56,0x12,0x34,0x56};Check(FindFirst(bytes,sizeof bytes,pattern)==bytes+1,"first match order preserved");
 uintptr_t found=0;int reads=0;
 auto one=[&](uintptr_t p,float& f){++reads;f=p==5000?60.0f:0.0f;return true;};
 for(int i=0;i<3;++i)Check(FindSecond(6000,one,found)==SearchResult::Pending&&found==0,"one address cannot become second candidate on retries");
 Check(reads==3*4096,"candidate retries scan one bounded pass each");
 Check(FindSecond(6000,[](uintptr_t p,float& f){f=(p==5000||p==4000)?60.0f:0.0f;return true;},found)==SearchResult::Found&&found==4000,"second distinct candidate preserves descending order");
 Check(FindSecond(10,[](uintptr_t,float&){return false;},found)==SearchResult::Unreadable,"read failure stops candidate search");
 float stored=150;int writes=0;bool changed=false;
 auto read=[&](uintptr_t,float& f){f=stored;return true;};auto write=[&](uintptr_t,float f){stored=f;++writes;return true;};
 bool stable=true;for(int i=0;i<10000;++i)stable&=Maintain(0,150,read,write,changed)&&!changed;Check(stable,"unchanged maintenance skips store");
 Check(writes==0,"10000 stable checks perform zero writes");
 stored=60;Check(Maintain(0,150,read,write,changed)&&changed&&writes==1,"game reset repaired on next check");Check(Maintain(0,240,read,write,changed)&&changed&&writes==2,"new target written on next check");
 for(float bad:{0.0f,-1.0f,29.0f,421.0f,123456.75f,std::numeric_limits<float>::quiet_NaN(),std::numeric_limits<float>::infinity()}) {
  stored=bad;const int before=writes;Check(!Maintain(0,160,read,write,changed)&&!changed&&writes==before,"changed address semantics stops maintenance without a write");
 }
 stored=60;Check(!Maintain(0,999,read,write,changed)&&!changed&&writes==2,"invalid target cannot be written");
 std::atomic<int> concurrent{150};std::thread producer([&]{for(int i=0;i<100000;++i)concurrent.store(i%2?30:420,std::memory_order_relaxed);});
 bool valid=true;for(int i=0;i<100000;++i){int n=concurrent.load(std::memory_order_relaxed);valid&=n==150||n==30||n==420;}producer.join();Check(valid,"concurrent atomic target publication");
 // Exact production receiver with a private fixture pipe, not the product pipe.
 std::string name="\\\\.\\pipe\\WuWaFpsCoreFixture-"+std::to_string(GetCurrentProcessId());stop_event=CreateEventW(nullptr,TRUE,FALSE,nullptr);Check(stop_event!=nullptr,"fixture stop event");
 HANDLE receiver=CreateThread(nullptr,0,Receive,name.data(),0,nullptr);Check(receiver!=nullptr,"fixture receiver thread");
 auto connect=[&](){HANDLE client=INVALID_HANDLE_VALUE;for(int i=0;i<100&&client==INVALID_HANDLE_VALUE;++i){client=CreateFileA(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);if(client==INVALID_HANDLE_VALUE)Sleep(10);}Check(client!=INVALID_HANDLE_VALUE,"private pipe connection");return client;};
 auto client=connect();DWORD written=0;std::string good="0,0,45.0,0,0,240";Check(WriteFile(client,good.data(),DWORD(good.size()),&written,nullptr)!=FALSE,"real pipe valid message");
 for(int i=0;i<100&&target.load()!=240;++i)Sleep(5);Check(target.load()==240,"receiver publishes parsed target");
 std::string malformed="0,0,45.0,0,0,420junk";WriteFile(client,malformed.data(),DWORD(malformed.size()),&written,nullptr);Sleep(20);Check(target.load()==240,"real receiver rejects trailing junk without changing target");
 for(size_t length:{129u,4095u,4096u}){std::string huge(length,'x');huge.replace(length-good.size(),good.size(),"0,0,45.0,0,0,420");WriteFile(client,huge.data(),DWORD(huge.size()),&written,nullptr);CloseHandle(client);Sleep(30);Check(target.load()==240,"oversize message continuation cannot become valid command");client=connect();}
 SetEvent(stop_event);Check(WaitForSingleObject(receiver,2000)==WAIT_OBJECT_0,"stop cancels blocked real pipe read");CloseHandle(client);CloseHandle(receiver);CloseHandle(stop_event);
 stop_event=CreateEventW(nullptr,TRUE,FALSE,nullptr);receiver=CreateThread(nullptr,0,Receive,name.data(),0,nullptr);Sleep(30);SetEvent(stop_event);Check(WaitForSingleObject(receiver,2000)==WAIT_OBJECT_0,"stop cancels blocked real pipe connect");CloseHandle(receiver);CloseHandle(stop_event);stop_event=nullptr;
 auto actualPattern=ParsePattern(kSignature);Check(actualPattern.size()==154,"unchanged upstream 154-byte signature");
 std::vector<uint8_t> data(16*1024*1024,0xcc);for(size_t i=0;i<actualPattern.size();++i)data[32+i]=actualPattern[i].value;
 size_t compared=0;auto t=std::chrono::steady_clock::now();auto hit=FindFirst(data.data(),data.size(),actualPattern,&compared);auto early=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-t).count();
 // Upstream SafeCompareMultiple loop on owned fixture bytes; no production scan.
 size_t allCompared=0;std::vector<size_t> hits;t=std::chrono::steady_clock::now();
 for(size_t p=0;p<=data.size()-actualPattern.size();++p){++allCompared;bool match=true;
 for(size_t j=0;j<actualPattern.size();++j)if(!actualPattern[j].wildcard&&data[p+j]!=actualPattern[j].value){match=false;break;}
 if(match)hits.push_back(p);}
 auto full=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-t).count();
 Check(hit==data.data()+32&&hits.size()==1&&hits[0]==32&&compared==33,"early stop preserves original full-scan first result");
 Check(!FindFirst(reinterpret_cast<const uint8_t*>(actualPattern.data()),actualPattern.size()*sizeof(PatternByte),actualPattern),"parsed interleaved signature does not match itself");
 std::cout<<"BENCH fixture 16MiB original multiple-scan positions="<<allCompared<<" ms="<<full<<" candidate first positions="<<compared<<" ms="<<early<<"; stable checks writes 10000 -> 0; cadence unchanged51ms\n";
 std::cout<<checks<<" passed; no game/core DLL loaded or process scan executed\n";return 0;
 }catch(const std::exception& e){std::cerr<<"FAIL "<<e.what()<<"\n";return 1;}}
