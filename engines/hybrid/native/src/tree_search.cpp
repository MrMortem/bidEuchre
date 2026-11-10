#include "hybrid/tree_search.hpp"
#include "hybrid/integrity.hpp"
#include "hybrid/policy.hpp"
#include <algorithm>
#include <cmath>
#include <map>
#include <sstream>
namespace hybrid {
ObserverKey make_observer_key(const SeatObservation& o) {
 const auto p=beu::parse_position(o.position);std::ostringstream out;
 auto n=[&](auto v){out<<v<<',';};auto seat=[&](int s){n(s<0?-1:(s-p.seat+4)%4);};
 out<<"observer-v1:"<<p.phase<<':';seat(p.dealer);seat(p.current_seat);
 for(int t=0;t<2;++t){n(p.scores[(p.seat+t)%2]);n(p.tricks[(p.seat+t)%2]);}
 for(int r=0;r<4;++r){int s=(p.seat+r)%4;n(p.card_counts[s]);n(p.sitting[s]);}
 n(p.hand);n(p.high_bid);seat(p.bidder);n(p.contract.valid);n(p.contract.bid);n(int(p.contract.mode));n(p.contract.trump);
 n(p.auction.size());for(auto [s,b]:p.auction){seat(s);n(b);}
 auto plays=[&](const auto& ps){n(ps.size());for(auto x:ps){seat(x.seat);n(x.card);}};
 plays(p.current_trick);n(p.completed_tricks.size());
 for(std::size_t i=0;i<p.completed_tricks.size();++i){const auto& t=p.completed_tricks[i];seat(o.position.at("game").at("completedTricks").array()[i].at("leader").integer());seat(t.winner);plays(t.plays);}
 const auto* w=o.position.at("game").find("gameWinner");n(!w || w->is_null()?-1:(w->integer()+p.seat)%2);
 for(auto x:{o.recall.initial_hand,o.recall.sent_exchange,o.recall.received_exchange}){n(x.has_value());if(x)n(*x);}
 n(o.recall.own_actions.size());for(auto x:o.recall.own_actions)n(int(x));
 for(bool x:o.legal)n(x);
 for(int r=0;r<4;++r){const auto& s=o.summary.seats[(p.seat+r)%4];n(s.bid_observations);n(s.passes);n(s.solo_bids);n(s.numeric_bids);n(s.numeric_bid_sum);n(s.contract_observations);for(auto x:s.modes)n(x);}
 auto bytes=out.str();return sha256(std::as_bytes(std::span(bytes.data(),bytes.size())));
}
void InformationNode::observe(const ActionMask& mask){for(int a=0;a<61;++a)if(mask[a])++availability[a];}
void InformationNode::update(ActionId a,double value){++visits[a];total[a]+=value;}
double InformationNode::mean(ActionId a) const{return visits[a]?total[a]/visits[a]:0;}
ActionId InformationNode::select(const ActionMask& legal,const std::array<float,61>& prior) const {
 int best=-1;double score=-1e30;
 for(int a=0;a<61;++a)if(legal[a]){double v=mean(a)+.1*prior[a]*std::sqrt(double(availability[a]+1))/(visits[a]+1);if(v>score){score=v;best=a;}}
 if(best<0)throw std::invalid_argument("empty information node");return static_cast<ActionId>(best);
}
SearchResult InformationTreeSearch::query(const SeatObservation& o,const FrozenModel& model,SearchLimits limits,Deadline deadline) const {
 // No outer sampled world, root hand, or root recall is accepted here.
 limits.deadline=deadline;limits.searchMode="root";limits.leafMode="policy";
 limits.maxSimulations=std::count(o.legal.begin(),o.legal.end(),true);
 auto key=make_observer_key(o);limits.seed^=std::stoull(key.substr(0,16),nullptr,16);
 return RootSearch{}.choose(o,SeatMemory::from_observation(o),model,limits);
}
SearchResult InformationTreeSearch::choose(const SeatObservation& o,const SeatMemory& memory,const FrozenModel& model,SearchLimits limits) const {
 const auto deadline=std::chrono::steady_clock::now()+std::chrono::milliseconds(std::max(0,std::min(limits.decisionMs,limits.stopMs)-20));
 limits.deadline=limits.deadline?std::min(*limits.deadline,deadline):deadline;
 std::map<ObserverKey,InformationNode> nodes;int queries=0,fallbacks=0,visits=0,availability=0;
 SearchHooks hooks;hooks.policy=[&](const SeatObservation& actor){
  auto prior=FrozenPolicy(model).distribution(actor);auto& node=nodes[make_observer_key(actor)];node.observe(actor.legal);
  availability+=std::count(actor.legal.begin(),actor.legal.end(),true);
  if(queries>=limits.observerQueries || std::chrono::steady_clock::now()+std::chrono::milliseconds(2)>=*limits.deadline){++fallbacks;return prior;}
  ++queries;auto end=std::min(*limits.deadline,std::chrono::steady_clock::now()+std::chrono::milliseconds(limits.observerQueryMs));
  auto result=query(actor,model,limits,end);
  if(!result.stats.completedBalancedPass){++fallbacks;return prior;}
  for(int a=0;a<61;++a)if(result.stats.counts[a]){node.update(static_cast<ActionId>(a),result.stats.means[a]);++visits;}
  std::array<float,61> selected{};selected[node.select(actor.legal,prior)]=1;return selected;
 };
 auto result=RootSearch(hooks).choose(o,memory,model,limits);
 result.stats.observerQueries=queries;result.stats.observerFallbacks=fallbacks;result.stats.treeVisits=visits;result.stats.treeAvailability=availability;return result;
}
}
