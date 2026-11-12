#include "hybrid/actions.hpp"
#include <algorithm>
#include <array>
#include <set>
#include <stdexcept>

namespace hybrid {
namespace {
template<std::size_t N>
std::size_t index_of(std::string_view name, const std::array<std::string_view, N>& values) {
    const auto it = std::find(values.begin(), values.end(), name);
    if (it == values.end()) throw beu::json::Error("unknown legal action value: " + std::string(name));
    return static_cast<std::size_t>(it - values.begin());
}

const Json::Array& optional_array(const Json& value, std::string_view key) {
    static const Json::Array empty;
    const auto* found = value.find(key);
    return found ? found->array() : empty;
}
} // namespace

std::string format_action(ActionId action) {
    if (action >= schema::action_count) throw std::out_of_range("action id must be 0 through 60");
    return std::string(schema::action_commands[action]);
}

ActionId parse_action(std::string_view command) {
    return static_cast<ActionId>(index_of(command, schema::action_commands));
}

ActionMask legal_mask(const Json& privatePosition) {
    const auto& game = privatePosition.at("game");
    const auto phase = game.at("phase").string();
    (void)index_of(phase, schema::phases);
    const auto& legal = game.at("legalActions");
    (void)legal.object();
    ActionMask result{};
    auto add = [&](std::string_view command) {
        const auto id = parse_action(command);
        if (result[id]) throw beu::json::Error("duplicate legal action");
        result[id] = true;
    };
    if (phase == "Bidding") {
        if (const auto* pass = legal.find("canPass"); pass && pass->boolean()) add("bestaction pass");
        for (const auto& bid : optional_array(legal, "bids")) {
            const auto index = index_of(bid.string(), schema::bid_names);
            add(schema::action_commands[index + 1]);
        }
    } else if (phase == "ChoosingContract") {
        std::set<std::string> seen;
        for (const auto& mode : optional_array(legal, "contractModes")) {
            const auto& name = mode.string();
            if (!seen.insert(name).second) throw beu::json::Error("duplicate contract mode");
            if (name == "High") add("bestaction contract high");
            else if (name == "Low") add("bestaction contract low");
            else if (name == "Trump") {
                for (const auto& suit : legal.at("trumpSuits").array()) {
                    const auto index = index_of(suit.string(), schema::suit_names);
                    add(schema::action_commands[index + 9]);
                }
            } else throw beu::json::Error("unknown contract mode");
        }
    } else if (phase == "ExchangingBidderCard" || phase == "ExchangingPartnerCard" || phase == "Playing") {
        const std::size_t offset = phase == "Playing" ? 37 : 13;
        for (const auto& card : legal.at("cards").array()) {
            const auto index = index_of(card.at("code").string(), schema::card_codes);
            add(schema::action_commands[index + offset]);
        }
    }
    return result;
}
} // namespace hybrid
