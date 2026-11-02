// Production minimum-version and capability publication policy, no vendor code.
#include "runtime_version.hpp"
#include "capability_epoch.hpp"
#include <iostream>
#include <stdexcept>
using namespace mfgunlock::runtimeversion;
int main(){unsigned checks=0;auto check=[&](bool v){++checks;if(!v)throw std::runtime_error("runtime compatibility regression");};
 check(!IsDynamicStreamlineVersion(0));check(!IsDynamicDlssgVersion(0));
 for(auto v:{Pack(2,14,1,0),Pack(2,14,1,1),Pack(2,14,16,0),Pack(2,15,0,0),Pack(3,0,0,0)})check(IsDynamicStreamlineVersion(v));
 for(auto v:{Pack(1,99,99,0),Pack(2,13,99,0),Pack(2,14,0,99)})check(!IsDynamicStreamlineVersion(v));
 for(auto v:{Pack(310,9,1,0),Pack(310,9,1,1),Pack(310,9,16,0),Pack(310,10,0,0),Pack(311,0,0,0)})check(IsDynamicDlssgVersion(v));
 for(auto v:{Pack(309,99,99,0),Pack(310,8,99,0),Pack(310,9,0,99)})check(!IsDynamicDlssgVersion(v));
 using namespace wuwa::capability;
 check(Valid(Encode(1,true),1));check(!Valid(Encode(1,false),1));check(!Valid(0,1));check(!Valid(Encode(1,true),2));
 // A late old-runtime result cannot authorize a new runtime, even after reset.
 check(!Valid(Encode(9,true),10));check(Valid(Encode(10,true),10));check(!Valid(Encode(10,false),10));
 Binding old{100,10},current{200,20};check(Matches(&current,200,20));check(!Matches(&old,200,20));check(!Matches(&current,100,20));check(!Matches(&current,200,10));check(!Matches(nullptr,200,20));check(!Matches(&current,0,20));
 std::cout<<checks<<" minimum-version/atomic-epoch policy checks passed\n";
}
