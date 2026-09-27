// SPDX-License-Identifier: MIT
#pragma once
#include <cstddef>
#include <string_view>
namespace mfgunlock::dynamiclive {
inline constexpr char kConfigKey[] = "DynamicLiveMaxMultiplier";
inline constexpr int kDefaultRequest = 4;
constexpr int NormalizeConfig(int value) { return value == 0 || (value >= 2 && value <= 6) ? value : kDefaultRequest; }
constexpr int ParseConfig(std::size_t count, std::string_view value) {
 if (count != 1) return kDefaultRequest;
 while (!value.empty() && (value.front() == ' ' || value.front() == '\t')) value.remove_prefix(1);
 while (!value.empty() && (value.back() == ' ' || value.back() == '\t')) value.remove_suffix(1);
 if (value.size() != 1 || value[0] < '0' || value[0] > '6') return kDefaultRequest;
 return NormalizeConfig(value[0] - '0');
}
}
