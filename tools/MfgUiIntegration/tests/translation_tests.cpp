// SPDX-License-Identifier: MIT
#include <imgui.h>
#include "ui_zh.hpp"
#include <chrono>
#include <iostream>
#include <stdexcept>
// Independent reference preserves the original mapping order and boundary rules.
static std::string Reference(const char* text) {
 for(const auto& pair:kTranslations)if(std::string_view(text)==pair.first)return pair.second;
 if(strlen(text)>4096)return text;
 std::string result(text);
 auto word=[](unsigned char ch){return ch<128&&(std::isalnum(ch)||ch=='_');};
 for(auto& pair:kTranslations){std::string_view key(pair.first);bool numeric=key=="x live"||key=="x max"||key=="% render scale";
  if(key.size()<4||(!numeric&&key.find('%')!=key.npos)||key.find('#')!=key.npos)continue;
  size_t at=0;while((at=result.find(key,at))!=std::string::npos){size_t end=at+key.size();bool number=numeric&&at&&result[at-1]>='0'&&result[at-1]<='9';
   if((at&&word(key.front())&&word(result[at-1])&&!number)||(end<result.size()&&word(key.back())&&word(result[end]))){at=end;continue;}
   result.replace(at,key.size(),pair.second);at+=strlen(pair.second);
  }
 }
 return result;
}
int main(){try{
 size_t checks=0;
 auto check=[&](bool valid){++checks;if(!valid)throw std::runtime_error("translation regression at "+std::to_string(checks));};
 check(wuwa_ui::Translate(nullptr)==nullptr);
 std::vector<std::string> corpus;
 for(auto& pair:kTranslations){corpus.emplace_back(pair.first);for(auto prefix:{"[", "identifier_", "8"})corpus.emplace_back(std::string(prefix)+pair.first+" / "+pair.first+" tail");}
 for(int n=0;n<1000;++n)corpus.push_back(std::to_string(n)+"x live / 6x max; "+std::to_string(n)+"% render scale");
 corpus.emplace_back(4097,'A');corpus.emplace_back(4096,'A');corpus.emplace_back(256,'A');corpus.emplace_back(257,'A');corpus.emplace_back("");
 for(int round=0;round<2;++round)for(auto& s:corpus)check(std::string(wuwa_ui::Translate(s.c_str()))==Reference(s.c_str()));
 // Cached outputs must retain the original 32-result scratch lifetime.
 std::array<const char*,32> pointers{};std::array<std::string,32> expected{};
 for(int i=0;i<32;++i){std::string value=std::to_string(i)+"x live / 6x max";expected[i]=Reference(value.c_str());pointers[i]=wuwa_ui::Translate(value.c_str());}
 for(int i=0;i<32;++i)check(pointers[i]==expected[i]);
 for(auto& pair:kTranslations){auto label=wuwa_ui::Label(pair.first);auto translated=std::string(wuwa_ui::Translate(pair.first));check(label==translated+"###"+pair.first || label==translated);}
 std::vector<std::string> load;for(auto& p:kTranslations)load.emplace_back(p.first);
 for(auto s:{"4x live / 6x max","Preset C","57% render scale","Running: NVIDIA Dynamic MFG","uncategorized status","Not Active (waiting)"})load.emplace_back(s);
 std::uint64_t digest=0;auto start=std::chrono::steady_clock::now();
 for(int round=0;round<2000;++round)for(auto& s:load)digest+=static_cast<unsigned char>(*wuwa_ui::Translate(s.c_str()));
 auto us=std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-start).count();
 std::cout<<checks<<" regression checks passed; mappings="<<std::size(kTranslations)<<" calls="<<load.size()*2000<<" us="<<us<<" digest="<<digest<<'\n';return 0;
 }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
