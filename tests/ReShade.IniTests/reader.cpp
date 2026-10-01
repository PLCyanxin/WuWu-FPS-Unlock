#include <cstdio>
#include <cstring>
#include <memory>
#include <share.h>
#include <iostream>
#include "ini_file.cpp"
std::filesystem::path g_reshade_dll_path,g_reshade_base_path,g_target_executable_path;
int wmain(int argc,wchar_t** argv) {
  if(argc!=3)return 2;
  reshade::ini_file ini(argv[1]);std::wstring stage(argv[2]);
  std::vector<std::string> actual;ini.get("ADDON","LoadFromDllMain",actual);
  std::vector<std::string> expected={"vendor,extras.addon64","other.addon64"};
  if(stage==L"deployed")expected.push_back("renodx-mfgunlock.addon64");
  std::string path,keep;int enabled=-1,lower=-1;
  ini.get("ADDON","AddonPath",path);ini.get("USER","Keep",keep);
  ini.get("RenoDX.MFGUnlock","Enabled",enabled);ini.get("renodx.mfgunlock","Enabled",lower);
  if(actual!=expected||path!="addons,shared"||keep!="a,b"||lower!=7||enabled!=(stage==L"deployed"?1:0))return 1;
  std::cout<<"PASS actual upstream INI parse\n";return 0;
}
