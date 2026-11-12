#pragma once
#include "hybrid/contracts.hpp"
#include <string>
#include <string_view>

namespace hybrid {
[[nodiscard]] std::string format_action(ActionId action);
[[nodiscard]] ActionId parse_action(std::string_view command);
// The host is the authority on legality. Unknown/duplicate values are errors.
[[nodiscard]] ActionMask legal_mask(const Json& privatePosition);
} // namespace hybrid
