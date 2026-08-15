#pragma once

#include "json.hpp"

#include <array>
#include <chrono>
#include <cstdint>
#include <random>
#include <string>
#include <string_view>
#include <vector>

namespace beu {

using CardMask = std::uint32_t;

enum class Mode { High, Low, Trump };

struct CardPlay {
    int seat = -1;
    int card = -1;
};

struct Contract {
    int bid = 0; // Three..Alone are 3..8.
    Mode mode = Mode::High;
    int trump = -1;
    bool valid = false;

    [[nodiscard]] bool partner_sits() const { return bid == 7 || bid == 8; }
    [[nodiscard]] int required_tricks() const { return bid >= 3 && bid <= 6 ? bid : 6; }
};

struct CompletedTrick {
    int winner = -1;
    std::vector<CardPlay> plays;
};

struct LegalActions {
    bool can_pass = false;
    std::vector<int> bids;
    std::vector<Mode> modes;
    std::vector<int> trump_suits;
    std::vector<int> cards;
};

struct Position {
    int seat = -1;
    std::string phase;
    int hand_number = 0;
    int dealer = -1;
    int current_seat = -1;
    std::array<int, 2> scores{};
    std::array<int, 4> card_counts{};
    std::array<bool, 4> sitting{};
    CardMask hand = 0;
    std::vector<std::pair<int, int>> auction; // bid 0 means pass.
    int high_bid = 0;
    int bidder = -1;
    Contract contract;
    std::vector<CardPlay> current_trick;
    std::vector<CompletedTrick> completed_tricks;
    std::array<int, 2> tricks{};
    LegalActions legal;
};

[[nodiscard]] Position parse_position(const json::Value& root);
[[nodiscard]] int parse_card(std::string_view code);
[[nodiscard]] std::string card_code(int card);
[[nodiscard]] int parse_bid(std::string_view name);
[[nodiscard]] std::string bid_token(int bid);
[[nodiscard]] int parse_suit(std::string_view name);
[[nodiscard]] std::string suit_token(int suit);
[[nodiscard]] Mode parse_mode(std::string_view name);
[[nodiscard]] std::string mode_token(Mode mode);

[[nodiscard]] int printed_suit(int card);
[[nodiscard]] int rank_index(int card);
[[nodiscard]] int effective_suit(int card, const Contract& contract);
[[nodiscard]] int trick_strength(int card, const Contract& contract);
[[nodiscard]] CardMask legal_cards(CardMask hand, const std::vector<CardPlay>& trick,
                                   const Contract& contract);
[[nodiscard]] int trick_winner(const std::vector<CardPlay>& trick, const Contract& contract);
[[nodiscard]] int next_active(int seat, int sitting_seat);
[[nodiscard]] int sitting_seat(const Position& position);
[[nodiscard]] int score_utility(const Contract& contract, int bidder, int team_zero_tricks,
                                int team_one_tricks, int perspective_team);

struct PlayState {
    std::array<CardMask, 4> hands{};
    int current_seat = -1;
    int sitting_seat = -1;
    Contract contract;
    int bidder = -1;
    std::vector<CardPlay> trick;
    std::array<int, 2> tricks{};
    int completed = 0;
};

void apply_play(PlayState& state, int card);
[[nodiscard]] int greedy_play(const PlayState& state);
[[nodiscard]] int simulate_greedy(PlayState state, int perspective_team);

struct HiddenDealContext {
    std::array<std::array<bool, 4>, 4> void_suit{};
    CardMask unknown = 0;
    std::array<int, 4> counts{};
};

[[nodiscard]] HiddenDealContext hidden_context(const Position& position);
[[nodiscard]] bool sample_hidden_hands(const Position& position,
                                       const HiddenDealContext& context,
                                       std::mt19937_64& random,
                                       std::array<CardMask, 4>& hands,
                                       int forced_card = -1,
                                       int forced_seat = -1);
[[nodiscard]] std::vector<std::array<CardMask, 4>> sample_hidden_deals(
    const Position& position,
    const HiddenDealContext& context,
    std::mt19937_64& random,
    int count,
    int forced_card = -1,
    int forced_seat = -1,
    std::chrono::steady_clock::time_point deadline = {});
[[nodiscard]] PlayState make_play_state(const Position& position,
                                        const std::array<CardMask, 4>& hands);

[[nodiscard]] std::vector<int> cards_in(CardMask mask);
[[nodiscard]] int lowest_expendable(CardMask legal, const Contract& contract);
[[nodiscard]] int donation_value(int card, const Contract& contract);

} // namespace beu
