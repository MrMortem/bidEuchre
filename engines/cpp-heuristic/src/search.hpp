#pragma once

#include "game.hpp"

#include <chrono>
#include <cstdint>
#include <unordered_map>

namespace beu {

struct SearchLimits {
    int maximum_nodes = 180000;
    int maximum_depth = 12;
    std::chrono::steady_clock::time_point deadline{};
};

class DoubleDummySolver {
public:
    explicit DoubleDummySolver(SearchLimits limits);

    // Returns the number of additional tricks won by perspective_team.
    [[nodiscard]] int future_tricks(const PlayState& state, int perspective_team);
    [[nodiscard]] std::uint64_t nodes() const { return nodes_; }
    [[nodiscard]] bool interrupted() const { return interrupted_; }

private:
    struct Key {
        std::array<CardMask, 4> hands{};
        std::array<std::uint8_t, 4> trick_cards{};
        std::array<std::uint8_t, 4> trick_seats{};
        std::uint8_t current = 0;
        std::uint8_t trick_size = 0;

        bool operator==(const Key&) const = default;
    };

    struct KeyHash {
        std::size_t operator()(const Key& key) const noexcept;
    };

    enum class Bound : std::uint8_t { Exact, Lower, Upper };
    struct Entry {
        std::int8_t value = 0;
        Bound bound = Bound::Exact;
    };

    [[nodiscard]] int solve(const PlayState& state, int perspective_team,
                            int depth, int alpha, int beta);
    [[nodiscard]] int rollout(const PlayState& state, int perspective_team) const;
    [[nodiscard]] Key make_key(const PlayState& state) const;
    [[nodiscard]] bool expired();

    SearchLimits limits_;
    std::unordered_map<Key, Entry, KeyHash> table_;
    std::uint64_t nodes_ = 0;
    bool interrupted_ = false;
};

} // namespace beu
