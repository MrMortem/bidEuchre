#include "test_support.hpp"
#include "hybrid/match.hpp"
#include "hybrid/json_io.hpp"
namespace hybrid::test {
void test_match() {
    CHECK("alone failure", score_hand({8,beu::Mode::Low,-1,true},0,{5,1},{0,0}).deltas[0] == -24);
    CHECK("PB success", score_hand({7,beu::Mode::Trump,0,true},0,{6,0},{0,0}).deltas[0] == 12);
    CHECK("PB failure", score_hand({7,beu::Mode::Trump,0,true},0,{5,1},{0,0}).deltas[0] == -12);
    CHECK("alone success", score_hand({8,beu::Mode::High,-1,true},1,{0,6},{0,0}).deltas[1] == 24);
    CHECK("defenders score", score_hand({4,beu::Mode::High,-1,true},1,{3,3},{0,0}).deltas[0] == 3);
    CHECK("higher over forty wins", score_hand({3,beu::Mode::Trump,0,true},0,{3,3},{39,38}).winner == 0);
    CHECK("other higher over forty wins", score_hand({3,beu::Mode::Trump,0,true},0,{3,3},{38,39}).winner == 1);
    CHECK("tie continues", !score_hand({3,beu::Mode::Trump,0,true},0,{3,3},{40,40}).winner);
    auto fixture = load_case("high");
    NativeMatch match;
    match.start(deal_from_json(fixture.at("initial")),3,{0,0});
    CHECK("next hand rejected while bidding", rejects([&]{match.start_next_hand(deal_from_json(fixture.at("initial")));}));
    int offSuitRejected=0;
    for (const auto& step : fixture.at("steps").array()) {
        if (match.full_state().at("phase").string() == "Playing") {
            CHECK("next hand rejected while playing", rejects([&]{match.start_next_hand(deal_from_json(fixture.at("initial")));}));
            auto before = json_stringify(match.full_state());
            for (int id=37; id<61; ++id) if (!match.legal_actions()[id]) {
                const auto state=match.full_state();
                for (const auto& c:state.at("players").array()[state.at("currentSeat").integer()].at("cards").array())
                    if(c.integer()==id-37) ++offSuitRejected;
                CHECK("illegal action rejected", rejects([&]{match.apply(static_cast<ActionId>(id));}));
                CHECK("illegal action atomic", before == json_stringify(match.full_state()));
            }
        }
        match.apply(static_cast<ActionId>(step.at("id").integer()));
    }
    CHECK("off-suit held cards rejected", offSuitRejected>0);
    CHECK("explicit boundary", match.full_state().at("phase").string() == "HandComplete");
    auto result=match.hand_result();
    CHECK("both deltas", result.scoresAfter[0]==result.scoresBefore[0]+result.deltas[0] && result.scoresAfter[1]==result.scoresBefore[1]+result.deltas[1]);
    const auto previousScores=match.full_state().at("scores");
    match.start_next_hand(deal_from_json(fixture.at("initial")));
    CHECK("next dealer", match.full_state().at("dealer").integer()==0);
    CHECK("next identity", match.full_state().at("handNumber").integer()==2);
    CHECK("next serial", match.full_state().at("dealSerial").integer()==2);
    CHECK("scores survive next hand", json_stringify(match.full_state().at("scores"))==json_stringify(previousScores));
    CHECK("history survives next hand", match.full_state().at("handResults").array().size()==1);
    CHECK("result only at boundary", rejects([&]{ (void)match.hand_result(); }));
    for(const auto name:{"near-forty","both-over-forty-zero","both-over-forty-one","tied-threshold-continues","partners-best-success","alone-sitting-lead"}) {
        auto f=load_case(name); const auto& initial=f.at("initial"); auto d=deal_from_json(initial);
        match.start(d,initial.at("dealer").integer(),{initial.at("scores").array()[0].integer(),initial.at("scores").array()[1].integer()});
        for(const auto& step:f.at("steps").array()) {
            match.apply(static_cast<ActionId>(step.at("id").integer()));
            if(std::string(name)=="alone-sitting-lead" && step.at("id").integer()==7)
                CHECK("sitting initial lead skipped", match.full_state().at("currentSeat").integer()==1);
        }
        if(match.hand_result().winner) {
            auto before=json_stringify(match.full_state());
            CHECK("next after win rejected", rejects([&]{match.start_next_hand(d);}));
            CHECK("redeal after win rejected", rejects([&]{match.redeal(d);}));
            CHECK("terminal action rejected", rejects([&]{match.apply(0);}));
            CHECK("terminal rejection atomic", before==json_stringify(match.full_state()));
        } else if(std::string(name)=="tied-threshold-continues") {
            match.start_next_hand(d);
            CHECK("tied above threshold continues", match.full_state().at("phase").string()=="Bidding");
        }
    }
    auto invalid=deal_from_json(fixture.at("initial")); invalid.hands[0]=invalid.hands[1];
    auto before=json_stringify(match.full_state());
    CHECK("invalid deal rejected", rejects([&]{match.start(invalid,3,{0,0});}));
    CHECK("invalid deal atomic", before==json_stringify(match.full_state()));
    CHECK("invalid actor rejected", rejects([&]{(void)match.view(4);}));
}
}
