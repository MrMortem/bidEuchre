#include "hybrid/search.hpp"
#include "hybrid/policy.hpp"
#include <algorithm>
#include <cmath>
#include <stdexcept>
#include <vector>
namespace hybrid {
namespace {
using Clock=std::chrono::steady_clock;
ActionId draw(const std::array<float,61>& p,const ActionMask& legal,Rng& rng) {
    double total=0;int last=-1;
    for(int i=0;i<61;++i) {if(!std::isfinite(p[i]) || p[i]<0 || (!legal[i] && p[i]!=0)) throw std::domain_error("invalid policy distribution");if(legal[i]){total+=p[i];last=i;}}
    if(last<0 || total<=0)throw std::domain_error("empty policy distribution");
    auto x=std::generate_canonical<double,53>(rng)*total;
    for(int i=0;i<61;++i) if(legal[i]) {x-=p[i];if(x<0)return static_cast<ActionId>(i);}
    return static_cast<ActionId>(last);
}
float sigmoid(float x) {if(!std::isfinite(x))throw std::domain_error("invalid boundary inference");return x>=0?1.f/(1.f+std::exp(-x)):std::exp(x)/(1.f+std::exp(x));}
}
SearchResult RootSearch::choose(const SeatObservation& input,const SeatMemory& memory,const FrozenModel& model,SearchLimits limits) const {
    const auto begin=Clock::now();
    auto o=input;
    // Legal lists are sets in BEUCI; canonicalize their order for world replay.
    auto position=o.position.object();auto game=position.at("game").object();auto actions=game.at("legalActions").object();
    for(auto& [key,value]:actions) if(value.is_array()) {
        auto items=value.array();
        auto rank=[&](const Json& v) {
            if(key=="bids")return beu::parse_bid(v.string());
            if(key=="trumpSuits")return beu::parse_suit(v.string());
            if(key=="contractModes")return static_cast<int>(beu::parse_mode(v.string()));
            return beu::parse_card(v.at("code").string());
        };
        std::sort(items.begin(),items.end(),[&](const Json& a,const Json& b){return rank(a)<rank(b);});value=Json(items);
    }
    game["legalActions"]=Json(actions);position["game"]=Json(game);o.position=Json(position);

    const auto deadline=begin+std::chrono::milliseconds(std::max(0,std::min(limits.decisionMs,limits.stopMs)-20));
    std::vector<int> legal;for(int i=0;i<61;++i)if(o.legal[i])legal.push_back(i);
    if(legal.empty())throw std::invalid_argument("no legal root action");
    SearchResult result{static_cast<ActionId>(legal.front()),{}};result.stats.seed=limits.seed;
    if(legal.size()==1)return result;
    const auto raw=model.evaluate(o.features);const auto policy=normalize_output(raw,o.legal).policy;
    for(int id:legal)if(policy[id]>policy[result.action])result.action=static_cast<ActionId>(id);
    if(Clock::now()>=deadline)return result;
    const int root=beu::parse_position(o.position).seat;
    FrozenPolicy continuation(model);BeliefSampler sampler;Rng rng(limits.seed);
    auto evaluate=[&](SampledWorld world,int action,std::uint64_t seed)->std::optional<float> {
        world.memories[root]=memory;
        (void)world.memories[root].observe(o.position);
        world.memories[root].record_own_action(static_cast<ActionId>(action));world.match.apply(static_cast<ActionId>(action));
        std::array<Rng,4> random{Rng(seed),Rng(seed+1),Rng(seed+2),Rng(seed+3)};
        for(int step=0;step<40;++step) {
            if(Clock::now()>=deadline)return {};
            auto view=world.match.view(root);auto p=beu::parse_position(view);
            if(p.phase=="HandComplete" || p.phase=="GameComplete") {
                const auto scored=world.match.hand_result(); // NativeMatch applied exact scoring already.
                if(scored.winner)return *scored.winner==root%2?1.f:0.f;
                auto final=world.memories[root].observe(view);
                // This is intentionally before any next deal, and the value API
                // receives only scores, next dealer and public summary features.
                auto features=encode_boundary(scored.scoresAfter,(p.dealer+1)%4,root,final.summary);
                float value=hooks_.boundary?hooks_.boundary(features):sigmoid(model.boundary_logit(features));
                if(!std::isfinite(value) || value<0 || value>1)throw std::domain_error("invalid boundary value");
                if(Clock::now()>=deadline)return {};
                return value;
            }
            const int actor=p.current_seat;
            auto actorObservation=world.memories[actor].observe(world.match.view(actor));
            auto probabilities=hooks_.policy?hooks_.policy(actorObservation):continuation.distribution(actorObservation);
            if(Clock::now()>=deadline)return {};
            auto choice=draw(probabilities,actorObservation.legal,random[actor]);
            world.memories[actor].record_own_action(choice);world.match.apply(choice);
        }
        throw std::logic_error("hand continuation exceeded action bound");
    };
    std::vector<int> batch=legal;
    while(Clock::now()<deadline) {
        if(limits.maxSimulations && result.stats.simulations+static_cast<int>(batch.size())>*limits.maxSimulations)break;
        auto sampled=hooks_.sample?hooks_.sample(o,raw.ownership_logits,rng,deadline):sampler.sample(o,raw.ownership_logits,rng,deadline);
        if(!std::holds_alternative<SampledWorld>(sampled) || Clock::now()>=deadline)break;
        const auto seed=rng();std::vector<float> values;
        for(int action:batch) {auto v=evaluate(std::get<SampledWorld>(sampled),action,seed);if(!v)break;values.push_back(*v);}
        if(values.size()!=batch.size())break; // Never publish a partially compared world.
        for(std::size_t i=0;i<batch.size();++i) {int id=batch[i];auto& count=result.stats.counts[id];++count;result.stats.means[id]+=(values[i]-result.stats.means[id])/count;}
        result.stats.simulations+=static_cast<int>(batch.size());result.stats.completedBalancedPass=true;
        int best=legal.front();for(int id:legal)if(result.stats.means[id]>result.stats.means[best])best=id;
        result.action=static_cast<ActionId>(best);
        // Pair the leader with a close alternative, prioritizing fewer samples.
        int other=-1;double priority=-1;
        for(int id:legal)if(id!=best) {
            double gap=result.stats.means[best]-result.stats.means[id];
            double uncertainty=std::sqrt(1.0/result.stats.counts[id]+1.0/result.stats.counts[best]);
            if(gap<=.10+uncertainty) {double score=uncertainty-gap;if(score>priority){priority=score;other=id;}}
        }
        if(other<0)break;
        batch={best,other};std::sort(batch.begin(),batch.end());
    }
    if(result.stats.completedBalancedPass) {
        std::array<float,61> target{};double sum=0,maximum=result.stats.means[result.action];
        for(int id:legal) {target[id]=static_cast<float>(std::exp((result.stats.means[id]-maximum)/result.stats.temperature));sum+=target[id];}
        for(int id:legal)target[id]=static_cast<float>(target[id]/sum);result.stats.target=target;
    }
    return result;
}
}
