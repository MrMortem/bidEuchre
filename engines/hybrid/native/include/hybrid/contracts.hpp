#pragma once
#include "hybrid/schema.hpp"
#include "json.hpp"
#include <array>
#include <chrono>
#include <cstdint>
#include <random>

namespace hybrid {
using ActionId = std::uint8_t;
using Json = beu::json::Value;
using Rng = std::mt19937_64;
using Deadline = std::chrono::steady_clock::time_point;
using ActionMask = std::array<bool, schema::action_count>;
using Features = std::array<float, schema::feature_count>;
using BoundaryFeatures = std::array<float, schema::boundary_feature_count>;
static_assert(schema::action_count == 61 && schema::feature_count == 1188 && schema::boundary_feature_count == 38);
} // namespace hybrid
