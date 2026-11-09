#pragma once
#include "hybrid/contracts.hpp"
#include "game.hpp"
#include <optional>

namespace hybrid {
struct Deal { std::array<beu::CardMask,4> hands{}; };
struct HandResult {
    std::array<int,2> scoresBefore{}, deltas{}, tricks{}, scoresAfter{};
    std::optional<int> winner;
};
HandResult score_hand(const beu::Contract&, int bidder, std::array<int,2> tricks,
                      std::array<int,2> scoresBefore);

// Deterministic rules authority: callers inject deals. No policy receives full_state().
class NativeMatch {
public:
    void start(const Deal&, int dealer, std::array<int,2> scores,
               int handNumber=1, std::uint64_t serial=1);
    [[nodiscard]] ActionMask legal_actions() const;
    void apply(ActionId);
    [[nodiscard]] Json view(int seat) const; // BEUCI private position
    [[nodiscard]] Json full_state() const; // privileged fixture projection
    [[nodiscard]] Json private_projection(int seat) const; // fixture comparison only
    [[nodiscard]] HandResult hand_result() const;
    void start_next_hand(const Deal&);
    void redeal(const Deal&);
    void apply_illegal_play_penalty(int seat);
private:
    enum class Phase { NotStarted, Bidding, ChoosingContract, ExchangingBidderCard,
                       ExchangingPartnerCard, Playing, HandComplete, GameComplete };
    Phase phase_=Phase::NotStarted;
    Deal deal_;
    int dealer_=0, actor_=-1, handNumber_=0, highBid_=0, bidder_=-1;
    std::uint64_t serial_=0;
    std::array<int,2> scores_{}, tricks_{};
    beu::Contract contract_;
    std::vector<std::pair<int,int>> auction_;
    std::vector<beu::CardPlay> trick_;
    std::vector<beu::CompletedTrick> completed_;
    Json::Array history_;
    std::optional<HandResult> result_;
    std::optional<int> winner_;
    void deal(const Deal&);
    void begin_play();
    void complete(std::array<int,2> deltas);
    [[nodiscard]] int sitting() const;
    [[nodiscard]] Json project(int seat, bool all) const;
};
// Privileged replay helpers. They never deliver full-state snapshots to a policy.
Deal deal_from_json(const Json&);
void replay_fixture(const Json&);
} // namespace hybrid
