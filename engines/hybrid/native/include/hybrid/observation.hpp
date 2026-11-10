#pragma once
#include "hybrid/contracts.hpp"
#include "game.hpp"
#include <optional>
#include <set>

namespace hybrid {
// nullopt is unknown; a zero mask is known-empty. No other seat's hand is stored.
struct SeatRecall {
    std::optional<beu::CardMask> initial_hand;
    std::optional<beu::CardMask> sent_exchange;
    std::optional<beu::CardMask> received_exchange;
    std::vector<ActionId> own_actions;
    bool operator==(const SeatRecall&) const = default;
};
struct PublicSeatSummary {
    std::uint64_t bid_observations=0, passes=0, solo_bids=0;
    std::uint64_t numeric_bids=0, numeric_bid_sum=0, contract_observations=0;
    std::array<std::uint64_t,3> modes{}; // High, Low, Trump
    bool operator==(const PublicSeatSummary&) const = default;
};
struct PublicSummary {
    std::array<PublicSeatSummary,4> seats{}; // absolute seats; rotated only at encoding
    bool operator==(const PublicSummary&) const = default;
};
struct SeatObservation {
    Json position; // canonical allowlisted BEUCI view; opponent cards always null
    SeatRecall recall;
    PublicSummary summary;
    ActionMask legal{};
    Features features{};
};
class SeatMemory {
public:
    SeatObservation observe(const Json& privatePosition);
    void record_own_action(ActionId action);
    void new_game();
    // Resume an observer's own sanitized state without inventing unknown recall.
    static SeatMemory from_observation(const SeatObservation&);
    // Public counters preceding the replayed deal; accepts no private state.
    static SeatMemory with_public_summary(const PublicSummary&);
    // Chronological actor decisions followed optionally by a final observation.
    // Consecutive duplicate snapshots consume no additional action. Every action
    // is matched to a distinct legal actor decision, never to a terminal view.
    static SeatMemory from_private_history(const std::vector<Json>& positions,
                                           const std::vector<ActionId>& ownActions);
private:
    std::optional<beu::Position> previous_;
    std::optional<std::uint64_t> serial_;
    SeatRecall recall_;
    PublicSummary summary_;
    std::set<std::string> public_events_;
    std::uint64_t legacy_epoch_=0;
    bool action_recorded_=false;
    ActionMask legal_{};
    std::string snapshot_;
};
// Intentionally cannot accept cards, recall, RNG, Json, or a simulator state.
BoundaryFeatures encode_boundary(const std::array<int,2>& scores, int nextDealer,
                                 int observer, const PublicSummary& summary);
} // namespace hybrid
