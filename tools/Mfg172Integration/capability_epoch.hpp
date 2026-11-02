// SPDX-License-Identifier: MIT
#pragma once
#include <cstdint>
namespace wuwa::capability {
struct Binding {std::uintptr_t function=0,owner=0;};
constexpr bool Matches(const Binding* binding,std::uintptr_t function,std::uintptr_t owner) {
 return binding && function && owner && binding->function==function && binding->owner==owner;
}
constexpr std::uint64_t Encode(std::uint64_t epoch,bool supported) {
 return (epoch<<2)|(supported?2u:1u);
}
constexpr bool Valid(std::uint64_t sample,std::uint64_t epoch) {
 return epoch && (sample>>2)==epoch && (sample&3u)==2;
}
}
