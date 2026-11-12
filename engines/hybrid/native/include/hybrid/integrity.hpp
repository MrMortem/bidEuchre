#pragma once
#include <cstddef>
#include <span>
#include <string>
namespace hybrid {
[[nodiscard]] std::string sha256(std::span<const std::byte> bytes);
}
