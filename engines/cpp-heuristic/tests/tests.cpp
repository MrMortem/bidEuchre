#include "bot.hpp"
#include "game.hpp"
#include "json.hpp"
#include "search.hpp"

#include <chrono>
#include <iostream>
#include <random>
#include <stdexcept>
#include <string>

namespace {

int failures = 0;
int checks = 0;

void check(bool condition, const std::string& name) {
    ++checks;
    if (condition) {
        std::cout << "PASS  " << name << '\n';
    } else {
        std::cerr << "FAIL  " << name << '\n';
        ++failures;
    }
}

beu::CardMask mask(std::initializer_list<const char*> codes) {
    beu::CardMask result = 0;
    for (const auto* code : codes) result |= beu::CardMask{1} << beu::parse_card(code);
    return result;
}

void card_and_trick_tests() {
    const beu::Contract hearts{4, beu::Mode::Trump, 2, true};
    check(beu::card_code(beu::parse_card("as")) == "AS", "card codes round-trip");
    check(beu::effective_suit(beu::parse_card("JD"), hearts) == 2,
          "left bower has trump effective suit");

    const auto following = beu::legal_cards(
        mask({"JD", "AC", "9S"}), {{0, beu::parse_card("9H")}}, hearts);
    check(following == mask({"JD"}), "left bower must follow trump");

    const std::vector<beu::CardPlay> plain{
        {0, beu::parse_card("9C")}, {1, beu::parse_card("AC")},
        {2, beu::parse_card("TC")}, {3, beu::parse_card("KC")}};
    check(beu::trick_winner(plain, {4, beu::Mode::High, -1, true}) == 1,
          "high trick chooses ace");
    check(beu::trick_winner(plain, {4, beu::Mode::Low, -1, true}) == 0,
          "low trick chooses nine");

    const std::vector<beu::CardPlay> bowers{
        {0, beu::parse_card("AH")}, {1, beu::parse_card("JD")},
        {2, beu::parse_card("JH")}, {3, beu::parse_card("9H")}};
    check(beu::trick_winner(bowers, hearts) == 2, "right bower beats left bower");
}

void scoring_tests() {
    check(beu::score_utility({4, beu::Mode::High, -1, true}, 0, 4, 2, 0) == 2,
          "made numeric bid uses actual tricks and defender points");
    check(beu::score_utility({4, beu::Mode::High, -1, true}, 0, 3, 3, 0) == -7,
          "set numeric bid uses bid penalty");
    check(beu::score_utility({7, beu::Mode::Trump, 0, true}, 0, 6, 0, 0) == 12,
          "partners best sweep scores twelve");
    check(beu::score_utility({8, beu::Mode::Low, -1, true}, 1, 1, 5, 1) == -25,
          "alone failure includes defender trick points");
}

void search_tests() {
    beu::PlayState state;
    state.hands[0] = mask({"9C"});
    state.hands[1] = mask({"AC"});
    state.hands[2] = mask({"TC"});
    state.hands[3] = mask({"KC"});
    state.current_seat = 0;
    state.contract = {4, beu::Mode::High, -1, true};
    state.bidder = 0;
    state.completed = 5;
    beu::DoubleDummySolver solver({10000, 8, {}});
    check(solver.future_tricks(state, 0) == 0, "double-dummy solver minimizes against bidder team");

    state.contract = {4, beu::Mode::Low, -1, true};
    beu::DoubleDummySolver low_solver({10000, 8, {}});
    check(low_solver.future_tricks(state, 0) == 1, "double-dummy solver handles low ordering");
}

void hidden_deal_tests() {
    beu::Position position;
    position.seat = 0;
    position.phase = "Playing";
    position.hand_number = 1;
    position.current_seat = 0;
    position.dealer = 3;
    position.bidder = 0;
    position.contract = {4, beu::Mode::Trump, 2, true};
    position.hand = mask({"JH", "AH", "KH", "QH", "TH"});
    position.card_counts = {5, 5, 5, 5};
    position.completed_tricks.push_back({0, {
        {0, beu::parse_card("9C")}, {1, beu::parse_card("AS")},
        {2, beu::parse_card("TC")}, {3, beu::parse_card("AC")}}});
    position.tricks = {1, 0};
    const auto context = beu::hidden_context(position);
    check(context.void_suit[1][0], "failure to follow records a hard void");

    std::mt19937_64 random(42);
    std::array<beu::CardMask, 4> hands{};
    check(beu::sample_hidden_hands(position, context, random, hands),
          "a constrained hidden deal can be sampled");
    bool seat_one_has_club = false;
    for (const int card : beu::cards_in(hands[1])) {
        if (beu::effective_suit(card, position.contract) == 0) seat_one_has_club = true;
    }
    check(!seat_one_has_club, "sampled deal respects inferred void");

    beu::Position bower_position = position;
    bower_position.completed_tricks = {{0, {
        {0, beu::parse_card("9H")}, {1, beu::parse_card("JD")},
        {2, beu::parse_card("TH")}, {3, beu::parse_card("QH")}}}};
    const auto bower_context = beu::hidden_context(bower_position);
    check(!bower_context.void_suit[1][2], "left bower follows a trump lead for void inference");

    bower_position.completed_tricks = {{0, {
        {0, beu::parse_card("AD")}, {1, beu::parse_card("JD")},
        {2, beu::parse_card("TD")}, {3, beu::parse_card("QD")}}}};
    const auto diamond_context = beu::hidden_context(bower_position);
    check(diamond_context.void_suit[1][1], "left bower does not follow its printed suit");
}

void json_tests() {
    const auto value = beu::json::parse("{\"seat\":1,\"ok\":true,\"items\":[null,2]}");
    check(value.at("seat").integer() == 1 && value.at("ok").boolean(), "JSON parser reads protocol values");
    check(beu::json::decode_base64url("eyJ4IjoxfQ") == "{\"x\":1}",
          "unpadded base64url decodes");
    bool rejected = false;
    try { (void)beu::json::decode_base64url("invalid!"); }
    catch (const beu::json::Error&) { rejected = true; }
    check(rejected, "malformed base64url is rejected");
}

void bidding_tests() {
    beu::Position position;
    position.seat = 0;
    position.phase = "Bidding";
    position.hand_number = 1;
    position.dealer = 3;
    position.current_seat = 0;
    position.hand = mask({"JH", "JD", "AH", "KH", "QH", "TH"});
    position.card_counts = {6, 6, 6, 6};
    position.legal.can_pass = true;
    position.legal.bids = {3, 4, 5, 6, 7, 8};
    beu::HeuristicBot bot({32, 20, 6, 3000, 42});
    bot.set_position(position);
    check(bot.choose_action() == "bestaction bid alone",
          "a guaranteed six-trump sweep bids Alone");

    std::reverse(position.legal.bids.begin(), position.legal.bids.end());
    beu::HeuristicBot reordered({32, 20, 6, 3000, 42});
    reordered.set_position(position);
    check(reordered.choose_action() == "bestaction bid alone",
          "legal-action ordering cannot perturb a seeded decision");
}

void sitting_partner_tests() {
    beu::PlayState state;
    state.hands[0] = mask({"9C"});
    state.hands[1] = mask({"AC"});
    state.hands[2] = mask({"JH"});
    state.hands[3] = mask({"KC"});
    state.current_seat = 0;
    state.sitting_seat = 2;
    state.contract = {7, beu::Mode::Trump, 2, true};
    state.bidder = 0;
    state.completed = 5;

    beu::apply_play(state, beu::parse_card("9C"));
    beu::apply_play(state, beu::parse_card("AC"));
    check(state.current_seat == 3 && state.hands[2] == mask({"JH"}),
          "Partners Best skips the bidder's sitting partner");
    beu::apply_play(state, beu::parse_card("KC"));
    check(state.completed == 6 && state.hands[2] == mask({"JH"}),
          "a three-player Partners Best trick completes without partner card");
}

void deadline_tests() {
    beu::Position position;
    position.seat = 0;
    position.phase = "Playing";
    position.hand_number = 3;
    position.dealer = 3;
    position.current_seat = 0;
    position.bidder = 0;
    position.contract = {4, beu::Mode::High, -1, true};
    position.hand = mask({"9C", "TC", "JC", "QD", "KH", "AS"});
    position.card_counts = {6, 6, 6, 6};
    position.legal.cards = beu::cards_in(position.hand);

    beu::HeuristicBot bot({4096, 20, 12, 12000, 99});
    bot.set_position(position);
    const auto start = std::chrono::steady_clock::now();
    const std::string action = bot.choose_action();
    const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - start);
    bool legal = false;
    for (const int card : position.legal.cards) {
        if (action == "bestaction play " + beu::card_code(card)) legal = true;
    }
    check(legal, "deadline fallback or search always returns a legal play");
    check(elapsed < std::chrono::milliseconds(300),
          "large sampling request respects the play-time budget with bounded overhead");
}

} // namespace

int main() {
    card_and_trick_tests();
    scoring_tests();
    search_tests();
    hidden_deal_tests();
    json_tests();
    bidding_tests();
    sitting_partner_tests();
    deadline_tests();
    std::cout << '\n' << (checks - failures) << '/' << checks << " tests passed.\n";
    return failures == 0 ? 0 : 1;
}
