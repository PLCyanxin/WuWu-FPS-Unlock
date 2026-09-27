// Real companion LoadConfig/SaveRequest using self-built ReShade configuration API.
// No ReShade, game, Streamline or NVIDIA DLL is loaded or executed.
#include "../addon.cpp"
#include <map>
#include <iostream>
#include <stdexcept>
std::map<std::pair<std::string,std::string>,std::string> config;
unsigned writes=0, checks=0;
extern "C" __declspec(dllexport) bool ReShadeGetConfigValue(void*,reshade::api::effect_runtime*,const char* section,const char* key,char* value,size_t* size) {
 auto found=config.find({section,key});if(found==config.end())return false;
 size_t required=found->second.size()+1;
 if(value){if(*size<required){*size=required;return false;}memcpy(value,found->second.c_str(),required);}
 *size=required;return true;
}
extern "C" __declspec(dllexport) void ReShadeSetConfigValue(void*,reshade::api::effect_runtime*,const char* section,const char* key,const char* value) {config[{section,key}]=value;++writes;}
void Check(bool value,const char* name){if(!value)throw std::runtime_error(name);++checks;std::cout<<"PASS "<<name<<'\n';}
int main(){
 reshade::internal::get_reshade_module_handle(GetModuleHandleW(nullptr));
 LoadConfig();Check(live::requested==4&&user_enabled,"fresh companion default4 enabled");
 config[{"RenoDX.MFGUnlock","DynamicMaxMultiplier"}]="2";
 LoadConfig();Check(live::requested==4,"retired static key never read");
 config[{"RenoDX.MFGUnlock",live::kConfigKey}]="6";
 LoadConfig();Check(live::requested==6&&config[{kSection,live::kConfigKey}]=="6","legacy live6 migrated to companion namespace");
 Check(config[{"RenoDX.MFGUnlock",live::kConfigKey}]=="6","migration never changes main addon config");
 auto previous=writes;config[{"RenoDX.MFGUnlock",live::kConfigKey}]="2";
 LoadConfig();Check(live::requested==6&&writes==previous,"own key prevents repeated migration");
 SaveRequest(0);live::requested=4;LoadConfig();Check(live::requested==0,"native0 persists via real API wiring");
 SaveRequest(6);live::requested=4;LoadConfig();Check(live::requested==6,"saved6 survives simulated next load");
 config[{kSection,live::kConfigKey}]="broken";LoadConfig();Check(live::requested==4,"malformed own key defaults4 not legacy2");
 config[{kSection,live::kConfigKey}]=std::string("6\0" "2",3);LoadConfig();Check(live::requested==4,"multiple values rejected");
 config[{kSection,live::kConfigKey}]=std::string(200,'6');LoadConfig();Check(live::requested==4,"oversized config rejected");
 config[{kSection,"Enabled"}]="0";LoadConfig();Check(!user_enabled,"own disable retained");
 Check(GetModuleHandleW(L"sl.dlss_g.dll")==nullptr&&GetModuleHandleW(L"nvapi64.dll")==nullptr,"no real runtime or driver loaded");
 std::cout<<checks<<"/"<<checks<<" companion API configuration tests passed\n";
}
