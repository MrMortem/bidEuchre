#include "hybrid/dd_leaf.hpp"
#include "search.hpp"
#include <cmath>
namespace hybrid {
std::optional<float> DoubleDummyLeaf::evaluate(const SampledWorld& world,int seat,Deadline end) const {
 auto p=beu::parse_position(world.match.view(seat));
 if(p.phase!="Playing" || p.completed_tricks.size()<4 || std::chrono::steady_clock::now()>=end)return {};
 std::array<beu::CardMask,4> hands{};for(int s=0;s<4;++s)hands[s]=beu::parse_position(world.match.view(s)).hand;
 auto state=beu::make_play_state(p,hands);beu::SearchLimits limits;limits.deadline=end;limits.maximum_depth=8;limits.maximum_nodes=maximumNodes_;
 beu::DoubleDummySolver solver(limits);int future=solver.future_tricks(state,seat%2);
 if(solver.interrupted() || std::chrono::steady_clock::now()>=end)return {};
 auto tricks=p.tricks;tricks[seat%2]+=future;tricks[1-seat%2]+=6-int(p.completed_tricks.size())-future;
 auto scored=score_hand(p.contract,p.bidder,tricks,p.scores);
 if(scored.winner)return *scored.winner==seat%2?1.f:0.f;
 auto memory=world.memories[seat];auto observation=memory.observe(world.match.view(seat));
 auto features=encode_boundary(scored.scoresAfter,(p.dealer+1)%4,seat,observation.summary);
 float value;
 if(boundary_)value=boundary_(features);else {float logit=model_.boundary_logit(features);value=logit>=0?1.f/(1.f+std::exp(-logit)):std::exp(logit)/(1.f+std::exp(logit));}
 if(!std::isfinite(value) || value<0 || value>1)throw std::domain_error("invalid DD boundary value");
 if(std::chrono::steady_clock::now()>=end)return {};return value;
}
}
