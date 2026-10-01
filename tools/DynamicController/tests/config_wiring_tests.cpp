// Real companion LoadConfig/SaveRequest using self-built ReShade configuration API.
// No ReShade, game, Streamline or NVIDIA DLL is loaded or executed.
#include "../addon.cpp"
#include <map>
#include <iostream>
#include <stdexcept>
std::map<std::pair<std::string,std::string>,std::string> config;
unsigned writes=0, checks=0;
std::vector<std::string> logs;
extern "C" __declspec(dllexport) void ReShadeLogMessage(void*,int,const char* message){logs.emplace_back(message);}
extern "C" __declspec(dllexport) bool ReShadeGetConfigValue(void*,reshade::api::effect_runtime*,const char* section,const char* key,char* value,size_t* size) {
 auto found=config.find({section,key});if(found==config.end())return false;
 size_t required=found->second.size()+1;
 if(value){if(*size<required){*size=required;return false;}memcpy(value,found->second.c_str(),required);}
 *size=required;return true;
}
extern "C" __declspec(dllexport) void ReShadeSetConfigValue(void*,reshade::api::effect_runtime*,const char* section,const char* key,const char* value) {config[{section,key}]=value;++writes;}
void Check(bool value,const char* name){if(!value)throw std::runtime_error(name);++checks;std::cout<<"PASS "<<name<<'\n';}
unsigned retries=0;
bool FakeInstall(const std::atomic_bool&,const std::atomic_bool&){++retries;return false;}
int main(){
 reshade::internal::get_reshade_module_handle(GetModuleHandleW(nullptr));
 LoadConfig();Check(live::requested==4&&user_enabled,"fresh companion default4 enabled");
 config[{"RenoDX.MFGUnlock","DynamicMaxMultiplier"}]="2";
 LoadConfig();Check(live::requested==4,"retired static key never read");
 config[{"RenoDX.MFGUnlock",live::kLegacyConfigKey}]="6";
 LoadConfig();Check(live::requested==6&&config[{kSection,live::kConfigKey}]=="6","legacy live6 migrated to companion namespace");
 Check(config[{"RenoDX.MFGUnlock",live::kLegacyConfigKey}]=="6","migration never changes main addon config");
 auto previous=writes;config[{"RenoDX.MFGUnlock",live::kLegacyConfigKey}]="2";
 LoadConfig();Check(live::requested==6&&writes==previous,"own key prevents repeated migration");
 config.erase({kSection,live::kConfigKey});config[{kSection,live::kLegacyConfigKey}]="0";
 LoadConfig();Check(live::requested==live::kNativeBound&&config[{kSection,live::kConfigKey}]=="-1","old companion0 migrates to native bound, never off");
 Check(config[{kSection,live::kLegacyConfigKey}]=="0","legacy key retained unchanged");
 config.erase({kSection,live::kConfigKey});config.erase({kSection,live::kLegacyConfigKey});config[{"RenoDX.MFGUnlock",live::kLegacyConfigKey}]="0";
 LoadConfig();Check(live::requested==live::kNativeBound,"old main addon0 migrates safely");
 SaveRequest(0);live::requested=4;LoadConfig();Check(live::requested==0,"new off0 persists via real API wiring");
 SaveRequest(6);live::requested=4;LoadConfig();Check(live::requested==6,"saved6 survives simulated next load");
 config[{kSection,live::kConfigKey}]="broken";LoadConfig();Check(live::requested==4,"malformed own key defaults4 not legacy2");
 config[{kSection,live::kConfigKey}]=std::string("6\0" "2",3);LoadConfig();Check(live::requested==4,"multiple values rejected");
 config[{kSection,live::kConfigKey}]=std::string(200,'6');LoadConfig();Check(live::requested==4,"oversized config rejected");
 config[{kSection,"Enabled"}]="0";LoadConfig();Check(!user_enabled,"own disable retained");
 config[{kSection,"FixedFrameGenerationEnabled"}]="0";LoadConfig();
 Check(!live::fixed_enabled&&!user_enabled,"ordinary Off persists independently of disabled Dynamic checkbox");
 SaveRequest(6);Check(!live::fixed_enabled,"Dynamic choice never enables ordinary generation");
 config[{kSection,"FixedFrameGenerationEnabled"}]="1";LoadConfig();Check(live::fixed_enabled&&live::requested==6&&!user_enabled,"ordinary resume leaves Dynamic choice and toggle intact");
 int fixedMultiplier=3;const int dynamicChoice=live::requested;
 live::installed=false;live::enabled=false;live::fixed_enabled=true;
 auto offWrites=writes;
 Check(!SaveFixedChoice(0,&fixedMultiplier)&&live::fixed_enabled&&writes==offWrites,"unavailable hook rejects Off without changing stored choice");
 live::installed=true;live::enabled=true;live::invalid=true;
 Check(!SaveFixedChoice(0,&fixedMultiplier)&&live::fixed_enabled&&writes==offWrites,"guard failure rejects Off without pretending success");
 live::invalid=false;
 Check(!SaveFixedChoice(0,&fixedMultiplier)&&fixedMultiplier==3&&!live::fixed_enabled&&live::requested==dynamicChoice,"ordinary Off saves only independent flag and preserves both multiplier choices");
 LoadConfig();Check(!live::fixed_enabled,"ordinary Off survives companion load");
 Check(SaveFixedChoice(3,&fixedMultiplier)&&fixedMultiplier==3&&live::fixed_enabled&&live::requested==dynamicChoice,"ordinary resume preserves Dynamic choice");
 Check(SaveFixedChoice(1,&fixedMultiplier)&&fixedMultiplier==0,"ordinary follow-game retains original multiplier0 meaning");
 SaveRequest(6);const auto logged=logs.size();LogFrameObservation();Check(logs.size()==logged,"no new native frame never claims observation");
 live::observation=(1ull<<63)|6|(6ull<<8)|(6ull<<16)|(4ull<<24);live::observed_frames.fetch_add(1);
 LogFrameObservation();Check(logs.size()==logged+1,"native observation logs separately from saved request");
 LogFrameObservation();Check(logs.size()==logged+1,"native observation is bounded once per selection");
 alignas(8) unsigned char native[0x58]{};
 unsigned mode=0,total=1;memcpy(native+0x20,&mode,4);memcpy(native+8,&total,4);
 live::PublishUiFrame(native,live::ui_revision.load());Check(live::UiFrameStatus()==1,"native off snapshot reports native frames");
 const auto oldRevision=live::ui_revision.load();live::UiChoiceChanged();
 live::PublishUiFrame(native,live::ui_revision.load());Check(live::UiFrameStatus()==0,"in-progress choice cannot acknowledge old request");
 live::UiChoiceCommitted();live::PublishUiFrame(native,oldRevision);Check(live::UiFrameStatus()==0,"late old frame cannot acknowledge newer choice");
 for(unsigned testMode=1;testMode<=3;++testMode)for(unsigned testTotal=2;testTotal<=6;++testTotal){
  memcpy(native+0x20,&testMode,4);memcpy(native+8,&testTotal,4);live::PublishUiFrame(native,live::ui_revision.load());
  Check(live::UiFrameStatus()==testTotal,"native mode reports observed total, not requested multiplier");
 }
 unsigned enabledMode=3,one=1;memcpy(native+0x20,&enabledMode,4);memcpy(native+8,&one,4);live::PublishUiFrame(native,live::ui_revision.load());
 Check(live::UiFrameStatus()==7,"enabled Dynamic one-frame result is not Off");
 live::enabled=true;live::invalid=true;Check(GetWuWaNativeFrameStatusV1()==0,"guard failure suppresses previous valid frame");live::invalid=false;
 live::enabled=false;Check(GetWuWaNativeFrameStatusV1()==0,"inactive hook never claims native observation");
 Check(live::UiFrameStatus(GetTickCount()+2100)==0,"stale frame expires during pause or loss of new frames");
 unsigned badMode=0,badTotal=3;memcpy(native+0x20,&badMode,4);memcpy(native+8,&badTotal,4);live::PublishUiFrame(native,live::ui_revision.load());
 Check(live::UiFrameStatus()==0,"inconsistent Off snapshot cannot display a stale multiplier");
 live::auto_finished=true;user_enabled=true;live::installed=false;live::invalid=false;
 RetryForUserChoice(FakeInstall);Check(retries==1&&live::auto_finished,"explicit choice retries exhausted startup without restarting automatic polling");
 live::invalid=true;RetryForUserChoice(FakeInstall);Check(retries==1&&live::invalid,"explicit choice never clears native guard failure");
 live::invalid=false;user_enabled=false;live::fixed_enabled=true;RetryForUserChoice(FakeInstall);Check(retries==1,"disabled features do not retry");
 live::fixed_enabled=false;RetryForUserChoice(FakeInstall);Check(retries==2,"ordinary Off can retry independently of Dynamic toggle");
 live::installed=true;RetryForUserChoice(FakeInstall);Check(retries==2,"installed hooks are not reinstalled by choices");
 live::installed=false;live::invalid=false;
 SaveRequest(6);ConfigureFrameMode(2,true);
 Check(user_enabled&&!live::fixed_enabled&&live::requested==6,"select Dynamic enables its path without overwriting fixed Off or saved6");
 SaveRequest(0);ConfigureFrameMode(2,false);
 Check(live::requested==0,"passive redraw preserves Dynamic Off");
 ConfigureFrameMode(2,true);
 Check(live::requested==6,"reselect Dynamic resumes last enabled6");
 live::installed=true;live::enabled=true;live::invalid=false;
 int savedFixed=5;SaveFixedChoice(0,&savedFixed);ConfigureFrameMode(2,true);
 Check(!live::fixed_enabled,"select Dynamic never overwrites independent fixed Off");
 ConfigureFrameMode(1,false);
 Check(!user_enabled&&!live::fixed_enabled&&live::requested==6,"passive fixed load preserves fixed Off and Dynamic choice");
 ConfigureFrameMode(1,true);
 Check(!user_enabled&&live::fixed_enabled&&live::requested==6,"select fixed enables fixed without changing Dynamic choice");
 SaveFixedChoice(0,&savedFixed);ConfigureFrameMode(0,true);
 Check(!user_enabled&&live::fixed_enabled&&live::observation_enabled,"game default withdraws custom Off/cap but keeps observation");
 config[{"RenoDX.MFGUnlock","WuWaFrameGenerationModeV1"}]="2";
 SaveRequest(0);LoadConfig();
 Check(frame_mode==2&&user_enabled&&live::requested==0,"persisted Dynamic Off survives next startup");
 ConfigureFrameMode(2,true);
 Check(live::requested==6,"saved last enabled Dynamic cap survives startup");
 config[{"RenoDX.MFGUnlock","WuWaFrameGenerationModeV1"}]="1";
 SaveFixedChoice(0,&savedFixed);LoadConfig();
 Check(frame_mode==1&&!user_enabled&&!live::fixed_enabled,"fixed Off survives next startup");
 config[{"RenoDX.MFGUnlock","WuWaFrameGenerationModeV1"}]="0";LoadConfig();
 Check(frame_mode==0&&!user_enabled&&live::fixed_enabled,"game default startup ignores obsolete feature switches");
 const auto beforeMode=frame_mode.load();const auto beforeCap=live::requested.load();
 ConfigureFrameMode(99,true);
 Check(frame_mode==beforeMode&&live::requested==beforeCap,"invalid mode does not modify persistent choices");
 Check(GetModuleHandleW(L"sl.dlss_g.dll")==nullptr&&GetModuleHandleW(L"nvapi64.dll")==nullptr,"no real runtime or driver loaded");
 std::cout<<checks<<"/"<<checks<<" companion API configuration tests passed\n";
}
