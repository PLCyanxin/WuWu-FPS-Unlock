// Production snippets exercised with inert upstream config/notification adapters.
#include <atomic>
#include <map>
#include <string>
#include <iostream>
#include <stdexcept>
constexpr const char* kConfigSection="RenoDX.MFGUnlock";
std::map<std::string,int> config;
std::atomic_bool g_enabled{true},g_configured_enabled{true};
int wuwa_frame_mode=0,wuwa_last_fixed=4;
namespace mfgunlock {
namespace forcepolicy { enum class FixedOverrideStatus {kNative,kPending}; }
namespace framecount {
std::atomic_uint g_force_multiplier{0},g_fixed_override_status{0};
std::atomic_bool g_dynamic_mfg_enabled{false};
int fixedNotify=0,dynamicNotify=0;
void NotifyFixedMultiplierChanged(unsigned){++fixedNotify;}
void NotifyDynamicModeChanged(){++dynamicNotify;}
}}
namespace reshade {
bool get_config_value(void*,const char*,const char* key,int& value){auto it=config.find(key);if(it==config.end())return false;value=it->second;return true;}
void set_config_value(void*,const char*,const char* key,int value){config[key]=value;}
}
#include "../frame_mode_apply.inc"
void LoadMode(){
#include "../frame_mode_load.inc"
}
unsigned checks=0;
void Check(bool ok,const char* name){if(!ok)throw std::runtime_error(name);++checks;std::cout<<"PASS "<<name<<'\n';}
int main(){using namespace mfgunlock::framecount;
LoadMode();Check(wuwa_frame_mode==0&&g_force_multiplier==0&&!g_dynamic_mfg_enabled,"fresh defaults to game settings without multiplier override");
config.clear();g_force_multiplier=3;g_dynamic_mfg_enabled=false;LoadMode();
Check(wuwa_frame_mode==1&&wuwa_last_fixed==3&&g_force_multiplier==3,"old fixed3 migrates without default4 replacement");
g_force_multiplier=6;ApplyWuWaFrameMode(2);
Check(wuwa_last_fixed==6&&g_force_multiplier==0&&g_dynamic_mfg_enabled,"Dynamic selection withdraws fixed override and remembers6");
LoadMode();Check(wuwa_frame_mode==2&&g_force_multiplier==0&&g_dynamic_mfg_enabled,"Dynamic remains selected on next launch");
ApplyWuWaFrameMode(1);Check(g_force_multiplier==6&&!g_dynamic_mfg_enabled,"fixed selection restores6 and withdraws Dynamic override");
ApplyWuWaFrameMode(0);Check(g_force_multiplier==0&&!g_dynamic_mfg_enabled&&wuwa_last_fixed==6,"game default removes both overrides without erasing remembered6");
LoadMode();Check(wuwa_frame_mode==0&&g_force_multiplier==0&&!g_dynamic_mfg_enabled,"game default persists despite stale legacy force value");
ApplyWuWaFrameMode(1);Check(g_force_multiplier==6,"return from game default restores fixed6");
Check(config["Enabled"]==1&&g_configured_enabled,"mode choice requests enabled addon");
Check(fixedNotify==4&&dynamicNotify==4,"each mode choice schedules both original option notification paths");
ApplyWuWaFrameMode(3);Check(wuwa_frame_mode==3&&g_force_multiplier==0&&!g_dynamic_mfg_enabled&&wuwa_last_fixed==6,"Off withdraws both overrides without erasing fixed6");
LoadMode();Check(wuwa_frame_mode==3&&g_force_multiplier==0&&!g_dynamic_mfg_enabled,"Off persists after restart");
ApplyWuWaFrameMode(1);Check(g_force_multiplier==6&&!g_dynamic_mfg_enabled,"return from Off restores fixed6");
auto before=config;ApplyWuWaFrameMode(99);Check(config==before&&g_force_multiplier==6,"invalid mode has no side effects");
config.clear();g_force_multiplier=5;g_dynamic_mfg_enabled=true;LoadMode();Check(wuwa_frame_mode==2&&wuwa_last_fixed==5,"legacy Dynamic wins while retaining old fixed5");
config.clear();g_force_multiplier=3;g_dynamic_mfg_enabled=true;g_enabled=false;LoadMode();Check(wuwa_frame_mode==0&&!g_dynamic_mfg_enabled,"disabled old master migrates to game default");
g_enabled=true;ApplyWuWaFrameMode(2);Check(g_configured_enabled&&config["Enabled"]==1,"explicit choice re-enables old disabled installation for next launch");
config["WuWaFrameGenerationModeV1"]=88;config["WuWaLastFixedMultiplier"]=99;g_force_multiplier=0;g_dynamic_mfg_enabled=false;LoadMode();Check(wuwa_frame_mode==0&&wuwa_last_fixed==4,"corrupt new config falls back to valid old semantics and fixed4");
config["WuWaFrameGenerationModeV1"]=1;config["FixedFrameGenerationEnabled"]=0;config["WuWaLastFixedMultiplier"]=5;LoadMode();
Check(wuwa_frame_mode==3&&wuwa_last_fixed==5&&config["WuWaFrameGenerationModeV1"]==3,"legacy fixed Off migrates to unified Off with saved5");
config["WuWaFrameGenerationModeV1"]=2;config["DynamicFrameGenerationChoiceV2"]=0;LoadMode();
Check(wuwa_frame_mode==3&&wuwa_last_fixed==5,"legacy Dynamic Off migrates to unified Off without erasing fixed5");
std::cout<<checks<<" main mode transition and persistence checks passed\n";
}
