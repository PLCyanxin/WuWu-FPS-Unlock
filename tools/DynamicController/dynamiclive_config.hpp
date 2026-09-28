// SPDX-License-Identifier: MIT
#pragma once
#include <cstddef>
#include <string_view>
namespace mfgunlock::dynamiclive {
inline constexpr char kLegacyConfigKey[] = "DynamicLiveMaxMultiplier";
inline constexpr char kConfigKey[] = "DynamicFrameGenerationChoiceV2";
inline constexpr int kNativeBound = -1;
inline constexpr int kDefaultRequest = 4;
constexpr int NormalizeConfig(int value) { return value == kNativeBound || value == 0 || (value >= 2 && value <= 6) ? value : kDefaultRequest; }
constexpr int ParseConfig(std::size_t count, std::string_view value) {
 if (count != 1) return kDefaultRequest;
 while (!value.empty() && (value.front() == ' ' || value.front() == '\t')) value.remove_prefix(1);
 while (!value.empty() && (value.back() == ' ' || value.back() == '\t')) value.remove_suffix(1);
 if (value == "-1") return kNativeBound;
 if (value.size() != 1 || value[0] < '0' || value[0] > '6') return kDefaultRequest;
 return NormalizeConfig(value[0] - '0');
}
constexpr int MigrateLegacy(std::size_t count, std::string_view value) {
 const int parsed=ParseConfig(count,value);
 return parsed==0 ? kNativeBound : (parsed==kNativeBound ? kDefaultRequest : parsed);
}
}
