// Real inert ImGui widgets; no game, ReShade, window or OS input calls.
#include <deps/imgui/imgui_internal.h>
#include "ui_zh.hpp"
#include <iostream>
#include <stdexcept>
unsigned checks=0;
void Check(bool ok,const char* name){if(!ok)throw std::runtime_error(name);++checks;}
int main(){try{
 for(const auto& pair: {std::pair{"Multiplier","倍率"},{"Backend","后端"},{"Diagnostics","诊断"},{"Recommended","推荐"},{"Balanced (recommended)","均衡（推荐）"},{"GitHub repository","GitHub 仓库"},{"Hover a setting to see what it does.","将鼠标悬停在选项上可查看说明。"},{"Ready - enable FG in the game","已就绪，请在游戏内开启帧生成"},{"Waiting for the game's DLSS options.","等待游戏提交 DLSS 参数。"},{"Choose a stable quality profile. Changes marked for restart are saved for the next launch.","请选择稳定的画质配置。标注需要重启的修改将在下次启动游戏时生效。"},{"NVIDIA Reflex controls the timing of real frames. Generated FPS does not replace a healthy base frame rate.","NVIDIA Reflex 控制实际渲染帧的时序。帧生成后的高帧率不能弥补过低的基础帧率。"}}) {
  Check(std::string(wuwa_ui::Translate(pair.first))==pair.second,"screenshot phrase not fully translated");
 }
 Check(wuwa_ui::Label("Diagnostics###Diagnostics")=="诊断###Diagnostics","explicit ID changed");
 Check(wuwa_ui::Label("Diagnostics##debug")=="诊断###Diagnostics##debug","implicit ID not preserved");
 Check(wuwa_ui::Label("##frame_mode")=="##frame_mode","hidden ID changed");
 auto* ctx=ImGui::CreateContext();auto& io=ImGui::GetIO();io.IniFilename=nullptr;io.DisplaySize=ImVec2(1000,800);io.DeltaTime=1.f/60;
 unsigned char* pixels;int x,y;io.Fonts->GetTexDataAsRGBA32(&pixels,&x,&y);
 ImGui::NewFrame();ImGui::SetNextWindowSize(ImVec2(800,600));ImGui::Begin("inert translation test");
 auto id=ImGui::GetID("###GitHub repository");wuwa_ui::SmallButton("GitHub repository");Check(ctx->LastItemData.ID==id,"SmallButton canonical translation ID changed");
 id=ImGui::GetID("Diagnostics###fixed_tab");wuwa_ui::Selectable("Diagnostics###fixed_tab",true);Check(ctx->LastItemData.ID==id,"Selectable ID changed");
 id=ImGui::GetID("Advanced compatibility controls###compat");wuwa_ui::TreeNodeEx("Advanced compatibility controls###compat",ImGuiTreeNodeFlags_NoTreePushOnOpen);Check(ctx->LastItemData.ID==id,"expander ID changed");
 id=ImGui::GetID("##mode");if(wuwa_ui::BeginCombo("##mode","Game default"))ImGui::EndCombo();Check(ctx->LastItemData.ID==id,"combo ID changed");
 ImGui::End();ImGui::Render();ImGui::DestroyContext(ctx);
 std::cout<<checks<<" screenshot phrase / real widget identity checks passed\n";return 0;
}catch(const std::exception& e){std::cerr<<"FAIL "<<e.what()<<'\n';return 1;}}
