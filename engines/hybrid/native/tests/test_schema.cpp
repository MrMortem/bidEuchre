#include "test_support.hpp"
#include "hybrid/actions.hpp"
#include "hybrid/json_io.hpp"
#include <algorithm>
#include <cmath>
#include <limits>
#include <set>
#include <span>

namespace hybrid::test {
void test_schema() {
    CHECK("main features", Features{}.size() == 1188);
    CHECK("boundary features", BoundaryFeatures{}.size() == 38);
    CHECK("output logits", schema::output_count == 165);
    CHECK("last action", parse_action(format_action(60)) == 60);
    std::set<std::string> actions;
    for (int i = 0; i < 61; ++i) {
        const auto id = static_cast<ActionId>(i);
        CHECK("action round trip", parse_action(format_action(id)) == id);
        actions.insert(format_action(id));
    }
    CHECK("unique actions", actions.size() == 61);
    CHECK("pass grammar", format_action(0) == "bestaction pass");
    CHECK("card order", format_action(37) == "bestaction play 9C");
    CHECK("last card", format_action(60) == "bestaction play AS");
    CHECK("bad action id", rejects([] { (void)format_action(61); }));
    for (auto value : {"pass", "bestaction", "bestaction bid 7", "bestaction play ZZ", "bestaction pass extra"})
        CHECK("bad action grammar", rejects([&] { (void)parse_action(value); }));
    for (const auto& layout : {std::span<const schema::Block>(schema::main_blocks), std::span<const schema::Block>(schema::boundary_blocks)}) {
        std::size_t next = 0;
        for (const auto& block : layout) {
            CHECK("block contiguous", block.offset == next);
            next += block.size;
        }
    }
    const auto bidding = beu::json::parse(R"({"game":{"phase":"Bidding","legalActions":{"canPass":true,"bids":["Three","Alone"]}}})");
    auto mask = legal_mask(bidding);
    CHECK("bid mask", mask[0] && mask[1] && mask[6] && !mask[2]);
    const auto contract = beu::json::parse(R"({"game":{"phase":"ChoosingContract","legalActions":{"contractModes":["High","Low","Trump"],"trumpSuits":["Clubs","Spades"]}}})");
    mask = legal_mask(contract);
    CHECK("contract mask", mask[7] && mask[8] && mask[9] && mask[12] && !mask[10]);
    for (auto phase : {"ExchangingBidderCard", "ExchangingPartnerCard", "Playing"}) {
        std::string text = "{\"game\":{\"phase\":\"" + std::string(phase) + "\",\"legalActions\":{\"cards\":[{\"code\":\"9C\"},{\"code\":\"AS\"}]}}}";
        mask = legal_mask(beu::json::parse(text));
        const int offset = std::string_view(phase) == "Playing" ? 37 : 13;
        CHECK("card mask", mask[offset] && mask[offset+23] && !mask[offset+1]);
    }
    for (auto text : {
        R"({"game":{"phase":"Bidding","legalActions":{"bids":["Three","Three"]}}})",
        R"({"game":{"phase":"Bidding","legalActions":{"bids":["Bogus"]}}})",
        R"({"game":{"phase":"Playing","legalActions":{"cards":[{"code":"ZZ"}]}}})",
        R"({"game":{"phase":"Bogus","legalActions":{}}})",
        R"({"game":{"phase":"Playing"}})"})
        CHECK("invalid legal actions", rejects([&] { (void)legal_mask(beu::json::parse(text)); }));
    mask = legal_mask(beu::json::parse(R"({"game":{"phase":"HandComplete","legalActions":{}}})"));
    CHECK("terminal mask empty", std::count(mask.begin(), mask.end(), true) == 0);
    const std::string escaped = "quote\" slash\\ controls\b\f\n\r\t" + std::string(1, '\x01') + " utf8: é";
    Json::Object object;
    object.emplace(escaped, Json(escaped));
    object.emplace("number", Json(1.2345678901234567));
    object.emplace("values", Json(Json::Array{Json(), Json(true), Json(false), Json(-40.5)}));
    const auto decoded = beu::json::parse(json_stringify(Json(std::move(object))));
    CHECK("escaped key and value", decoded.at(escaped).string() == escaped);
    CHECK("finite precision", decoded.at("number").number() == 1.2345678901234567);
    CHECK("array/null/bool", decoded.at("values").array()[0].is_null() && decoded.at("values").array()[1].boolean());
    for (double value : {std::numeric_limits<double>::infinity(), -std::numeric_limits<double>::infinity(), std::numeric_limits<double>::quiet_NaN()})
        CHECK("nonfinite rejected", rejects([&] { (void)json_stringify(Json(value)); }));
    CHECK("missing fixture fails explicitly", rejects([] { (void)load_case("missing-task4-case"); }));
}
}
