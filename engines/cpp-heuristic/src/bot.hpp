#pragma once

#include "game.hpp"

#include <chrono>
#include <cstdint>
#include <optional>
#include <random>
#include <string>

namespace beu {

struct BotOptions {
    int samples = 160;
    int play_time_ms = 700;
    int search_depth = 12;
    int search_nodes = 12000;
    std::uint64_t seed = 0x5eedb1deULL;
};

class HeuristicBot {
public:
    explicit HeuristicBot(BotOptions options = {});

    void new_game();
    void set_position(Position position);
    void set_option(const std::string& name, const std::string& value);
    [[nodiscard]] std::string choose_action();

private:
    struct ContractChoice {
        Contract contract;
        double utility = -1e30;
        double tricks = 0.0;
        double make_probability = 0.0;
    };

    [[nodiscard]] std::string choose_bid();
    [[nodiscard]] std::string choose_contract();
    [[nodiscard]] std::string choose_exchange();
    [[nodiscard]] std::string choose_play();

    [[nodiscard]] ContractChoice evaluate_contract(const Contract& contract,
                                                   const std::vector<std::array<CardMask, 4>>& deals,
                                                   int bidder) const;
    [[nodiscard]] std::vector<std::array<CardMask, 4>> sample_deals(
        int count, std::chrono::steady_clock::time_point deadline = {}) const;
    [[nodiscard]] double public_history_likelihood(
        const std::array<CardMask, 4>& remaining_hands) const;
    [[nodiscard]] int modeled_bid(CardMask hand, int standing_bid, bool forced,
                                  bool partner_winning) const;
    [[nodiscard]] Contract modeled_contract(CardMask hand, int bid) const;
    [[nodiscard]] std::vector<double> auction_action_values(
        int candidate, const std::vector<std::array<CardMask, 4>>& deals) const;
    [[nodiscard]] int select_partner_return(CardMask hand, const Contract& contract) const;
    [[nodiscard]] int select_bidder_discard(CardMask hand, const Contract& contract) const;
    void apply_exchange(std::array<CardMask, 4>& hands, const Contract& contract,
                        int bidder, int forced_discard = -1) const;
    [[nodiscard]] double completed_utility(const PlayState& state, int perspective_team) const;
    [[nodiscard]] PlayState searched_finish(PlayState state, int perspective_team) const;
    [[nodiscard]] std::uint64_t random_seed(std::uint64_t salt) const;

    BotOptions options_;
    std::optional<Position> position_;
    std::uint64_t position_hash_ = 0;
    int remembered_hand_number_ = -1;
    CardMask remembered_hand_ = 0;
    int received_exchange_card_ = -1;
    int sent_exchange_card_ = -1;
};

[[nodiscard]] std::uint64_t stable_hash(std::string_view value);

} // namespace beu
