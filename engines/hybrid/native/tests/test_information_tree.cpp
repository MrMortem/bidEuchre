#include "test_support.hpp"
#include "hybrid/tree_search.hpp"
#include "hybrid/policy.hpp"
namespace hybrid::test {
void test_information_tree() {
 auto fixture=load_case("high");
 auto init=fixture.at("initial"); NativeMatch match;
 match.start(deal_from_json(init),init.at("dealer").integer(),{0,0});
 int actor=beu::parse_position(match.view(0)).current_seat;
 SeatMemory memory;auto o=memory.observe(match.view(actor));
 auto equivalent=o;auto g=equivalent.position.at("game").object();g["dealSerial"]=Json(999.);g["handNumber"]=Json(777.);
 auto j=equivalent.position.object();j["game"]=Json(g);equivalent.position=Json(j);
 CHECK("observer key invariant",make_observer_key(o)==make_observer_key(equivalent));
 equivalent.summary.seats[actor].bid_observations+=1000;
 CHECK("key uses uncapped counters",make_observer_key(o)!=make_observer_key(equivalent));
 equivalent=o;equivalent.recall.initial_hand.reset();
 CHECK("key preserves recall presence",make_observer_key(o)!=make_observer_key(equivalent));
 InformationNode node;std::array<float,61> prior{};ActionMask available{};available[1]=true;prior[1]=1;
 node.observe(available);node.update(0,1);CHECK("unavailable edge excluded",node.select(available,prior)==1);
 // Two indistinguishable worlds: adapting future choice to their hidden bit
 // falsely values the risky plan at 1; one shared information set values it .5.
 InformationNode trap;ActionMask choices{};choices[0]=choices[1]=true;prior[0]=prior[1]=.5;
 for(int hidden=0;hidden<2;++hidden){trap.observe(choices);trap.update(0,hidden==0?1:0);trap.update(1,hidden==1?1:0);}
 CHECK("fusion trap shared plan",trap.mean(0)==.5 && trap.mean(1)==.5);
 InformationNode root;root.observe(choices);root.update(0,std::max(trap.mean(0),trap.mean(1)));root.update(1,.6);
 CHECK("shared continuation rejects fusion trap",root.select(choices,prior)==1);
 double omniscient=0;for(int hidden=0;hidden<2;++hidden)omniscient+=.5*std::max(hidden==0?1.:0.,hidden==1?1.:0.);
 CHECK("omniscient continuation falsely prefers risky plan",omniscient>.6);

 // Root seat 1 has a different private hand, invisible to observer/actor 0.
 auto swapped=deal_from_json(init);std::swap(swapped.hands[(actor+1)%4],swapped.hands[(actor+3)%4]);
 NativeMatch other;other.start(swapped,init.at("dealer").integer(),{0,0});
 auto otherObservation=SeatMemory{}.observe(other.view(actor));
 CHECK("controlled root private difference",beu::parse_position(match.view((actor+1)%4)).hand!=beu::parse_position(other.view((actor+1)%4)).hand);
 CHECK("opponent observation invariant",make_observer_key(o)==make_observer_key(otherObservation));
 auto rotated=deal_from_json(init);auto original=rotated.hands;
 for(int i=0;i<4;++i)rotated.hands[(i+1)%4]=original[i];
 NativeMatch rotation;rotation.start(rotated,(init.at("dealer").integer()+1)%4,{0,0});
 CHECK("role-relative key",make_observer_key(o)==make_observer_key(SeatMemory{}.observe(rotation.view((actor+1)%4))));
 auto model=FrozenModel::load(HYBRID_TEST_MODEL);SearchLimits limits;limits.maxSimulations=7;limits.seed=919;
 limits.observerQueryMs=100;limits.decisionMs=3000;limits.stopMs=3500;
 InformationTreeSearch tree;auto a=tree.query(o,model,limits,std::chrono::steady_clock::now()+std::chrono::seconds(2));
 auto b=tree.query(otherObservation,model,limits,std::chrono::steady_clock::now()+std::chrono::seconds(2));
 CHECK("fixed observer query invariant",a.action==b.action && a.stats.means==b.stats.means && a.stats.counts==b.stats.counts);
 CHECK("observer query actually searched",a.stats.completedBalancedPass);
 auto result=tree.choose(o,memory,model,limits);
 CHECK("bounded active tree",result.stats.observerQueries>0 && result.stats.observerQueries<=limits.observerQueries && result.stats.treeVisits>0);
 CHECK("tree original legal",o.legal[result.action]);

}
}
