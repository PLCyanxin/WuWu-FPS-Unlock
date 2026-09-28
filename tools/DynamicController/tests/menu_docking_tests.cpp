// SPDX-License-Identifier: MIT
#include "../menu_docking.hpp"
#include <deps/imgui/imgui_internal.h>
#include <iostream>
#include <stdexcept>
static int passed;
void Check(bool value,const char* name){if(!value)throw std::runtime_error(name);++passed;std::cout<<"PASS "<<name<<"\n";}
void Frame(float x,float y,bool down,bool shift){
 auto& io=ImGui::GetIO();io.AddMousePosEvent(x,y);io.AddMouseButtonEvent(0,down);io.AddKeyEvent(ImGuiMod_Shift,shift);
 ImGui::NewFrame();
 ImGui::SetNextWindowPos(ImVec2(350,100),ImGuiCond_Once);ImGui::SetNextWindowSize(ImVec2(300,250),ImGuiCond_Once);
 ImGui::Begin("Dock target");ImGui::TextUnformatted("target");ImGui::End();
 ImGui::SetNextWindowPos(ImVec2(30,100),ImGuiCond_Once);ImGui::SetNextWindowSize(ImVec2(200,180),ImGuiCond_Once);
 ImGui::Begin("MFG Unlock");ImGui::TextUnformatted("controls unchanged");ImGui::End();ImGui::Render();
}
void Drag(bool shift){
 auto* ctx=ImGui::CreateContext();auto& io=ImGui::GetIO();io.IniFilename=nullptr;io.DisplaySize=ImVec2(900,600);io.DeltaTime=1.0f/60;
 io.ConfigFlags|=ImGuiConfigFlags_DockingEnable;io.ConfigInputTrickleEventQueue=false;
 unsigned char* pixels;int width,height;io.Fonts->GetTexDataAsRGBA32(&pixels,&width,&height);
 wuwa::menu_docking::Configure([](const char*){});
 Frame(70,110,false,shift);Frame(70,110,false,shift);Frame(70,110,true,shift);
 for(int i=1;i<=12;++i)Frame(70+i*35.0f,110+i*8.0f,true,shift);
 auto* moving=ImGui::FindWindowByName("MFG Unlock");
 Check(moving&&moving->Pos.x>300,"title drag still moves window");
 Check(!(moving->Flags&ImGuiWindowFlags_NoDocking),"window docking remains enabled");
 Check(ctx->DragDropActive==shift,shift?"Shift title drag creates native docking payload":"ordinary title drag creates no docking payload");
 Frame(490,206,false,shift);Frame(490,206,false,shift);
 Check((moving->DockId!=0)==shift,shift?"Shift release retains native docking":"ordinary release remains floating");
 ImGui::DestroyContext(ctx);
}
int main(){try{
 struct FakeIo{bool ConfigDockingWithShift=false;};FakeIo io;int gets=0,logs=0;wuwa::menu_docking::Policy policy;
 auto get=[&]()->FakeIo&{++gets;return io;};auto log=[&](const char*){++logs;};
 Check(!policy.Apply("0.0",get,log)&&gets==0&&!io.ConfigDockingWithShift,"mismatched ABI never reads or mutates IO");
 policy.Apply(nullptr,get,log);policy.Apply("0.0",get,log);Check(logs==1&&gets==0,"unsupported ABI logs once");
 Check(policy.Apply(IMGUI_VERSION,get,log)&&gets==1&&io.ConfigDockingWithShift,"exact ABI enables Shift docking");
 Drag(false);Drag(true);std::cout<<"RESULT "<<passed<<" passed; actual ImGui frames, no ReShade/game/config writes\n";return 0;
 }catch(const std::exception& ex){std::cerr<<"FAIL "<<ex.what()<<"\n";return 1;}}
