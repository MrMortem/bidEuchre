#include "test_support.hpp"
#include "hybrid/dd_leaf.hpp"
namespace hybrid::test {
void test_dd_leaf() {
 auto fixture=load_case("partners-best-same-card");auto init=fixture.at("initial");NativeMatch match;
 match.start(deal_from_json(init),init.at("dealer").integer(),{39,39});
 auto model=FrozenModel::load(HYBRID_TEST_MODEL);DoubleDummyLeaf leaf(model);
 CHECK("early DD disabled",!leaf.evaluate(SampledWorld{match,{}},0,std::chrono::steady_clock::now()+std::chrono::seconds(1)));
 NativeMatch low;low.start(deal_from_json(init),init.at("dealer").integer(),{0,0});
 bool boundaryChecked=false;bool checked=false;
 for(const auto& step:fixture.at("steps").array()){
  auto p=beu::parse_position(match.view(0));
  if(p.phase=="Playing" && p.completed_tricks.size()>=4){
   CHECK("interrupted DD is unknown",!leaf.evaluate(SampledWorld{match,{}},0,std::chrono::steady_clock::now()));
   CHECK("solver interruption is unknown",!DoubleDummyLeaf(model,{},1).evaluate(SampledWorld{match,{}},0,std::chrono::steady_clock::now()+std::chrono::seconds(1)));
   auto a=leaf.evaluate(SampledWorld{match,{}},0,std::chrono::steady_clock::now()+std::chrono::seconds(1));
   auto b=leaf.evaluate(SampledWorld{match,{}},1,std::chrono::steady_clock::now()+std::chrono::seconds(1));
   CHECK("DD terminal exact",a && b && (*a==0 || *a==1) && *a+*b==1);checked=true;
  }
  auto action=static_cast<ActionId>(step.at("id").integer());
  auto before=beu::parse_position(low.view(0));auto finished=low;finished.apply(action);
  if(before.phase=="Playing" && beu::parse_position(finished.view(0)).phase=="HandComplete"){
   auto expected=finished.hand_result();int calls=0;
   DoubleDummyLeaf boundary(model,[&](const BoundaryFeatures& f){++calls;
    CHECK("DD exact scores into boundary",f[0]==expected.scoresAfter[0]/40.f && f[1]==expected.scoresAfter[1]/40.f);
    CHECK("DD next dealer into boundary",f[2+(before.dealer+1)%4]==1);return .3f;});
   auto value=boundary.evaluate(SampledWorld{low,{}},0,std::chrono::steady_clock::now()+std::chrono::seconds(1));
   CHECK("DD nonterminal boundary conversion",value && *value==.3f && calls==1);boundaryChecked=true;
  }
  low.apply(action);match.apply(action);
 }
 CHECK("DD fixture exercised",checked && boundaryChecked);
}
}
