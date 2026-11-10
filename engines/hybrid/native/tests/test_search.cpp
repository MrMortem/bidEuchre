#include "test_support.hpp"
#include "hybrid/search.hpp"
#include "hybrid/policy.hpp"
#include "hybrid/json_io.hpp"
#include <algorithm>
#include <cmath>
#include <thread>
namespace hybrid::test {
void test_search() {
    auto model=FrozenModel::load(HYBRID_TEST_MODEL);
    auto fixture=load_case("partners-best-same-card");
    auto init=fixture.at("initial"); NativeMatch match;
    match.start(deal_from_json(init),init.at("dealer").integer(),{0,0});
    std::array<SeatMemory,4> memories;
    int observations=0,boundaries=0;
    SearchHooks hooks;
    hooks.policy=[&](const SeatObservation& o) {
        auto p=beu::parse_position(o.position);
        CHECK("continuation actor only",p.current_seat==p.seat);
        for(int s=0;s<4;++s) CHECK("continuation no opponent cards",s==p.seat || o.position.at("game").at("players").array()[s].at("cards").is_null());
        ++observations; return FrozenPolicy(model).distribution(o);
    };
    hooks.boundary=[&](const BoundaryFeatures& f) { CHECK("card-free boundary 38",f.size()==38);++boundaries;return .5f; };
    RootSearch search(hooks);
    for(const auto& step:fixture.at("steps").array()) {
        int actor=beu::parse_position(match.view(0)).current_seat;
        auto o=memories[actor].observe(match.view(actor));
        SearchLimits expired; expired.decisionMs=expired.stopMs=0;expired.seed=17;
        auto partial=search.choose(o,memories[actor],model,expired);
        CHECK("no partial target",!partial.stats.completedBalancedPass && !partial.stats.target.has_value());
        CHECK("legal fallback",o.legal[partial.action]);
        if(std::count(o.legal.begin(),o.legal.end(),true)>1) {
            SearchLimits limits;limits.maxSimulations=61;limits.seed=17;
            // A budget smaller than the first pass must never create a target.
            limits.maxSimulations=1; auto shortRun=search.choose(o,memories[actor],model,limits);
            CHECK("incomplete fixed count target absent",!shortRun.stats.target);
            limits.maxSimulations=std::count(o.legal.begin(),o.legal.end(),true);
            auto result=search.choose(o,memories[actor],model,limits);
            CHECK("balanced pass completed",result.stats.completedBalancedPass);
            CHECK("target emitted",result.stats.target.has_value());
            CHECK("searched legal",o.legal[result.action]);
            float sum=0;for(int id=0;id<61;++id) { CHECK("masked target",o.legal[id] || (*result.stats.target)[id]==0);sum+=(*result.stats.target)[id]; }
            CHECK("normalized target",std::abs(sum-1)<1e-5);
            auto changed=o.position.object(); auto game=changed.at("game").object(); auto legal=game.at("legalActions").object();
            for(auto& [key,value]:legal) if(value.is_array()) {auto a=value.array();std::reverse(a.begin(),a.end());value=Json(a);}
            game["legalActions"]=Json(legal);changed["game"]=Json(game);auto reordered=memories[actor];auto equivalent=reordered.observe(Json(changed));
            auto repeat=search.choose(equivalent,reordered,model,limits);
            CHECK("candidate order independent",result.action==repeat.action && result.stats.means==repeat.stats.means && result.stats.counts==repeat.stats.counts);
        } else {
            auto before=observations;SearchLimits limits;limits.maxSimulations=10;
            (void)search.choose(o,memories[actor],model,limits);CHECK("forced bypass",before==observations);
        }
        auto action=static_cast<ActionId>(step.at("id").integer());memories[actor].record_own_action(action);match.apply(action);
    }
    CHECK("continuation spy invoked",observations>0);CHECK("boundary invoked",boundaries>0);
    // A known common world allows exact assertions about allocation and terminal scoring.
    NativeMatch terminal;terminal.start(deal_from_json(init),init.at("dealer").integer(),{39,39});
    int actor=(init.at("dealer").integer()+1)%4;SeatMemory terminalMemory;auto terminalObservation=terminalMemory.observe(terminal.view(actor));
    int samples=0,terminalBoundaries=0;
    SearchHooks exact;
    exact.sample=[&](const SeatObservation&,const std::optional<std::array<float,96>>&,Rng&,Deadline)->SampleResult {++samples;return SampledWorld{terminal,{}};};
    exact.policy=[](const SeatObservation& view){std::array<float,61> p{};for(int id=0;id<61;++id)if(view.legal[id]){p[id]=1;break;}return p;};
    exact.boundary=[&](const BoundaryFeatures&){++terminalBoundaries;return .5f;};
    SearchLimits fixed;fixed.maxSimulations=7;fixed.seed=123;
    auto scored=RootSearch(exact).choose(terminalObservation,terminalMemory,model,fixed);
    CHECK("one world shared by balanced candidates",samples==1);
    CHECK("terminal bypasses boundary",terminalBoundaries==0);
    for(int id=0;id<61;++id)if(terminalObservation.legal[id])CHECK("terminal exact zero or one",scored.stats.counts[id]==1 && (scored.stats.means[id]==0 || scored.stats.means[id]==1));
    samples=0;fixed.maxSimulations=9;auto paired=RootSearch(exact).choose(terminalObservation,terminalMemory,model,fixed);
    CHECK("later paired world",samples==2 && paired.stats.simulations==9);
    int twice=0;for(auto n:paired.stats.counts)if(n==2)++twice;CHECK("two paired alternatives",twice==2);
    // Interrupt after the first candidate completes: discard all batch counts.
    int calls=0;auto partialHooks=exact;
    partialHooks.policy=[&](const SeatObservation& view){if(++calls==30)std::this_thread::sleep_for(std::chrono::milliseconds(70));return exact.policy(view);};
    fixed.maxSimulations=7;fixed.decisionMs=70;fixed.stopMs=100;
    auto interrupted=RootSearch(partialHooks).choose(terminalObservation,terminalMemory,model,fixed);
    CHECK("actual interrupted first pass",calls>=30 && !interrupted.stats.target && interrupted.stats.simulations==0);
    CHECK("interrupted fallback precomputed",interrupted.action==RootSearch{}.choose(terminalObservation,terminalMemory,model,SearchLimits{0,0,{},0}).action);
    // Slow work must receive and respect the shared real deadline.
    auto first=SeatMemory{};NativeMatch start;start.start(deal_from_json(init),init.at("dealer").integer(),{0,0});
    auto o=first.observe(start.view((init.at("dealer").integer()+1)%4));
    SearchHooks slow;slow.sample=[](const SeatObservation&,const std::optional<std::array<float,96>>&,Rng&,Deadline deadline)->SampleResult {
        while(std::chrono::steady_clock::now()<deadline) std::this_thread::sleep_for(std::chrono::milliseconds(1));return SampleFailure::Deadline;
    };
    SearchLimits limits;limits.decisionMs=40;limits.stopMs=60;auto begin=std::chrono::steady_clock::now();
    auto stopped=RootSearch(slow).choose(o,first,model,limits);
    CHECK("slow sampler bounded",std::chrono::steady_clock::now()-begin<std::chrono::milliseconds(150));
    CHECK("slow sampler no target",!stopped.stats.target && o.legal[stopped.action]);
}
}
