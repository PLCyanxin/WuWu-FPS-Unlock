#include "../dynamiclive_module_discovery.hpp"
#include <cassert>
#include <cstdio>
using mfgunlock::dynamiclive::discovery::CandidatePath;
using mfgunlock::dynamiclive::discovery::AcquireLoaded;

int wmain(int argc, wchar_t** argv) {
 assert(CandidatePath(L"F:\\Game\\sl.dlss_g.dll"));
 assert(CandidatePath(L"C:\\ProgramData\\NVIDIA\\NGX\\models\\sl_dlss_g_0\\versions\\134672\\files\\190_E658703.dll"));
 assert(CandidatePath(L"c:/PROGRAMDATA/nvidia/ngx/models/sl_dlss_g_0/versions/134672/files/190_e658703.dll"));
 assert(!CandidatePath(L"F:\\Game\\sl.dlss_g.dll.backup"));
 assert(!CandidatePath(L"C:\\Other\\190_E658703.dll"));
 assert(!CandidatePath(L"C:\\ProgramData\\NVIDIA\\NGX\\models\\sl_dlss_d_0\\versions\\134672\\files\\190_E658703.dll"));
 assert(!CandidatePath(L"C:\\ProgramData\\NVIDIA\\NGX\\models\\sl_dlss_g_0\\versions\\134672\\files\\asset.bin"));
 assert(!CandidatePath(L"C:\\ProgramData\\NVIDIA\\NGX\\models\\sl_dlss_g_0\\versions\\134672\\not-files\\190_E658703.dll"));
 assert(!CandidatePath(L""));
 if(argc!=4) {std::fputs("Supply two NGX alias DLL paths and one ordinary test-owned DLL path\n",stderr); return 2;}
 bool alias=false, ambiguous=false;
 const HMODULE first=LoadLibraryW(argv[1]);
 if(!first) {std::printf("Failed first synthetic DLL Win32=%lu\n",GetLastError()); return 3;}
 const HMODULE unique=AcquireLoaded(&alias,&ambiguous);
 if(unique!=first || !alias || ambiguous) {
  std::puts("FAIL: unique NGX alias discovery");
  if(unique)FreeLibrary(unique);
  FreeLibrary(first);
  return 4;
 }
 FreeLibrary(unique);
 const HMODULE second=LoadLibraryW(argv[2]);
 if(!second || first==second) {std::puts("FAIL: second alias load");if(second)FreeLibrary(second);FreeLibrary(first);return 5;}
 alias=true;ambiguous=false;
 const HMODULE duplicate=AcquireLoaded(&alias,&ambiguous);
 if(duplicate || !ambiguous) {
  std::puts("FAIL: multiple mapped NGX alias DLLs must not select an arbitrary target");
  if(duplicate)FreeLibrary(duplicate);
  FreeLibrary(second);FreeLibrary(first);
  return 6;
 }
 FreeLibrary(second);
 alias=false;ambiguous=false;
 const HMODULE after=AcquireLoaded(&alias,&ambiguous);
 if(after!=first || !alias || ambiguous) {
  std::puts("FAIL: unique candidate must recover when second DLL unloads");
  if(after)FreeLibrary(after);
  FreeLibrary(first);
  return 7;
 }
 FreeLibrary(after);
 FreeLibrary(first);
 // The ordinary filename must be recognized, but never preferred over a
 // simultaneously loaded NGX alias without proof of the active API owner.
 const HMODULE ordinary=LoadLibraryW(argv[3]);
 if(!ordinary) {std::puts("FAIL: ordinary synthetic DLL failed to load");return 8;}
 alias=true;ambiguous=true;
 const HMODULE ordinaryOnly=AcquireLoaded(&alias,&ambiguous);
 if(ordinaryOnly!=ordinary || alias || ambiguous) {
  std::puts("FAIL: ordinary-only module discovery");
  if(ordinaryOnly)FreeLibrary(ordinaryOnly);
  FreeLibrary(ordinary);return 9;
 }
 FreeLibrary(ordinaryOnly);
 const HMODULE coexist=LoadLibraryW(argv[1]);
 if(!coexist || coexist==ordinary) {
  std::puts("FAIL: ordinary and alias synthetic DLLs failed to coexist");
  if(coexist)FreeLibrary(coexist);
  FreeLibrary(ordinary);return 10;
 }
 alias=false;ambiguous=false;
 const HMODULE mixed=AcquireLoaded(&alias,&ambiguous);
 if(mixed || !ambiguous) {
  std::puts("FAIL: ordinary+NGX alias must reject ambiguous ownership");
  if(mixed)FreeLibrary(mixed);
  FreeLibrary(coexist);FreeLibrary(ordinary);return 11;
 }
 FreeLibrary(coexist);
 alias=true;ambiguous=true;
 const HMODULE restored=AcquireLoaded(&alias,&ambiguous);
 if(restored!=ordinary || alias || ambiguous) {
  std::puts("FAIL: ordinary module discovery after alias unload");
  if(restored)FreeLibrary(restored);
  FreeLibrary(ordinary);return 12;
 }
 FreeLibrary(restored);
 FreeLibrary(ordinary);
 std::puts("PASS: 9 filename checks, NGX alias, ordinary DLL, dual aliases and mixed-name ambiguity, unload recovery");
 return 0;
}
