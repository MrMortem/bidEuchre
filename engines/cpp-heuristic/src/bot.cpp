#include "bot.hpp"

#include "search.hpp"

#include <algorithm>
#include <bit>
#include <chrono>
#include <cmath>
#include <limits>
#include <numeric>
#include <stdexcept>

namespace beu {

namespace {

constexpr std::array<int, 6> all_bids{3, 4, 5, 6, 7, 8};

struct HandOutcome {
    std::array<int, 2> tricks{};
    int bidder = -1;
    Contract contract;
};

double trump_control(CardMask hand, int trump) {
    Contract contract{3, Mode::Trump, trump, true};
    double value = 0.0;
    int trump_count = 0;
    for (const int card : cards_in(hand)) {
        if (effective_suit(card, contract) == trump) {
            ++trump_count;
            const int strength = trick_strength(card, contract);
            if (strength == 100) value += 2.00;
            else if (strength == 99) value += 1.75;
            else {
                static constexpr std::array<double, 6> weights{0.15, 0.30, 0.0, 0.55, 0.85, 1.25};
                value += weights[rank_index(card)];
            }
        } else if (rank_index(card) == 5) {
            value += 0.70;
        }
    }
    if (trump_count > 2) value += 0.40 * (trump_count - 2);
    std::array<int, 4> lengths{};
    for (const int card : cards_in(hand)) ++lengths[effective_suit(card, contract)];
    for (int suit = 0; suit < 4; ++suit) {
        if (suit != trump && lengths[suit] == 0) value += 0.20;
        else if (suit != trump && lengths[suit] == 1) value += 0.10;
    }
    return value;
}

double no_trump_control(CardMask hand, Mode mode) {
    double value = 0.0;
    for (int suit = 0; suit < 4; ++suit) {
        int run = 0;
        if (mode == Mode::High) {
            for (int rank = 5; rank >= 0; --rank) {
                if (hand & (CardMask{1} << (suit * 6 + rank))) ++run;
                else break;
            }
        } else {
            for (int rank = 0; rank < 6; ++rank) {
                if (hand & (CardMask{1} << (suit * 6 + rank))) ++run;
                else break;
            }
        }
        value += run;
        if (run == 0) {
            // Secondary controls become winners after the missing extreme is
            // driven out, so give them useful (but deliberately modest) credit.
            const int second = suit * 6 + (mode == Mode::High ? 4 : 1);
            const int third = suit * 6 + (mode == Mode::High ? 3 : 2);
            if (hand & (CardMask{1} << second)) value += 0.30;
            if (hand & (CardMask{1} << third)) value += 0.12;
        }
    }
    return value;
}

PlayState finish_greedy(PlayState state) {
    int guard = 30;
    while (state.completed < 6 && guard-- > 0) apply_play(state, greedy_play(state));
    if (state.completed != 6) throw std::runtime_error("play simulation did not finish");
    return state;
}

bool contains(const std::vector<int>& values, int value) {
    return std::find(values.begin(), values.end(), value) != values.end();
}

std::string lower_ascii(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char character) {
        return static_cast<char>(std::tolower(character));
    });
    return value;
}

std::uint64_t canonical_position_hash(const Position& position) {
    std::uint64_t hash = 0xcbf29ce484222325ULL;
    auto add = [&](std::uint64_t value) {
        hash ^= value + 0x9e3779b97f4a7c15ULL + (hash << 6U) + (hash >> 2U);
        hash *= 0x100000001b3ULL;
    };
    add(position.seat);
    add(stable_hash(position.phase));
    add(position.hand_number);
    add(position.dealer + 1);
    add(position.current_seat + 1);
    add(position.hand);
    add(static_cast<std::uint32_t>(position.scores[0]));
    add(static_cast<std::uint32_t>(position.scores[1]));
    for (int seat = 0; seat < 4; ++seat) {
        add(position.card_counts[seat]);
        add(position.sitting[seat]);
    }
    for (const auto& [seat, bid] : position.auction) {
        add(seat);
        add(bid);
    }
    add(position.high_bid);
    add(position.bidder + 1);
    add(position.contract.bid);
    add(static_cast<int>(position.contract.mode));
    add(position.contract.trump + 1);
    for (const auto& trick : position.completed_tricks) {
        add(trick.winner + 1);
        for (const auto& play : trick.plays) {
            add(play.seat);
            add(play.card);
        }
    }
    for (const auto& play : position.current_trick) {
        add(play.seat);
        add(play.card);
    }
    add(position.tricks[0]);
    add(position.tricks[1]);
    add(position.legal.can_pass);
    auto bids = position.legal.bids;
    auto modes = position.legal.modes;
    auto suits = position.legal.trump_suits;
    auto cards = position.legal.cards;
    std::sort(bids.begin(), bids.end());
    std::sort(modes.begin(), modes.end());
    std::sort(suits.begin(), suits.end());
    std::sort(cards.begin(), cards.end());
    add(bids.size());
    for (const int bid : bids) add(bid);
    add(modes.size());
    for (const Mode mode : modes) add(static_cast<int>(mode));
    add(suits.size());
    for (const int suit : suits) add(suit);
    add(cards.size());
    for (const int card : cards) add(card);
    return hash;
}

} // namespace

HeuristicBot::HeuristicBot(BotOptions options) : options_(options) {}

void HeuristicBot::new_game() {
    position_.reset();
    position_hash_ = 0;
    remembered_hand_number_ = -1;
    remembered_hand_ = 0;
    received_exchange_card_ = -1;
    sent_exchange_card_ = -1;
}

void HeuristicBot::set_position(Position position) {
    const bool same_number_redeal = position_ &&
        position.hand_number == remembered_hand_number_ &&
        position.phase == "Bidding" && position.auction.empty() &&
        (position_->phase != "Bidding" || !position_->auction.empty() ||
         received_exchange_card_ >= 0 || sent_exchange_card_ >= 0);
    if (position.hand_number != remembered_hand_number_ || same_number_redeal) {
        remembered_hand_number_ = position.hand_number;
        remembered_hand_ = position.hand;
        received_exchange_card_ = -1;
        sent_exchange_card_ = -1;
    } else {
        const CardMask added = position.hand & ~remembered_hand_;
        if (position.phase == "ExchangingPartnerCard" && std::popcount(added) == 1) {
            received_exchange_card_ = std::countr_zero(added);
        }
        remembered_hand_ = position.hand;
    }
    // Seed only from strategically relevant fields. Player names, event prose,
    // JSON property order, and unknown additive fields cannot alter a decision.
    position_hash_ = canonical_position_hash(position);
    position_ = std::move(position);
}

void HeuristicBot::set_option(const std::string& raw_name, const std::string& value) {
    const std::string name = lower_ascii(raw_name);
    try {
        if (name == "samples") options_.samples = std::clamp(std::stoi(value), 16, 4096);
        else if (name == "playtimems") options_.play_time_ms = std::clamp(std::stoi(value), 20, 5000);
        else if (name == "searchdepth") options_.search_depth = std::clamp(std::stoi(value), 1, 30);
        else if (name == "searchnodes") options_.search_nodes = std::clamp(std::stoi(value), 1000, 2000000);
        else if (name == "seed") options_.seed = std::stoull(value);
    } catch (const std::exception&) {
        // BEUCI options are advisory. Invalid values leave the prior setting intact.
    }
}

std::string HeuristicBot::choose_action() {
    if (!position_) throw std::runtime_error("a position must be supplied before go");
    if (position_->phase == "Bidding") return choose_bid();
    if (position_->phase == "ChoosingContract") return choose_contract();
    if (position_->phase == "ExchangingBidderCard" || position_->phase == "ExchangingPartnerCard") {
        return choose_exchange();
    }
    if (position_->phase == "Playing") return choose_play();
    throw std::runtime_error("position has no legal action for this phase");
}

std::vector<std::array<CardMask, 4>> HeuristicBot::sample_deals(
    int count, std::chrono::steady_clock::time_point deadline) const {
    const Position& position = *position_;
    const HiddenDealContext context = hidden_context(position);
    std::mt19937_64 random(random_seed(0x9e3779b97f4a7c15ULL));
    const int requested = std::clamp(count, 1, 4096);
    const int pool_count = std::max(requested, requested * 3);
    const int forced_seat = sent_exchange_card_ >= 0 && position.bidder == position.seat
        ? (position.bidder + 2) % 4 : -1;
    auto pool = sample_hidden_deals(position, context, random, pool_count,
                                    sent_exchange_card_, forced_seat, deadline);
    if (pool.empty()) throw std::runtime_error("could not construct a hidden-card deal");

    std::vector<double> weights;
    weights.reserve(pool.size());
    for (const auto& hands : pool) weights.push_back(public_history_likelihood(hands));

    const std::size_t output_count = std::min<std::size_t>(requested, pool.size());
    const auto [minimum, maximum] = std::minmax_element(weights.begin(), weights.end());
    if (*maximum - *minimum < 1e-12) {
        pool.resize(output_count);
        return pool;
    }
    std::discrete_distribution<std::size_t> select(weights.begin(), weights.end());
    std::vector<std::array<CardMask, 4>> deals;
    deals.reserve(output_count);
    for (std::size_t index = 0; index < output_count; ++index) {
        deals.push_back(pool[select(random)]);
    }
    return deals;
}

double HeuristicBot::public_history_likelihood(
    const std::array<CardMask, 4>& remaining_hands) const {
    const Position& position = *position_;
    const bool partners_best_changed_hands = position.contract.valid &&
        position.contract.bid == 7 && position.phase != "ExchangingBidderCard";
    const int exchanged_partner = position.bidder >= 0 ? (position.bidder + 2) % 4 : -1;

    auto original_hands = remaining_hands;
    for (const auto& trick : position.completed_tricks) {
        for (const auto& play : trick.plays) original_hands[play.seat] |= CardMask{1} << play.card;
    }
    for (const auto& play : position.current_trick) {
        original_hands[play.seat] |= CardMask{1} << play.card;
    }

    int standing = 0;
    int bidder = -1;
    double log_likelihood = 0.0;
    int observations = 0;
    for (const auto& [seat, actual] : position.auction) {
        const bool exchange_changed_this_hand = partners_best_changed_hands &&
            (seat == position.bidder || seat == exchanged_partner);
        if (seat != position.seat && !exchange_changed_this_hand) {
            const bool forced = seat == position.dealer && standing == 0;
            const bool partner_winning = bidder >= 0 && bidder % 2 == seat % 2;
            const int predicted = modeled_bid(original_hands[seat], standing, forced, partner_winning);
            double likelihood = 0.10;
            if (actual == predicted) likelihood = 0.92;
            else if (actual == 0 || predicted == 0) likelihood = 0.18;
            else likelihood = std::max(0.12, 0.62 * std::exp(-0.65 * std::abs(actual - predicted)));
            log_likelihood += std::log(likelihood);
            ++observations;
        }
        if (actual > 0) {
            standing = actual;
            bidder = seat;
        }
    }

    if (position.contract.valid && !partners_best_changed_hands &&
        position.bidder >= 0 && position.bidder != position.seat) {
        const Contract expected = modeled_contract(original_hands[position.bidder], position.contract.bid);
        double likelihood = 0.14;
        if (expected.mode == position.contract.mode && expected.trump == position.contract.trump) likelihood = 0.90;
        else if (expected.mode == position.contract.mode) likelihood = 0.38;
        log_likelihood += std::log(likelihood);
        ++observations;
    }
    if (observations == 0) return 1.0;
    // Geometric averaging prevents a long public history from collapsing the
    // particle set while still favoring deals consistent with observed choices.
    return std::clamp(std::exp(log_likelihood / observations), 0.10, 1.0);
}

int HeuristicBot::modeled_bid(CardMask hand, int standing_bid, bool forced,
                              bool partner_winning) const {
    double trump_only = 0.0;
    for (int suit = 0; suit < 4; ++suit) trump_only = std::max(trump_only, trump_control(hand, suit));
    const double high = no_trump_control(hand, Mode::High);
    const double low = no_trump_control(hand, Mode::Low);
    const double numeric = std::max({trump_only, high, low});
    int desired = 0;
    if (trump_only >= 7.15 || high >= 5.45 || low >= 5.45) desired = 8;
    else if (trump_only >= 6.0) desired = 7;
    else if (numeric >= 6.0) desired = 6;
    else if (numeric >= 5.0) desired = 5;
    else if (numeric >= 4.1) desired = 4;
    else if (trump_only >= 3.25) desired = 3;

    if (partner_winning && desired <= std::max(standing_bid + 1, 6)) desired = 0;
    if (desired <= standing_bid) desired = 0;
    if (forced && standing_bid == 0 && desired == 0) desired = 3;
    return desired;
}

Contract HeuristicBot::modeled_contract(CardMask hand, int bid) const {
    Contract best{bid, Mode::Trump, 0, true};
    double best_value = -1.0;
    for (int suit = 0; suit < 4; ++suit) {
        const double value = trump_control(hand, suit);
        if (value > best_value) {
            best_value = value;
            best = {bid, Mode::Trump, suit, true};
        }
    }
    if (bid != 3 && bid != 7) {
        const double high = no_trump_control(hand, Mode::High);
        const double low = no_trump_control(hand, Mode::Low);
        if (high > best_value) {
            best_value = high;
            best = {bid, Mode::High, -1, true};
        }
        if (low > best_value) best = {bid, Mode::Low, -1, true};
    }
    return best;
}

int HeuristicBot::select_partner_return(CardMask hand, const Contract& contract) const {
    const auto cards = cards_in(hand);
    if (cards.empty()) throw std::runtime_error("empty exchange hand");
    return *std::max_element(cards.begin(), cards.end(), [&](int first, int second) {
        const int first_value = donation_value(first, contract);
        const int second_value = donation_value(second, contract);
        if (first_value != second_value) return first_value < second_value;
        // Returning the received card is legal but rarely useful; break exact ties against it.
        if (first == received_exchange_card_) return true;
        if (second == received_exchange_card_) return false;
        return first > second;
    });
}

int HeuristicBot::select_bidder_discard(CardMask hand, const Contract& contract) const {
    const auto cards = cards_in(hand);
    if (cards.empty()) throw std::runtime_error("empty exchange hand");
    return *std::min_element(cards.begin(), cards.end(), [&](int first, int second) {
        auto retention = [&](int card) {
            int value = donation_value(card, contract);
            const int suit = effective_suit(card, contract);
            int length = 0;
            for (const int held : cards_in(hand)) if (effective_suit(held, contract) == suit) ++length;
            if (suit != contract.trump && length == 1) value -= 90; // Create a useful void.
            return value;
        };
        const int first_value = retention(first);
        const int second_value = retention(second);
        return first_value != second_value ? first_value < second_value : first < second;
    });
}

void HeuristicBot::apply_exchange(std::array<CardMask, 4>& hands, const Contract& contract,
                                  int bidder, int forced_discard) const {
    const int partner = (bidder + 2) % 4;
    const int discard = forced_discard >= 0 ? forced_discard
                                             : select_bidder_discard(hands[bidder], contract);
    hands[bidder] &= ~(CardMask{1} << discard);
    hands[partner] |= CardMask{1} << discard;
    const int returned = select_partner_return(hands[partner], contract);
    hands[partner] &= ~(CardMask{1} << returned);
    hands[bidder] |= CardMask{1} << returned;
}

double HeuristicBot::completed_utility(const PlayState& state, int perspective_team) const {
    const int bidding_team = state.bidder % 2;
    const int defending_team = 1 - bidding_team;
    std::array<int, 2> delta{};
    const int bidding_tricks = state.tricks[bidding_team];
    const int defending_tricks = state.tricks[defending_team];
    if (state.contract.bid == 7) delta[bidding_team] = bidding_tricks == 6 ? 12 : -12;
    else if (state.contract.bid == 8) delta[bidding_team] = bidding_tricks == 6 ? 24 : -24;
    else delta[bidding_team] = bidding_tricks >= state.contract.required_tricks()
        ? bidding_tricks : -state.contract.required_tricks();
    delta[defending_team] = defending_tricks;

    const int our_score = position_->scores[perspective_team] + delta[perspective_team];
    const int their_score = position_->scores[1 - perspective_team] + delta[1 - perspective_team];
    double utility = static_cast<double>(delta[perspective_team] - delta[1 - perspective_team]);
    const bool we_win = our_score >= 40 && our_score > their_score;
    const bool they_win = their_score >= 40 && their_score > our_score;
    if (we_win) utility += 200.0;
    if (they_win) utility -= 200.0;
    // A small match-position term rewards building/protecting a lead without drowning out hand score.
    utility += 0.015 * (our_score - their_score);
    return utility;
}

PlayState HeuristicBot::searched_finish(PlayState state, int perspective_team) const {
    SearchLimits limits{
        std::min(options_.search_nodes, 4000),
        std::min(options_.search_depth, 8),
        {}};
    DoubleDummySolver solver(limits);
    const int current = state.tricks[perspective_team];
    const int final = current + solver.future_tricks(state, perspective_team);
    state.tricks[perspective_team] = final;
    state.tricks[1 - perspective_team] = 6 - final;
    state.completed = 6;
    state.trick.clear();
    return state;
}

std::vector<double> HeuristicBot::auction_action_values(
    int candidate, const std::vector<std::array<CardMask, 4>>& deals) const {
    const Position& position = *position_;
    const int perspective = position.seat % 2;
    std::vector<double> values;
    values.reserve(deals.size());
    const int refinement_count = std::min<int>(12, std::max<int>(4, deals.size() / 16));
    for (std::size_t deal_index = 0; deal_index < deals.size(); ++deal_index) {
        auto hands = deals[deal_index];
        int high_bid = position.high_bid;
        int bidder = position.bidder;
        if (candidate != 0) {
            high_bid = candidate;
            bidder = position.seat;
        }
        int seat = (position.seat + 1) % 4;
        const int remaining_turns = 4 - static_cast<int>(position.auction.size()) - 1;
        for (int turn = 0; turn < remaining_turns; ++turn) {
            const bool forced = seat == position.dealer && high_bid == 0;
            const bool partner_winning = bidder >= 0 && bidder % 2 == seat % 2;
            const int bid = modeled_bid(hands[seat], high_bid, forced, partner_winning);
            if (bid > high_bid) {
                high_bid = bid;
                bidder = seat;
            }
            seat = (seat + 1) % 4;
        }
        if (bidder < 0) {
            bidder = position.dealer;
            high_bid = 3;
        }
        Contract contract = modeled_contract(hands[bidder], high_bid);
        if (contract.bid == 7) apply_exchange(hands, contract, bidder);
        const int sit = contract.partner_sits() ? (bidder + 2) % 4 : -1;
        PlayState state{hands, next_active(position.dealer, sit), sit, contract, bidder, {}, {0, 0}, 0};
        state = static_cast<int>(deal_index) < refinement_count
            ? searched_finish(state, perspective) : finish_greedy(state);
        values.push_back(completed_utility(state, perspective));
    }
    return values;
}

std::string HeuristicBot::choose_bid() {
    const Position& position = *position_;
    if (position.legal.bids.empty() && !position.legal.can_pass) {
        throw std::runtime_error("bidding position has no legal action");
    }
    const auto deals = sample_deals(options_.samples);
    struct Candidate { int bid; double value; std::vector<double> outcomes; };
    std::vector<Candidate> candidates;
    auto add_candidate = [&](int bid) {
        auto outcomes = auction_action_values(bid, deals);
        const double mean = std::accumulate(outcomes.begin(), outcomes.end(), 0.0) / outcomes.size();
        candidates.push_back({bid, mean, std::move(outcomes)});
    };
    if (position.legal.can_pass) add_candidate(0);
    for (const int bid : position.legal.bids) add_candidate(bid);

    auto best = std::max_element(candidates.begin(), candidates.end(), [](const auto& first, const auto& second) {
        if (std::abs(first.value - second.value) > 1e-9) return first.value < second.value;
        // Stable tie-break: pass, then the least risky sufficient bid.
        return first.bid > second.bid;
    });
    if (best == candidates.end()) throw std::runtime_error("bidding evaluation produced no action");

    if (position.legal.can_pass && best->bid != 0) {
        const auto pass = std::find_if(candidates.begin(), candidates.end(),
                                       [](const Candidate& candidate) { return candidate.bid == 0; });
        if (pass != candidates.end()) {
            const std::size_t sample_count = std::min(best->outcomes.size(), pass->outcomes.size());
            double mean_difference = 0.0;
            for (std::size_t index = 0; index < sample_count; ++index) {
                mean_difference += best->outcomes[index] - pass->outcomes[index];
            }
            mean_difference /= sample_count;
            double squared = 0.0;
            for (std::size_t index = 0; index < sample_count; ++index) {
                const double difference = best->outcomes[index] - pass->outcomes[index];
                squared += (difference - mean_difference) * (difference - mean_difference);
            }
            const double standard_error = sample_count > 1
                ? std::sqrt(squared / (sample_count - 1) / sample_count) : 0.0;
            const double margin = best->bid >= 7 ? 0.45 : 0.15;
            if (mean_difference < margin + 1.28 * standard_error) best = pass;
        }
    }

    // Do not overcall a partner for a negligible modeled gain.
    if (position.bidder >= 0 && position.bidder % 2 == position.seat % 2 && position.legal.can_pass) {
        const double pass_value = candidates.front().value;
        if (best->bid != 0 && best->value < pass_value + (best->bid >= 7 ? 1.5 : 0.75)) best = candidates.begin();
    }
    return best->bid == 0 ? "bestaction pass" : "bestaction bid " + bid_token(best->bid);
}

HeuristicBot::ContractChoice HeuristicBot::evaluate_contract(
    const Contract& contract, const std::vector<std::array<CardMask, 4>>& deals,
    int bidder) const {
    ContractChoice result;
    result.contract = contract;
    const int bidding_team = bidder % 2;
    double utility = 0.0;
    double tricks = 0.0;
    int made = 0;
    const int refinement_count = std::min<int>(12, deals.size());
    for (std::size_t deal_index = 0; deal_index < deals.size(); ++deal_index) {
        auto hands = deals[deal_index];
        if (contract.bid == 7) apply_exchange(hands, contract, bidder);
        const int sit = contract.partner_sits() ? (bidder + 2) % 4 : -1;
        PlayState state{hands, next_active(position_->dealer, sit), sit, contract, bidder, {}, {0, 0}, 0};
        state = static_cast<int>(deal_index) < refinement_count
            ? searched_finish(state, bidding_team) : finish_greedy(state);
        utility += completed_utility(state, bidding_team);
        tricks += state.tricks[bidding_team];
        if (state.tricks[bidding_team] >= contract.required_tricks()) ++made;
    }
    result.utility = utility / deals.size();
    result.tricks = tricks / deals.size();
    result.make_probability = static_cast<double>(made) / deals.size();
    return result;
}

std::string HeuristicBot::choose_contract() {
    const Position& position = *position_;
    if (position.bidder != position.seat || position.high_bid == 0) {
        throw std::runtime_error("contract position does not identify this bidder");
    }
    const auto deals = sample_deals(options_.samples);
    std::vector<ContractChoice> choices;
    for (const Mode mode : position.legal.modes) {
        if (mode == Mode::Trump) {
            for (const int suit : position.legal.trump_suits) {
                choices.push_back(evaluate_contract({position.high_bid, mode, suit, true}, deals, position.seat));
            }
        } else {
            choices.push_back(evaluate_contract({position.high_bid, mode, -1, true}, deals, position.seat));
        }
    }
    const auto best = std::max_element(choices.begin(), choices.end(), [](const auto& first, const auto& second) {
        if (std::abs(first.utility - second.utility) > 1e-9) return first.utility < second.utility;
        if (std::abs(first.make_probability - second.make_probability) > 1e-9) {
            return first.make_probability < second.make_probability;
        }
        return first.tricks < second.tricks;
    });
    if (best == choices.end()) throw std::runtime_error("contract position has no legal contract");
    if (best->contract.mode == Mode::Trump) {
        return "bestaction contract trump " + suit_token(best->contract.trump);
    }
    return "bestaction contract " + mode_token(best->contract.mode);
}

std::string HeuristicBot::choose_exchange() {
    const Position& position = *position_;
    if (position.legal.cards.empty() || !position.contract.valid) {
        throw std::runtime_error("exchange position has no legal card");
    }
    int card = -1;
    if (position.phase == "ExchangingPartnerCard") {
        // The partner's remaining six cards will be dead during play. Evaluate
        // which donation most improves the bidder across possible bidder and
        // defender hands, using only this process's seven visible cards.
        const auto deals = sample_deals(std::max(32, options_.samples / 2));
        const int refinement_count = std::min<int>(8, deals.size());
        double best_value = -1e30;
        for (const int candidate : position.legal.cards) {
            double value = 0.0;
            for (std::size_t deal_index = 0; deal_index < deals.size(); ++deal_index) {
                auto hands = deals[deal_index];
                hands[position.seat] &= ~(CardMask{1} << candidate);
                hands[position.bidder] |= CardMask{1} << candidate;
                const int sit = position.seat;
                PlayState state{hands, next_active(position.dealer, sit), sit, position.contract,
                                position.bidder, {}, {0, 0}, 0};
                state = static_cast<int>(deal_index) < refinement_count
                    ? searched_finish(state, position.bidder % 2) : finish_greedy(state);
                value += completed_utility(state, position.bidder % 2);
            }
            value /= deals.size();
            const int strategic = donation_value(candidate, position.contract);
            const int incumbent = card >= 0 ? donation_value(card, position.contract) : -1;
            if (value > best_value + 1e-9 ||
                (std::abs(value - best_value) <= 1e-9 && strategic > incumbent)) {
                best_value = value;
                card = candidate;
            }
        }
    } else {
        // Evaluate each discard across the same possible partner hands. This captures
        // both void creation and the card the partner is likely to return.
        const auto deals = sample_deals(std::max(32, options_.samples / 2));
        const int refinement_count = std::min<int>(8, deals.size());
        double best_value = -1e30;
        for (const int candidate : position.legal.cards) {
            double value = 0.0;
            for (std::size_t deal_index = 0; deal_index < deals.size(); ++deal_index) {
                auto hands = deals[deal_index];
                apply_exchange(hands, position.contract, position.seat, candidate);
                const int sit = (position.seat + 2) % 4;
                PlayState state{hands, next_active(position.dealer, sit), sit, position.contract,
                                position.seat, {}, {0, 0}, 0};
                state = static_cast<int>(deal_index) < refinement_count
                    ? searched_finish(state, position.seat % 2) : finish_greedy(state);
                value += completed_utility(state, position.seat % 2);
            }
            value /= deals.size();
            if (value > best_value + 1e-9 ||
                (std::abs(value - best_value) <= 1e-9 &&
                 donation_value(candidate, position.contract) < donation_value(card, position.contract))) {
                best_value = value;
                card = candidate;
            }
        }
        sent_exchange_card_ = card;
        remembered_hand_ &= ~(CardMask{1} << card);
    }
    if (!contains(position.legal.cards, card)) throw std::runtime_error("exchange evaluator selected an illegal card");
    return "bestaction exchange " + card_code(card);
}

std::string HeuristicBot::choose_play() {
    const Position& position = *position_;
    if (position.legal.cards.empty() || !position.contract.valid) {
        throw std::runtime_error("playing position has no legal card");
    }
    if (position.legal.cards.size() == 1) return "bestaction play " + card_code(position.legal.cards.front());

    CardMask legal_mask = 0;
    for (const int card : position.legal.cards) legal_mask |= CardMask{1} << card;
    const int fallback_card = lowest_expendable(legal_mask, position.contract);
    const std::string fallback = "bestaction play " + card_code(fallback_card);

    const auto start = std::chrono::steady_clock::now();
    const auto deadline = start +
        std::chrono::milliseconds(options_.play_time_ms);
    const auto sampling_deadline = std::min(
        deadline, start + std::chrono::milliseconds(std::max(1, options_.play_time_ms / 3)));
    std::vector<std::array<CardMask, 4>> deals;
    try {
        deals = sample_deals(options_.samples, sampling_deadline);
    } catch (const std::exception&) {
        // A legal, deterministic response is better than forfeiting a turn if
        // a malformed or transitional snapshot cannot be determinized.
        return fallback;
    }
    if (deals.empty() || std::chrono::steady_clock::now() >= deadline) return fallback;

    const int perspective = position.seat % 2;
    std::vector<double> totals(position.legal.cards.size(), 0.0);
    std::vector<int> counts(position.legal.cards.size(), 0);
    std::vector<std::vector<double>> baseline;
    baseline.reserve(deals.size());

    // Establish a complete paired baseline first. No candidate is allowed to
    // benefit merely because it happened to be searched before the deadline.
    for (std::size_t deal_index = 0; deal_index < deals.size(); ++deal_index) {
        if (std::chrono::steady_clock::now() >= deadline) break;
        const auto& hands = deals[deal_index];
        std::vector<double> row(position.legal.cards.size(), 0.0);
        for (std::size_t index = 0; index < position.legal.cards.size(); ++index) {
            PlayState state = make_play_state(position, hands);
            apply_play(state, position.legal.cards[index]);
            const PlayState outcome = finish_greedy(state);
            row[index] = completed_utility(outcome, perspective);
            totals[index] += row[index];
            ++counts[index];
        }
        baseline.push_back(std::move(row));
    }
    if (baseline.empty()) return fallback;

    // Refine complete common-deal batches with minimax. If the deadline lands
    // inside a batch, discard that batch so every root action retains the same
    // quality of evidence.
    for (std::size_t deal_index = 0; deal_index < baseline.size(); ++deal_index) {
        if (std::chrono::steady_clock::now() >= deadline) break;
        const auto& hands = deals[deal_index];
        std::vector<double> refined(position.legal.cards.size(), 0.0);
        bool complete_batch = true;
        for (std::size_t index = 0; index < position.legal.cards.size(); ++index) {
            if (std::chrono::steady_clock::now() >= deadline) {
                complete_batch = false;
                break;
            }
            PlayState state = make_play_state(position, hands);
            apply_play(state, position.legal.cards[index]);
            const int current_root_tricks = state.tricks[perspective];
            SearchLimits limits{options_.search_nodes, options_.search_depth, deadline};
            DoubleDummySolver solver(limits);
            const int future = solver.future_tricks(state, perspective);
            const int final_root = current_root_tricks + future;
            PlayState outcome = state;
            outcome.tricks[perspective] = final_root;
            outcome.tricks[1 - perspective] = 6 - final_root;
            outcome.completed = 6;
            refined[index] = completed_utility(outcome, perspective);
            if (std::chrono::steady_clock::now() >= deadline) {
                complete_batch = false;
                break;
            }
        }
        if (!complete_batch) break;
        for (std::size_t index = 0; index < position.legal.cards.size(); ++index) {
            totals[index] += refined[index] - baseline[deal_index][index];
        }
    }

    std::size_t best = 0;
    for (std::size_t index = 1; index < totals.size(); ++index) {
        const double value = totals[index] / std::max(1, counts[index]);
        const double incumbent = totals[best] / std::max(1, counts[best]);
        if (value > incumbent + 1e-9) {
            best = index;
        } else if (std::abs(value - incumbent) <= 1e-9 &&
                   donation_value(position.legal.cards[index], position.contract) <
                       donation_value(position.legal.cards[best], position.contract)) {
            best = index;
        }
    }
    const int card = position.legal.cards[best];
    return "bestaction play " + card_code(card);
}

std::uint64_t HeuristicBot::random_seed(std::uint64_t salt) const {
    std::uint64_t value = options_.seed ^ position_hash_ ^ salt;
    value ^= value >> 30U;
    value *= 0xbf58476d1ce4e5b9ULL;
    value ^= value >> 27U;
    value *= 0x94d049bb133111ebULL;
    return value ^ (value >> 31U);
}

std::uint64_t stable_hash(std::string_view value) {
    std::uint64_t hash = 1469598103934665603ULL;
    for (const unsigned char character : value) {
        hash ^= character;
        hash *= 1099511628211ULL;
    }
    return hash;
}

} // namespace beu
