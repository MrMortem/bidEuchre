#pragma once
#include "hybrid/contracts.hpp"
#include <string>

namespace hybrid {
// JSON numbers must be finite; reject NaN/infinity rather than emit invalid JSON.
[[nodiscard]] std::string json_stringify(const Json& value);
} // namespace hybrid
