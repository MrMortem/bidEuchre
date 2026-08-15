#include "search.hpp"

#include <algorithm>
#include <bit>
#include <limits>

namespace beu {

namespace {

std::size_t mix(std::size_t hash, std::uint64_t value) {
    value ^= value >> 30U;
    value *= 0xbf58476d1ce4e5b9ULL;
    value ^= value >> 27U;
    value *= 0x94d049bb133111ebULL;
    value ^= value >> 31U;
    return hash ^ (static_cast<std::size_t>(value) + 0x9e3779b97f4a7c15ULL + (hash << 6U) + (hash >> 2U));
}

int remaining_tricks(const PlayState& state) {
    return 6 - state.completed;
}

std::vector<int> ordered_moves(const PlayState& state, CardMask legal, bool maximizing) {
    auto moves = cards_in(legal);
    std::stable_sort(moves.begin(), moves.end(), [&](int first, int second) {
        const int first_value = donation_value(first, state.contract);
        const int second_value = donation_value(second, state.contract);
        return maximizing ? first_value > second_value : first_value < second_value;
    });
    return moves;
}

} // namespace

DoubleDummySolver::DoubleDummySolver(SearchLimits limits) : limits_(limits) {
    table_.reserve(32768);
}

std::size_t DoubleDummySolver::KeyHash::operator()(const Key& key) const noexcept {
    std::size_t hash = 0x6a09e667f3bcc909ULL;
    for (const auto hand : key.hands) hash = mix(hash, hand);
    for (const auto card : key.trick_cards) hash = mix(hash, card);
    for (const auto seat : key.trick_seats) hash = mix(hash, seat);
    hash = mix(hash, key.current);
    hash = mix(hash, key.trick_size);
    return hash;
}

int DoubleDummySolver::future_tricks(const PlayState& state, int perspective_team) {
    table_.clear();
    nodes_ = 0;
    interrupted_ = false;
    return solve(state, perspective_team, limits_.maximum_depth, 0, remaining_tricks(state));
}

int DoubleDummySolver::solve(const PlayState& state, int perspective_team,
                             int depth, int alpha, int beta) {
    if (state.completed >= 6) return 0;
    if (depth <= 0 || expired()) return rollout(state, perspective_team);

    const Key key = make_key(state);
    const int original_alpha = alpha;
    const int original_beta = beta;
    if (const auto found = table_.find(key); found != table_.end()) {
        const int value = found->second.value;
        if (found->second.bound == Bound::Exact) return value;
        if (found->second.bound == Bound::Lower) alpha = std::max(alpha, value);
        if (found->second.bound == Bound::Upper) beta = std::min(beta, value);
        if (alpha >= beta) return value;
    }

    const int seat = state.current_seat;
    const bool maximizing = seat % 2 == perspective_team;
    const CardMask legal = legal_cards(state.hands[seat], state.trick, state.contract);
    const auto moves = ordered_moves(state, legal, maximizing);
    if (moves.empty()) return rollout(state, perspective_team);

    int best = maximizing ? std::numeric_limits<int>::min() : std::numeric_limits<int>::max();
    for (const int card : moves) {
        PlayState child = state;
        const int before = child.tricks[perspective_team];
        apply_play(child, card);
        const int gain = child.tricks[perspective_team] - before;
        const int value = gain + solve(child, perspective_team, depth - 1,
                                       std::max(0, alpha - gain), std::max(0, beta - gain));
        if (maximizing) {
            best = std::max(best, value);
            alpha = std::max(alpha, best);
        } else {
            best = std::min(best, value);
            beta = std::min(beta, best);
        }
        if (beta <= alpha) {
            break;
        }
    }
    if (!interrupted_) {
        Bound bound = Bound::Exact;
        if (best <= original_alpha) bound = Bound::Upper;
        else if (best >= original_beta) bound = Bound::Lower;
        table_.insert_or_assign(key, Entry{static_cast<std::int8_t>(best), bound});
    }
    return best;
}

int DoubleDummySolver::rollout(const PlayState& input, int perspective_team) const {
    PlayState state = input;
    const int before = state.tricks[perspective_team];
    int guard = 30;
    while (state.completed < 6 && guard-- > 0) {
        const int card = greedy_play(state);
        if (card < 0) break;
        apply_play(state, card);
    }
    return state.tricks[perspective_team] - before;
}

DoubleDummySolver::Key DoubleDummySolver::make_key(const PlayState& state) const {
    Key key;
    key.hands = state.hands;
    key.current = static_cast<std::uint8_t>(state.current_seat);
    key.trick_size = static_cast<std::uint8_t>(state.trick.size());
    for (std::size_t index = 0; index < state.trick.size(); ++index) {
        key.trick_cards[index] = static_cast<std::uint8_t>(state.trick[index].card + 1);
        key.trick_seats[index] = static_cast<std::uint8_t>(state.trick[index].seat + 1);
    }
    return key;
}

bool DoubleDummySolver::expired() {
    if (interrupted_) return true;
    ++nodes_;
    if (nodes_ >= static_cast<std::uint64_t>(limits_.maximum_nodes)) {
        interrupted_ = true;
        return true;
    }
    if ((nodes_ == 1 || (nodes_ & 127U) == 0) &&
        limits_.deadline != std::chrono::steady_clock::time_point{} &&
        std::chrono::steady_clock::now() >= limits_.deadline) {
        interrupted_ = true;
        return true;
    }
    return false;
}

} // namespace beu
