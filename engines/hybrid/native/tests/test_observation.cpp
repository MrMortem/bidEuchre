#include "test_support.hpp"
#include "hybrid/observation.hpp"
#include "hybrid/match.hpp"
#include "hybrid/json_io.hpp"
#include <algorithm>
#include <cmath>
#include <functional>

namespace hybrid::test {
namespace {
Json number(int n) { return Json(static_cast<double>(n)); }
Json game_edit(const Json& root, const std::function<void(Json::Object&)>& edit) {
    auto r=root.object(); auto g=r.at("game").object(); edit(g); r["game"]=Json(g); return Json(r);
}
// Replay only actual actor decisions and the four terminal deliveries. The four
// privileged parity projections in the corpus are never fed to SeatMemory.
struct History { std::vector<Json> positions; std::vector<ActionId> actions; };
std::array<History,4> history(const Json& f) {
    const auto& initial=f.at("initial"); NativeMatch m;
    m.start(deal_from_json(initial),initial.at("dealer").integer(),
        {initial.at("scores").array()[0].integer(),initial.at("scores").array()[1].integer()},
        initial.at("handNumber").integer(),static_cast<std::uint64_t>(initial.at("dealSerial").number()));
    std::array<History,4> result; std::size_t delivered=0;
    for(const auto& step:f.at("steps").array()) {
        auto kind=step.at("kind").string();
        if(kind=="redeal") { m.redeal(deal_from_json(step.at("deal"))); continue; }
        const auto& d=f.at("deliveries").array()[delivered++]; int seat=d.at("seat").integer();
        result[seat].positions.push_back(m.view(seat));
        if(kind=="action") { auto id=static_cast<ActionId>(step.at("id").integer()); result[seat].actions.push_back(id); m.apply(id); }
        else m.apply_illegal_play_penalty(seat);
    }
    for(int seat=0;seat<4;++seat) {
        CHECK("terminal delivery matches observer", f.at("deliveries").array()[delivered++].at("seat").integer()==seat);
        result[seat].positions.push_back(m.view(seat));
    }
    CHECK("only live cadence consumed", delivered==f.at("deliveries").array().size());
    return result;
}
std::array<History,4> history(std::string_view name) { return history(load_case(name)); }
Json rotate(const Json& root,int n) {
    auto r=root.object(); r["seat"]=number((r.at("seat").integer()+n)%4);
    auto g=r.at("game").object();
    for(auto key:{"dealer","currentSeat","bidder"}) if(!g.at(key).is_null()) g[key]=number((g.at(key).integer()+n)%4);
    for(auto key:{"scores","tricksByTeam"}) { auto a=g.at(key).array(); if(n%2) std::swap(a[0],a[1]); g[key]=Json(a); }
    if(!g.at("gameWinner").is_null()) g["gameWinner"]=number((g.at("gameWinner").integer()+n)%2);
    auto players=g.at("players").array();
    for(auto& p:players) { auto o=p.object(); int s=(o.at("seat").integer()+n)%4; o["seat"]=number(s); o["team"]=number(s%2); p=Json(o); } g["players"]=Json(players);
    auto plays=[&](const Json& value) { auto a=value.array(); for(auto& p:a) { auto o=p.object(); o["seat"]=number((o.at("seat").integer()+n)%4); p=Json(o); } return Json(a); };
    g["auction"]=plays(g.at("auction")); g["currentTrick"]=plays(g.at("currentTrick"));
    auto ts=g.at("completedTricks").array(); for(auto& t:ts) { auto o=t.object(); for(auto key:{"leader","winner"}) o[key]=number((o.at(key).integer()+n)%4); o["plays"]=plays(o.at("plays")); t=Json(o); } g["completedTricks"]=Json(ts);
    r["game"]=Json(g); return Json(r);
}
Json inject(const Json& root) {
    int seat=root.at("seat").integer(); auto r=root.object(); r["nextDeal"]=Json(std::string("SECRET"));
    auto g=r.at("game").object(); g["nextDeal"]=Json(std::string("SECRET")); g["events"]=Json(Json::Array{Json(std::string("SECRET"))});
    auto ps=g.at("players").array(); for(auto& p:ps) { auto o=p.object(); o["name"]=Json(std::string("SECRET")); o["private"]=Json(std::string("SECRET"));
        if(o.at("seat").integer()!=seat) o["cards"]=Json(Json::Array{Json(std::string("SECRET"))}); p=Json(o); } g["players"]=Json(ps);
    auto la=g.at("legalActions").object(); la["hidden"]=Json(std::string("SECRET")); g["legalActions"]=Json(la); r["game"]=Json(g); return Json(r);
}
bool near(float a,float b) { return std::abs(a-b)<0.000001f; }
void exercise_history(const History& h,bool uninterrupted=true) {
    SeatMemory clean,dirty,rotated;
    for(std::size_t i=0;i<h.positions.size();++i) {
        auto a=clean.observe(h.positions[i]); auto duplicate=clean.observe(h.positions[i]);
        auto b=dirty.observe(inject(h.positions[i])); auto c=rotated.observe(rotate(h.positions[i],1));
        CHECK("duplicate observation features stable", a.features==duplicate.features);
        CHECK("duplicate observations do not double public counts", a.summary==duplicate.summary);
        CHECK("private inputs invariant", a.features==b.features);
        CHECK("retained position strips privileged injected fields", json_stringify(b.position).find("SECRET")==std::string::npos);
        for(const auto& p:b.position.at("game").at("players").array()) if(p.at("seat").integer()!=b.position.at("seat").integer()) CHECK("policy object has no opponent cards", p.at("cards").is_null());
        CHECK("role relative encoding invariant", a.features==c.features);
        CHECK("all features finite", std::all_of(a.features.begin(),a.features.end(),[](float v){return std::isfinite(v);}));
        if(i<h.actions.size()) { clean.record_own_action(h.actions[i]); dirty.record_own_action(h.actions[i]); rotated.record_own_action(h.actions[i]); }
    }
    auto replay=SeatMemory::from_private_history(h.positions,h.actions);
    CHECK("private history resumes identical features", replay.observe(h.positions.back()).features==clean.observe(h.positions.back()).features);
    if(!uninterrupted) return;
    SeatMemory skipped;
    auto late=skipped.observe(h.positions.back()); auto sequential=clean.observe(h.positions.back());
    CHECK("skipped public snapshots recover summary", late.summary==sequential.summary);
    CHECK("midhand restart does not invent initial hand", !late.recall.initial_hand);
    CHECK("initial hand learned at first auction delivery", sequential.recall.initial_hand.has_value());
}
}
void test_observation() {
    std::ifstream corpus(HYBRID_FIXTURE_PATH); std::string line;
    while(std::getline(corpus,line)) {
        auto fixture=beu::json::parse(line); auto hs=history(fixture);
        bool uninterrupted=!fixture.at("id").string().starts_with("redeal-");
        for(const auto& h:hs) exercise_history(h,uninterrupted);
    }
    for(auto name:{"partners-best-same-card","partners-best-return-other"}) {
        auto hs=history(name); const auto& bidder=hs[0]; auto m=SeatMemory::from_private_history(bidder.positions,bidder.actions);
        auto o=m.observe(bidder.positions.back());
        CHECK("bidder initial six clubs", o.recall.initial_hand==std::optional<beu::CardMask>(63));
        CHECK("bidder sent nine clubs", o.recall.sent_exchange==std::optional<beu::CardMask>(1));
        CHECK("return reconstructed including previously played own card", o.recall.received_exchange==std::optional<beu::CardMask>(name==std::string_view("partners-best-same-card")?1:4096));
        auto partner=SeatMemory::from_private_history(hs[2].positions,hs[2].actions).observe(hs[2].positions.back());
        CHECK("partner knows received card", partner.recall.received_exchange==o.recall.sent_exchange);
        CHECK("partner knows returned card", partner.recall.sent_exchange==o.recall.received_exchange);
        // Stop observing after sending until terminal: reconstruct via public own
        // plays even though the return and every playing decision were skipped.
        SeatMemory sparse;
        for(std::size_t i=0;i<bidder.actions.size();++i) {
            sparse.observe(bidder.positions[i]); sparse.record_own_action(bidder.actions[i]);
            if(bidder.actions[i]>=13 && bidder.actions[i]<=36) break;
        }
        CHECK("bidder return survives skipped own plays", sparse.observe(bidder.positions.back()).recall.received_exchange==o.recall.received_exchange);
        CHECK("own action history retained", o.recall.own_actions==bidder.actions);
    }
    auto h=history("high")[0]; SeatMemory m; auto first=m.observe(h.positions.front());
    CHECK("known empty exchange differs from unknown", first.recall.sent_exchange==std::optional<beu::CardMask>(0) && first.features[schema::main_blocks_sent_exchange_offset+schema::recall_fields_known_offset]==1);
    // Independent full-vector golden for the authority High opening decision.
    Features expected{}; expected[1]=expected[11]=expected[12]=1;
    for(int seat=0;seat<4;++seat) expected[19+seat]=6.0f/7;
    for(int card:{0,4,8,12,16,20}) expected[27+card]=expected[51+card]=1;
    for(int index:{75,100,125,174,185,187,194,198,1074}) expected[index]=1;
    for(int action=0;action<=6;++action) expected[1075+action]=1;
    expected[1136]=expected[1138]=2.0f/7; expected[1137]=expected[1139]=1.0f/7;
    for(int seat=0;seat<4;++seat) {
        expected[1156+8*seat+1]=expected[1156+8*seat+2]=0.5f;
        for(int mode=5;mode<=7;++mode) expected[1156+8*seat+mode]=1.0f/3;
    }
    CHECK("golden all 1188 feature slots", first.features==expected);
    auto end=SeatMemory::from_private_history(h.positions,h.actions).observe(h.positions.back());
    CHECK("completed trick presence", end.features[320]==1);
    CHECK("completed trick leader relative seat zero", end.features[321]==1);
    CHECK("completed trick winner relative seat three", end.features[328]==1);
    CHECK("completed first card nine clubs", end.features[334]==1);
    CHECK("terminal current seat none", end.features[16]==1);
    CHECK("terminal legal mask empty", std::none_of(end.legal.begin(),end.legal.end(),[](bool v){return v;}));
    CHECK("numeric bid smoothed pass rate", near(end.features[schema::main_blocks_public_session_summaries_offset+1],1.0f/3));
    CHECK("numeric bid average normalized", near(end.features[schema::main_blocks_public_session_summaries_offset+3],4.0f/6));
    CHECK("high mode Dirichlet smoothing", near(end.features[schema::main_blocks_public_session_summaries_offset+5],0.5f));
    auto repeated=SeatMemory::from_private_history(h.positions,h.actions);
    repeated.observe(h.positions.front());
    CHECK("older duplicate deal locations are not recounted", repeated.observe(h.positions.back()).summary==end.summary);
    auto changed=game_edit(h.positions.back(),[](auto& g){g["dealSerial"]=number(2);});
    auto cleared=m.observe(changed);
    CHECK("changed serial clears initial recall", !cleared.recall.initial_hand);
    CHECK("serial not a feature", SeatMemory{}.observe(changed).features==SeatMemory{}.observe(h.positions.back()).features);
    auto legacy=[](const Json& p){return game_edit(p,[](auto& g){g.erase("dealSerial");});};
    SeatMemory legacyMemory; legacyMemory.observe(legacy(h.positions.front()));
    CHECK("legacy justified monotone history retains initial", legacyMemory.observe(legacy(h.positions.back())).recall.initial_hand.has_value());
    // A legacy restart at a later exchange cannot establish initial six cards.
    auto pb=history("partners-best-return-other")[0]; SeatMemory legacyRestart;
    legacyRestart.observe(legacy(pb.positions[0]));
    auto mid=legacy(pb.positions.back()); legacyRestart.observe(mid);
    auto rewind=legacy(pb.positions[pb.positions.size()-2]);
    CHECK("legacy unjustified rewind clears recall", !legacyRestart.observe(rewind).recall.initial_hand);
    CHECK("fresh legacy midhand remains unknown", !SeatMemory{}.observe(mid).recall.initial_hand);
    auto invalidHand=game_edit(legacy(h.positions[2]),[](auto& g){
        auto ps=g.at("players").array(); auto p=ps[0].object(); auto cs=p.at("cards").array();
        cs[0]=Json(Json::Object{{"code",Json(std::string("AS"))}}); p["cards"]=Json(cs); ps[0]=Json(p); g["players"]=Json(ps);
    });
    SeatMemory incompatible; incompatible.observe(legacy(h.positions.front()));
    CHECK("unjustified legacy hand change clears recall", !incompatible.observe(invalidHand).recall.initial_hand);
    auto histories=history("redeal-exchange"); auto redealt=SeatMemory::from_private_history(histories[0].positions,histories[0].actions).observe(histories[0].positions.back());
    CHECK("redeal counts both public bids", redealt.summary.seats[0].bid_observations==2);
    CHECK("redeal counts both public contracts", redealt.summary.seats[0].contract_observations==2);
    auto initialNew=std::find_if(histories[0].positions.begin(),histories[0].positions.end(),[](const Json& p){return p.at("game").at("dealSerial").integer()==2;});
    CHECK("redeal initial recall matches new private cards", redealt.recall.initial_hand==std::optional<beu::CardMask>(beu::parse_position(*initialNew).hand));
    auto solo=SeatMemory::from_private_history(pb.positions,pb.actions).observe(pb.positions.back());
    CHECK("solo bids excluded from numeric average", solo.features[1159]==0);
    CHECK("solo binary rate Beta smoothing", near(solo.features[1158],2.0f/3));
    auto bower=history("bower-draw-left")[3];
    auto bowerEnd=SeatMemory{}.observe(bower.positions[2]);
    CHECK("following left bower is not public trump void", bowerEnd.features[1140]==0);
    CHECK("left bower counted in effective trump suit", SeatMemory{}.observe(bower.positions[1]).features[1136]==1.0f/7);
    auto seven=history("partners-best-return-other")[2]; SeatMemory sevenMemory;
    sevenMemory.observe(seven.positions[0]); sevenMemory.record_own_action(seven.actions[0]);
    auto exchange=sevenMemory.observe(seven.positions[1]);
    CHECK("partner seven card count normalized to one", exchange.features[19]==1);
    CHECK("partner remembers receipt before sending", exchange.recall.received_exchange==std::optional<beu::CardMask>(1));
    auto duplicates=h.positions; duplicates.insert(duplicates.begin()+1,duplicates.front());
    CHECK("history duplicate consumes action once", SeatMemory::from_private_history(duplicates,h.actions).observe(h.positions.back()).recall==end.recall);
    CHECK("history rejects unmatched actions", rejects([&]{SeatMemory::from_private_history({},h.actions);}));
    auto scores=std::array<int,2>{-8,41}; auto boundaryBefore=encode_boundary(scores,2,0,end.summary);
    NativeMatch a,b; a.start(deal_from_json(load_case("high").at("initial")),2,{0,0}); b.start(deal_from_json(load_case("random-7100").at("initial")),2,{0,0});
    auto boundaryAfter=encode_boundary(scores,2,0,end.summary);
    CHECK("two deals really differ", json_stringify(a.full_state())!=json_stringify(b.full_state()));
    CHECK("next deal invariant", boundaryBefore==boundaryAfter);
    CHECK("dimensions", first.features.size()==1188 && boundaryBefore.size()==38);
    CHECK("negative scores not clipped", boundaryBefore[0]==-0.2f && boundaryBefore[1]==1.025f);
    CHECK("boundary rejects invalid observer", rejects([&]{encode_boundary(scores,2,4,end.summary);}));
    PublicSummary capped; capped.seats[0]={200,50,100,50,250,200,{100,50,50}};
    auto caps=encode_boundary(scores,2,0,capped);
    CHECK("summary count features cap", caps[6]==1 && caps[10]==1);
    CHECK("rates use uncapped denominators", near(caps[7],51.0f/202) && near(caps[11],101.0f/203));
    CHECK("numeric average excludes other bids", near(caps[9],250.0f/300));
    auto malformed=game_edit(h.positions.front(),[](auto& g){g["dealSerial"]=Json(1.5);});
    CHECK("fractional serial rejected", rejects([&]{SeatMemory{}.observe(malformed);}));
    CHECK("seat switch requires new game", rejects([&]{m.observe(rotate(h.positions.front(),1));}));
    m.new_game(); CHECK("new game clears summary", m.observe(h.positions.front()).summary==PublicSummary{});
    CHECK("record requires observation", rejects([]{SeatMemory{}.record_own_action(0);}));
    CHECK("record requires legal action", rejects([&]{m.record_own_action(60);}));
}
}
