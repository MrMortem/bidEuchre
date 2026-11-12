#include "hybrid/match.hpp"
#include "hybrid/actions.hpp"
#include "hybrid/json_io.hpp"
#include <algorithm>
#include <cmath>
#include <fstream>
#include <iostream>
#include <sstream>

namespace hybrid {
namespace {
Json n(int x) { return Json(static_cast<double>(x)); }
Json s(std::string x) { return Json(std::move(x)); }
void equal(const Json& a,const Json& b,const std::string& path) {
    if(a.is_object() && b.is_object()) {
        for(const auto& [key,value]:b.object()) {
            const auto* found=a.find(key);
            if(!found) throw std::runtime_error(path+"."+key+": missing field");
            equal(*found,value,path+"."+key);
        }
        if(a.object().size()==b.object().size()) return;
    } else if(a.is_array() && b.is_array()) {
        if(a.array().size()==b.array().size()) {
            for(std::size_t i=0;i<a.array().size();++i) equal(a.array()[i],b.array()[i],path+"["+std::to_string(i)+"]");
            return;
        }
    } else if(json_stringify(a)==json_stringify(b)) return;
    throw std::runtime_error(path+": actual "+json_stringify(a)+", expected "+json_stringify(b));
}
Json bid(const Json& v) { return v.is_null()?Json():n(beu::parse_bid(v.string())); }
Json contract(const Json& v) {
    if(v.is_null()) return {};
    return Json(Json::Object{{"bid",bid(v.at("bid"))},{"mode",v.at("mode")},
        {"trump",v.at("trump").is_null()?Json():n(beu::parse_suit(v.at("trump").string()))}});
}
Json wire_plays(const Json& v) {
    Json::Array a; for(const auto& p:v.array()) a.emplace_back(Json::Object{{"seat",p.at("seat")},{"card",n(beu::parse_card(p.at("card").at("code").string()))}});
    return Json(a);
}
// Normalize the actual public policy input independently of NativeMatch's fixture projection.
Json normalize_private(const Json& position) {
    auto game=position.at("game").object(); Json::Array players,auction,tricks,legal;
    int observer=position.at("seat").integer();
    for(const auto& p:game.at("players").array()) {
        auto o=p.object(); o.erase("name");
        int seat=o.at("seat").integer(); const auto& cards=o.at("cards");
        if((seat==observer)==cards.is_null()) throw std::runtime_error("private card visibility violated");
        if(!cards.is_null()) {
            std::vector<int> ids; for(const auto& c:cards.array()) ids.push_back(beu::parse_card(c.at("code").string()));
            std::sort(ids.begin(),ids.end()); Json::Array a; for(int c:ids) a.push_back(n(c)); o["cards"]=Json(a);
        }
        players.emplace_back(o);
    }
    game["players"]=Json(players);
    for(const auto& a:game.at("auction").array()) auction.emplace_back(Json::Object{{"seat",a.at("seat")},{"bid",bid(a.at("bid"))}});
    game["auction"]=Json(auction); game["highBid"]=bid(game.at("highBid")); game["contract"]=contract(game.at("contract"));
    game["currentTrick"]=wire_plays(game.at("currentTrick"));
    for(const auto& t:game.at("completedTricks").array()) { auto o=t.object(); o["plays"]=wire_plays(o.at("plays")); tricks.emplace_back(o); }
    game["completedTricks"]=Json(tricks);
    auto mask=legal_mask(position); for(int i=0;i<61;++i) if(mask[i]) legal.push_back(n(i));
    game.erase("legalActions"); game.erase("events"); game["legal"]=Json(legal);
    return Json(game);
}
void check_private(const NativeMatch& match,int seat,const Json& expected,const std::string& path) {
    equal(match.private_projection(seat),expected,path);
    auto wireExpected=expected.object(); wireExpected.erase("handResults");
    equal(normalize_private(match.view(seat)),Json(wireExpected),path+".BEUCI");
}
void snapshot(const NativeMatch& match,const Json& expected,const std::string& path) {
    equal(match.full_state(),expected.at("state"),path+".state");
    if(expected.at("privateViews").array().size()!=4) throw std::runtime_error(path+": expected four views");
    for(int seat=0;seat<4;++seat) check_private(match,seat,expected.at("privateViews").array()[seat],path+".privateViews["+std::to_string(seat)+"]");
}
int action_id(const Json& value) { int id=value.integer(); if(id<0 || id>60) throw std::runtime_error("invalid action ID"); return id; }
} // namespace
void replay_fixture(const Json& fixture) {
    const auto id=fixture.at("id").string();
    try {
        if(fixture.at("schema").string()!="fixture-v1") throw std::runtime_error("unknown fixture schema");
        const auto& initial=fixture.at("initial");
        NativeMatch match;
        const auto& scores=initial.at("scores").array();
        if(scores.size()!=2) throw std::runtime_error("two initial scores required");
        double serial=initial.at("dealSerial").number();
        if(serial<1 || serial>9007199254740991.0 || serial!=std::floor(serial)) throw std::runtime_error("invalid deal serial");
        match.start(deal_from_json(initial),initial.at("dealer").integer(),{scores[0].integer(),scores[1].integer()},initial.at("handNumber").integer(),static_cast<std::uint64_t>(serial));
        snapshot(match,fixture.at("expectedInitial"),"initial");
        const auto& deliveries=fixture.at("deliveries").array(); std::size_t delivered=0;
        auto deliver=[&](int index,const std::string& kind,int seat) {
            if(delivered>=deliveries.size()) throw std::runtime_error("missing delivery");
            const auto& d=deliveries[delivered++];
            equal(d.at("afterStep"),n(index),"delivery.afterStep"); equal(d.at("kind"),s(kind),"delivery.kind"); equal(d.at("seat"),n(seat),"delivery.seat");
            check_private(match,seat,d.at("view"),"delivery["+std::to_string(delivered-1)+"].view");
        };
        int index=-1;
        for(const auto& step:fixture.at("steps").array()) {
            const auto kind=step.at("kind").string();
            std::string path="steps["+std::to_string(index+1)+"]";
            try {
                if(kind=="redeal") {
                    match.redeal(deal_from_json(step.at("deal")));
                    const auto state=match.full_state();
                    for(auto key:{"dealer","scores","handNumber","dealSerial"}) equal(state.at(key),step.at("deal").at(key),path+".deal."+key);
                } else {
                    int seat=step.at("seat").integer(); equal(match.full_state().at("currentSeat"),n(seat),path+".seat");
                    deliver(index,"decision",seat);
                    if(kind=="action") match.apply(static_cast<ActionId>(action_id(step.at("id"))));
                    else if(kind=="penalty") {
                        auto before=json_stringify(match.full_state()); bool rejected=false;
                        try { match.apply(static_cast<ActionId>(action_id(step.at("illegalAction")))); } catch(const std::invalid_argument&) { rejected=true; }
                        if(!rejected || before!=json_stringify(match.full_state())) throw std::runtime_error("illegal play not rejected before mutation");
                        match.apply_illegal_play_penalty(seat);
                    } else throw std::runtime_error("unknown step kind");
                }
                snapshot(match,step.at("expected"),path+".expected");
            } catch(const std::exception& e) { throw std::runtime_error(path+": "+e.what()); }
            ++index;
        }
        auto phase=match.full_state().at("phase").string();
        if(phase!="HandComplete" && phase!="GameComplete") throw std::runtime_error("trace has no terminal boundary");
        for(int seat=0;seat<4;++seat) deliver(index,"terminal",seat);
        if(delivered!=deliveries.size()) throw std::runtime_error("extra private deliveries");
        const auto result=match.hand_result(); const auto state=match.full_state();
        equal(Json(Json::Array{n(result.deltas[0]),n(result.deltas[1])}),
              Json(Json::Array{state.at("handResults").array().back().at("teamZeroDelta"),state.at("handResults").array().back().at("teamOneDelta")}),"hand_result.deltas");
    } catch(const std::exception& e) { throw std::runtime_error(id+": "+e.what()); }
}
} // namespace hybrid
#ifndef HYBRID_REPLAY_LIBRARY
int main(int argc,char** argv) {
    try {
        if((argc!=3 && argc!=5) || std::string(argv[1])!="--fixtures" || (argc==5 && std::string(argv[3])!="--group"))
            throw std::runtime_error("Usage: bideuchre-hybrid-replay --fixtures PATH [--group rare|random]");
        std::string group=argc==5?argv[4]:"all";
        if(group!="all" && group!="rare" && group!="random") throw std::runtime_error("unknown group");
        std::ifstream input(argv[2]); if(!input) throw std::runtime_error("cannot open fixtures");
        std::string line; int count=0;
        while(std::getline(input,line)) {
            if(line.empty()) continue;
            auto fixture=beu::json::parse(line); bool random=fixture.at("id").string().starts_with("random-");
            if((group=="rare" && random) || (group=="random" && !random)) continue;
            hybrid::replay_fixture(fixture); ++count;
        }
        if(!count) throw std::runtime_error("empty fixture selection");
        std::cout<<"Replayed "<<count<<" authority traces ("<<group<<")\n"; return 0;
    } catch(const std::exception& e) { std::cerr<<"Parity failure: "<<e.what()<<'\n'; return 1; }
}
#endif
