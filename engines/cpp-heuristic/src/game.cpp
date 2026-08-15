#include "game.hpp"

#include <algorithm>
#include <bit>
#include <cctype>
#include <functional>
#include <limits>
#include <numeric>
#include <stdexcept>
#include <unordered_map>

namespace beu {

namespace {

constexpr std::array<char, 6> ranks{'9', 'T', 'J', 'Q', 'K', 'A'};
constexpr std::array<char, 4> suits{'C', 'D', 'H', 'S'};
constexpr CardMask full_deck = (CardMask{1} << 24U) - 1U;

const json::Value* optional(const json::Value& object, std::string_view key) {
    const auto* value = object.find(key);
    return value && !value->is_null() ? value : nullptr;
}

int optional_integer(const json::Value& object, std::string_view key, int fallback) {
    const auto* value = optional(object, key);
    return value ? value->integer() : fallback;
}

std::vector<int> parse_cards(const json::Value& values) {
    std::vector<int> result;
    for (const auto& value : values.array()) {
        result.push_back(parse_card(value.at("code").string()));
    }
    return result;
}

CardPlay parse_play(const json::Value& value) {
    return {value.at("seat").integer(), parse_card(value.at("card").at("code").string())};
}

bool same_color(int first, int second) {
    const bool first_red = first == 1 || first == 2;
    const bool second_red = second == 1 || second == 2;
    return first_red == second_red;
}

int card_cost(int card, const Contract& contract) {
    return donation_value(card, contract);
}

int current_winner(const std::vector<CardPlay>& trick, const Contract& contract) {
    return trick.empty() ? -1 : trick_winner(trick, contract);
}

CardMask suit_mask(int effective, const Contract& contract);

bool can_beat(int candidate, int incumbent, int led_suit, const Contract& contract) {
    const int candidate_suit = effective_suit(candidate, contract);
    const int incumbent_suit = effective_suit(incumbent, contract);
    if (contract.mode == Mode::Trump) {
        if (candidate_suit == contract.trump && incumbent_suit != contract.trump) return true;
        if (candidate_suit != contract.trump && incumbent_suit == contract.trump) return false;
    }
    if (candidate_suit != incumbent_suit) return false;
    if (candidate_suit != led_suit && candidate_suit != contract.trump) return false;
    if (contract.mode == Mode::Low) return trick_strength(candidate, contract) < trick_strength(incumbent, contract);
    return trick_strength(candidate, contract) > trick_strength(incumbent, contract);
}

int choose_greedy(const PlayState& state) {
    const int seat = state.current_seat;
    const CardMask legal = legal_cards(state.hands[seat], state.trick, state.contract);
    auto choices = cards_in(legal);
    if (choices.empty()) throw std::runtime_error("greedy player has no legal card");

    const int team = seat % 2;
    if (state.trick.empty()) {
        // Lead a card that is currently highest among all active outstanding cards.
        int best_winner = -1;
        int best_winner_cost = std::numeric_limits<int>::max();
        for (const int card : choices) {
            const int led = effective_suit(card, state.contract);
            bool unbeatable = true;
            for (int other = 0; other < 4 && unbeatable; ++other) {
                if (other == seat || other == state.sitting_seat) continue;
                for (const int reply : cards_in(legal_cards(state.hands[other], {{seat, card}}, state.contract))) {
                    if (can_beat(reply, card, led, state.contract)) {
                        unbeatable = false;
                        break;
                    }
                }
            }
            const int cost = card_cost(card, state.contract);
            if (unbeatable && cost < best_winner_cost) {
                best_winner = card;
                best_winner_cost = cost;
            }
        }
        if (best_winner >= 0) return best_winner;

        // Develop the longest effective suit, using a low card to preserve controls.
        return *std::min_element(choices.begin(), choices.end(), [&](int first, int second) {
            const int first_suit = effective_suit(first, state.contract);
            const int second_suit = effective_suit(second, state.contract);
            const int first_length = std::popcount(state.hands[seat] & suit_mask(first_suit, state.contract));
            const int second_length = std::popcount(state.hands[seat] & suit_mask(second_suit, state.contract));
            if (first_length != second_length) return first_length > second_length;
            return card_cost(first, state.contract) < card_cost(second, state.contract);
        });
    }

    const int winner_before = current_winner(state.trick, state.contract);
    const int led = effective_suit(state.trick.front().card, state.contract);
    const int incumbent = [&] {
        for (const auto& play : state.trick) if (play.seat == winner_before) return play.card;
        return state.trick.front().card;
    }();
    std::vector<int> winners;
    for (const int card : choices) {
        if (can_beat(card, incumbent, led, state.contract)) winners.push_back(card);
    }
    const bool partner_winning = winner_before >= 0 && winner_before % 2 == team;
    const int active = state.sitting_seat >= 0 ? 3 : 4;
    const bool last = static_cast<int>(state.trick.size()) + 1 == active;
    auto later_opponent_can_beat = [&](int proposed) {
        int next = next_active(seat, state.sitting_seat);
        const int later_count = active - static_cast<int>(state.trick.size()) - 1;
        for (int index = 0; index < later_count; ++index) {
            if (next % 2 != team) {
                for (const int reply : cards_in(legal_cards(state.hands[next], state.trick, state.contract))) {
                    if (can_beat(reply, proposed, led, state.contract)) return true;
                }
            }
            next = next_active(next, state.sitting_seat);
        }
        return false;
    };
    if (partner_winning && (last || !later_opponent_can_beat(incumbent))) {
        return lowest_expendable(legal, state.contract);
    }
    if (!winners.empty()) {
        std::stable_sort(winners.begin(), winners.end(), [&](int first, int second) {
            return card_cost(first, state.contract) < card_cost(second, state.contract);
        });
        for (const int card : winners) {
            if (!later_opponent_can_beat(card)) return card;
        }
        return winners.front();
    }
    return lowest_expendable(legal, state.contract);
}

CardMask suit_mask(int effective, const Contract& contract) {
    CardMask result = 0;
    for (int card = 0; card < 24; ++card) {
        if (effective_suit(card, contract) == effective) result |= CardMask{1} << card;
    }
    return result;
}

} // namespace

Position parse_position(const json::Value& root) {
    Position position;
    position.seat = root.at("seat").integer();
    if (position.seat < 0 || position.seat > 3) throw json::Error("seat must be 0 through 3");
    const auto& game = root.at("game");
    position.phase = game.at("phase").string();
    position.hand_number = optional_integer(game, "handNumber", 0);
    position.dealer = optional_integer(game, "dealer", -1);
    position.current_seat = optional_integer(game, "currentSeat", -1);

    if (const auto* values = optional(game, "scores")) {
        if (values->array().size() != 2) throw json::Error("scores must have two entries");
        position.scores = {values->array()[0].integer(), values->array()[1].integer()};
    }

    const auto& players = game.at("players").array();
    if (players.size() != 4) throw json::Error("players must have four entries");
    for (const auto& player : players) {
        const int seat = player.at("seat").integer();
        if (seat < 0 || seat > 3) throw json::Error("player seat must be 0 through 3");
        position.card_counts[seat] = player.at("cardCount").integer();
        if (const auto* sitting = optional(player, "isSittingOut")) position.sitting[seat] = sitting->boolean();
        if (seat == position.seat) {
            const auto* cards = optional(player, "cards");
            if (!cards) throw json::Error("controlled player's cards are missing");
            for (const int card : parse_cards(*cards)) position.hand |= CardMask{1} << card;
        }
    }

    if (const auto* auction = optional(game, "auction")) {
        for (const auto& action : auction->array()) {
            int bid = 0;
            if (const auto* value = optional(action, "bid")) bid = parse_bid(value->string());
            position.auction.emplace_back(action.at("seat").integer(), bid);
        }
    }
    if (const auto* bid = optional(game, "highBid")) position.high_bid = parse_bid(bid->string());
    position.bidder = optional_integer(game, "bidder", -1);

    if (const auto* contract = optional(game, "contract")) {
        position.contract.valid = true;
        position.contract.bid = parse_bid(contract->at("bid").string());
        position.contract.mode = parse_mode(contract->at("mode").string());
        if (const auto* trump = optional(*contract, "trump")) position.contract.trump = parse_suit(trump->string());
    }
    if (const auto* trick = optional(game, "currentTrick")) {
        for (const auto& play : trick->array()) position.current_trick.push_back(parse_play(play));
    }
    if (const auto* tricks = optional(game, "completedTricks")) {
        for (const auto& trick : tricks->array()) {
            CompletedTrick completed;
            completed.winner = trick.at("winner").integer();
            for (const auto& play : trick.at("plays").array()) completed.plays.push_back(parse_play(play));
            position.completed_tricks.push_back(std::move(completed));
        }
    }
    if (const auto* values = optional(game, "tricksByTeam")) {
        if (values->array().size() != 2) throw json::Error("tricksByTeam must have two entries");
        position.tricks = {values->array()[0].integer(), values->array()[1].integer()};
    }

    const auto& legal = game.at("legalActions");
    if (const auto* can_pass = optional(legal, "canPass")) position.legal.can_pass = can_pass->boolean();
    if (const auto* bids = optional(legal, "bids")) {
        for (const auto& bid : bids->array()) position.legal.bids.push_back(parse_bid(bid.string()));
    }
    if (const auto* modes = optional(legal, "contractModes")) {
        for (const auto& mode : modes->array()) position.legal.modes.push_back(parse_mode(mode.string()));
    }
    if (const auto* suits_value = optional(legal, "trumpSuits")) {
        for (const auto& suit : suits_value->array()) position.legal.trump_suits.push_back(parse_suit(suit.string()));
    }
    if (const auto* cards = optional(legal, "cards")) position.legal.cards = parse_cards(*cards);
    return position;
}

int parse_card(std::string_view code) {
    if (code.size() != 2) throw json::Error("card code must contain two characters");
    const char rank = static_cast<char>(std::toupper(static_cast<unsigned char>(code[0])));
    const char suit = static_cast<char>(std::toupper(static_cast<unsigned char>(code[1])));
    const auto rank_it = std::find(ranks.begin(), ranks.end(), rank);
    const auto suit_it = std::find(suits.begin(), suits.end(), suit);
    if (rank_it == ranks.end() || suit_it == suits.end()) throw json::Error("invalid card code");
    return static_cast<int>(suit_it - suits.begin()) * 6 + static_cast<int>(rank_it - ranks.begin());
}

std::string card_code(int card) {
    if (card < 0 || card >= 24) throw std::out_of_range("card index");
    return {ranks[rank_index(card)], suits[printed_suit(card)]};
}

int parse_bid(std::string_view name) {
    if (name == "Three") return 3;
    if (name == "Four") return 4;
    if (name == "Five") return 5;
    if (name == "Six") return 6;
    if (name == "PartnersBest") return 7;
    if (name == "Alone") return 8;
    throw json::Error("unknown bid value");
}

std::string bid_token(int bid) {
    switch (bid) {
    case 3: return "3";
    case 4: return "4";
    case 5: return "5";
    case 6: return "6";
    case 7: return "partnersbest";
    case 8: return "alone";
    default: throw std::out_of_range("bid value");
    }
}

int parse_suit(std::string_view name) {
    if (name == "Clubs") return 0;
    if (name == "Diamonds") return 1;
    if (name == "Hearts") return 2;
    if (name == "Spades") return 3;
    throw json::Error("unknown suit value");
}

std::string suit_token(int suit) {
    static constexpr std::array<std::string_view, 4> names{"clubs", "diamonds", "hearts", "spades"};
    if (suit < 0 || suit > 3) throw std::out_of_range("suit value");
    return std::string(names[suit]);
}

Mode parse_mode(std::string_view name) {
    if (name == "High") return Mode::High;
    if (name == "Low") return Mode::Low;
    if (name == "Trump") return Mode::Trump;
    throw json::Error("unknown contract mode");
}

std::string mode_token(Mode mode) {
    switch (mode) {
    case Mode::High: return "high";
    case Mode::Low: return "low";
    case Mode::Trump: return "trump";
    }
    throw std::out_of_range("contract mode");
}

int printed_suit(int card) { return card / 6; }
int rank_index(int card) { return card % 6; }

int effective_suit(int card, const Contract& contract) {
    const int suit = printed_suit(card);
    if (contract.mode == Mode::Trump && rank_index(card) == 2 && suit != contract.trump &&
        same_color(suit, contract.trump)) {
        return contract.trump;
    }
    return suit;
}

int trick_strength(int card, const Contract& contract) {
    if (contract.mode == Mode::Trump && effective_suit(card, contract) == contract.trump) {
        if (rank_index(card) == 2 && printed_suit(card) == contract.trump) return 100;
        if (rank_index(card) == 2) return 99;
    }
    return rank_index(card);
}

CardMask legal_cards(CardMask hand, const std::vector<CardPlay>& trick, const Contract& contract) {
    if (trick.empty()) return hand;
    const int led = effective_suit(trick.front().card, contract);
    const CardMask following = hand & suit_mask(led, contract);
    return following ? following : hand;
}

int trick_winner(const std::vector<CardPlay>& trick, const Contract& contract) {
    if (trick.empty()) throw std::invalid_argument("empty trick");
    const int led = effective_suit(trick.front().card, contract);
    int winner = 0;
    for (int index = 1; index < static_cast<int>(trick.size()); ++index) {
        if (can_beat(trick[index].card, trick[winner].card, led, contract)) winner = index;
    }
    return trick[winner].seat;
}

int next_active(int seat, int sit) {
    int result = (seat + 1) % 4;
    if (result == sit) result = (result + 1) % 4;
    return result;
}

int sitting_seat(const Position& position) {
    for (int seat = 0; seat < 4; ++seat) if (position.sitting[seat]) return seat;
    if (position.contract.partner_sits() && position.bidder >= 0) return (position.bidder + 2) % 4;
    return -1;
}

int score_utility(const Contract& contract, int bidder, int team_zero_tricks,
                  int team_one_tricks, int perspective_team) {
    const int bidding_team = bidder % 2;
    const int defending_team = 1 - bidding_team;
    const int bidding_tricks = bidding_team == 0 ? team_zero_tricks : team_one_tricks;
    const int defending_tricks = defending_team == 0 ? team_zero_tricks : team_one_tricks;
    std::array<int, 2> deltas{};
    if (contract.bid == 7) deltas[bidding_team] = bidding_tricks == 6 ? 12 : -12;
    else if (contract.bid == 8) deltas[bidding_team] = bidding_tricks == 6 ? 24 : -24;
    else deltas[bidding_team] = bidding_tricks >= contract.required_tricks()
        ? bidding_tricks : -contract.required_tricks();
    deltas[defending_team] = defending_tricks;
    return deltas[perspective_team] - deltas[1 - perspective_team];
}

void apply_play(PlayState& state, int card) {
    const int seat = state.current_seat;
    if (!(state.hands[seat] & (CardMask{1} << card))) throw std::invalid_argument("card not held");
    state.hands[seat] &= ~(CardMask{1} << card);
    state.trick.push_back({seat, card});
    const int active = state.sitting_seat >= 0 ? 3 : 4;
    if (static_cast<int>(state.trick.size()) < active) {
        state.current_seat = next_active(seat, state.sitting_seat);
        return;
    }
    const int winner = trick_winner(state.trick, state.contract);
    ++state.tricks[winner % 2];
    ++state.completed;
    state.trick.clear();
    state.current_seat = winner;
}

int greedy_play(const PlayState& state) { return choose_greedy(state); }

int simulate_greedy(PlayState state, int perspective_team) {
    int guard = 30;
    while (state.completed < 6 && guard-- > 0) apply_play(state, choose_greedy(state));
    if (state.completed != 6) throw std::runtime_error("greedy simulation did not finish");
    return score_utility(state.contract, state.bidder, state.tricks[0], state.tricks[1], perspective_team);
}

HiddenDealContext hidden_context(const Position& position) {
    HiddenDealContext result;
    result.counts = position.card_counts;
    result.counts[position.seat] = 0;
    CardMask known = position.hand;
    auto observe_trick = [&](const std::vector<CardPlay>& plays) {
        if (plays.empty() || !position.contract.valid) return;
        const int led = effective_suit(plays.front().card, position.contract);
        for (const auto& play : plays) {
            known |= CardMask{1} << play.card;
            if (effective_suit(play.card, position.contract) != led) result.void_suit[play.seat][led] = true;
        }
    };
    for (const auto& trick : position.completed_tricks) observe_trick(trick.plays);
    observe_trick(position.current_trick);
    // Bidding/exchange positions have no contract, but still need to exclude any public cards.
    for (const auto& trick : position.completed_tricks) {
        for (const auto& play : trick.plays) known |= CardMask{1} << play.card;
    }
    for (const auto& play : position.current_trick) known |= CardMask{1} << play.card;
    result.unknown = full_deck & ~known;
    return result;
}

bool sample_hidden_hands(const Position& position, const HiddenDealContext& original,
                         std::mt19937_64& random, std::array<CardMask, 4>& hands,
                         int forced_card, int forced_seat) {
    auto deals = sample_hidden_deals(position, original, random, 1,
                                     forced_card, forced_seat);
    if (deals.empty()) return false;
    hands = deals.front();
    return true;
}

std::vector<std::array<CardMask, 4>> sample_hidden_deals(
    const Position& position, const HiddenDealContext& original,
    std::mt19937_64& random, int count, int forced_card, int forced_seat,
    std::chrono::steady_clock::time_point deadline) {
    if (count < 1) return {};
    HiddenDealContext context = original;
    std::array<CardMask, 4> fixed_hands{};
    fixed_hands[position.seat] = position.hand;
    if (forced_card >= 0 && forced_seat >= 0 && (context.unknown & (CardMask{1} << forced_card)) &&
        context.counts[forced_seat] > 0 &&
        !context.void_suit[forced_seat][position.contract.valid
            ? effective_suit(forced_card, position.contract) : printed_suit(forced_card)]) {
        fixed_hands[forced_seat] |= CardMask{1} << forced_card;
        context.unknown &= ~(CardMask{1} << forced_card);
        --context.counts[forced_seat];
    }
    if (std::accumulate(context.counts.begin(), context.counts.end(), 0) != std::popcount(context.unknown)) {
        return {};
    }
    auto cards = cards_in(context.unknown);
    std::shuffle(cards.begin(), cards.end(), random);
    std::stable_sort(cards.begin(), cards.end(), [&](int first, int second) {
        auto options = [&](int card) {
            int count = 0;
            for (int seat = 0; seat < 4; ++seat) {
                const int suit = position.contract.valid
                    ? effective_suit(card, position.contract) : printed_suit(card);
                if (context.counts[seat] > 0 && !context.void_suit[seat][suit]) ++count;
            }
            return count;
        };
        return options(first) < options(second);
    });

    // Count completions for every capacity state, then choose each owner in
    // proportion to its downstream count. This produces a uniform sample over
    // every deal that satisfies card counts and inferred voids.
    std::unordered_map<std::uint64_t, std::uint64_t> memo;
    auto key_for = [](std::size_t index, const std::array<int, 4>& counts) {
        std::uint64_t key = index;
        for (const int count : counts) key = key * 8U + static_cast<std::uint64_t>(count);
        return key;
    };
    auto eligible = [&](int seat, int card) {
        const int suit = position.contract.valid
            ? effective_suit(card, position.contract) : printed_suit(card);
        return !context.void_suit[seat][suit];
    };
    std::function<std::uint64_t(std::size_t, std::array<int, 4>&)> ways =
        [&](std::size_t index, std::array<int, 4>& counts) -> std::uint64_t {
            if (index == cards.size()) {
                return std::all_of(counts.begin(), counts.end(), [](int count) { return count == 0; }) ? 1U : 0U;
            }
            const auto key = key_for(index, counts);
            if (const auto found = memo.find(key); found != memo.end()) return found->second;
            std::uint64_t total = 0;
            for (int seat = 0; seat < 4; ++seat) {
                if (counts[seat] <= 0 || !eligible(seat, cards[index])) continue;
                --counts[seat];
                total += ways(index + 1, counts);
                ++counts[seat];
            }
            memo.emplace(key, total);
            return total;
        };

    auto initial_remaining = context.counts;
    if (ways(0, initial_remaining) == 0) return {};

    std::vector<std::array<CardMask, 4>> result;
    result.reserve(count);
    for (int sample = 0; sample < count; ++sample) {
        if (sample > 0 && deadline != std::chrono::steady_clock::time_point{} &&
            std::chrono::steady_clock::now() >= deadline) {
            break;
        }
        auto hands = fixed_hands;
        auto remaining = context.counts;
        bool valid = true;
        for (std::size_t index = 0; index < cards.size(); ++index) {
            struct Candidate { int seat; std::uint64_t ways; };
            std::vector<Candidate> candidates;
            std::uint64_t total = 0;
            for (int seat = 0; seat < 4; ++seat) {
                if (remaining[seat] <= 0 || !eligible(seat, cards[index])) continue;
                --remaining[seat];
                const std::uint64_t downstream = ways(index + 1, remaining);
                ++remaining[seat];
                if (downstream > 0) {
                    candidates.push_back({seat, downstream});
                    total += downstream;
                }
            }
            if (total == 0) {
                valid = false;
                break;
            }
            std::uniform_int_distribution<std::uint64_t> distribution(0, total - 1);
            std::uint64_t choice = distribution(random);
            int selected = candidates.back().seat;
            for (const auto& candidate : candidates) {
                if (choice < candidate.ways) {
                    selected = candidate.seat;
                    break;
                }
                choice -= candidate.ways;
            }
            hands[selected] |= CardMask{1} << cards[index];
            --remaining[selected];
        }
        if (valid && std::all_of(remaining.begin(), remaining.end(), [](int value) { return value == 0; })) {
            result.push_back(hands);
        }
    }
    return result;
}

PlayState make_play_state(const Position& position, const std::array<CardMask, 4>& hands) {
    PlayState state;
    state.hands = hands;
    state.current_seat = position.current_seat;
    state.sitting_seat = sitting_seat(position);
    state.contract = position.contract;
    state.bidder = position.bidder;
    state.trick = position.current_trick;
    state.tricks = position.tricks;
    state.completed = static_cast<int>(position.completed_tricks.size());
    return state;
}

std::vector<int> cards_in(CardMask mask) {
    std::vector<int> result;
    while (mask) {
        const int card = std::countr_zero(mask);
        result.push_back(card);
        mask &= mask - 1;
    }
    return result;
}

int lowest_expendable(CardMask legal, const Contract& contract) {
    const auto choices = cards_in(legal);
    if (choices.empty()) return -1;
    return *std::min_element(choices.begin(), choices.end(), [&](int first, int second) {
        const int first_value = donation_value(first, contract);
        const int second_value = donation_value(second, contract);
        if (first_value != second_value) return first_value < second_value;
        return first < second;
    });
}

int donation_value(int card, const Contract& contract) {
    if (contract.mode == Mode::Trump && effective_suit(card, contract) == contract.trump) {
        if (trick_strength(card, contract) == 100) return 1000;
        if (trick_strength(card, contract) == 99) return 900;
        return 500 + rank_index(card) * 35;
    }
    if (contract.mode == Mode::Low) return (5 - rank_index(card)) * 55;
    return rank_index(card) * 55 + (rank_index(card) == 5 ? 100 : 0);
}

} // namespace beu
