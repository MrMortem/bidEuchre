#include "hybrid/sampler.hpp"
#include "hybrid/json_io.hpp"
#include <algorithm>
#include <bit>
#include <cmath>
#include <limits>
#include <unordered_map>

namespace hybrid {
namespace {
using beu::CardMask;
constexpr double impossible=-std::numeric_limits<double>::infinity();
void guard(Deadline deadline) { if(std::chrono::steady_clock::now()>=deadline) throw SampleFailure::Deadline; }
void require(bool valid) { if(!valid) throw SampleFailure::Inconsistent; }
struct Scenario {
    int sent=-1, returned=-1;
    std::array<std::uint8_t,24> allowed{};
};
struct Evidence {
    beu::Position p;
    std::array<CardMask,4> played{};
    std::vector<beu::CardPlay> plays;
    std::array<std::uint8_t,24> allowed{};
    std::vector<int> cards;
    std::vector<Scenario> scenarios;
    int exchanges=0;
};
CardMask bit(int card) { return card<0?0:1U<<card; }
bool known_matches(const std::optional<CardMask>& known,CardMask value) { return !known || *known==value; }
Evidence evidence(const SeatObservation& o,Deadline deadline) {
    // Re-sanitize/validate the policy-facing object; no privileged projection is consumed.
    auto canonical=SeatMemory{}.observe(o.position); Evidence e; e.p=beu::parse_position(canonical.position);
    const auto& p=e.p; require(p.phase!="NotStarted");
    for(const auto& known:{o.recall.initial_hand,o.recall.sent_exchange,o.recall.received_exchange}) require(!known || !(*known>>24));
    if(o.recall.initial_hand) require(std::popcount(*o.recall.initial_hand)==6);
    if(o.recall.sent_exchange) require(std::popcount(*o.recall.sent_exchange)<=1);
    if(o.recall.received_exchange) require(std::popcount(*o.recall.received_exchange)<=1);
    std::array<std::array<bool,4>,4> voids{}; CardMask allPlayed=0;
    auto trick=[&](const auto& ps) {
        for(std::size_t i=0;i<ps.size();++i) {
            auto play=ps[i]; auto c=bit(play.card); int suit=beu::effective_suit(play.card,p.contract);
            require(!(allPlayed&c) && !voids[play.seat][suit]);
            allPlayed|=c; e.played[play.seat]|=c; e.plays.push_back(play);
            if(i && suit!=beu::effective_suit(ps[0].card,p.contract)) voids[play.seat][beu::effective_suit(ps[0].card,p.contract)]=true;
        }
    };
    for(const auto& t:p.completed_tricks) trick(t.plays);
    trick(p.current_trick); require(!(allPlayed&p.hand));
    int total=std::popcount(allPlayed);
    for(int s=0;s<4;++s) total+=p.card_counts[s];
    require(total==24);
    for(int c=0;c<24;++c) {
        for(int s=0;s<4;++s) {
            if(allPlayed&bit(c)) { if(e.played[s]&bit(c)) e.allowed[c]|=1U<<s; }
            else if((s==p.seat)==bool(p.hand&bit(c)) && !voids[s][beu::effective_suit(c,p.contract)]) e.allowed[c]|=1U<<s;
        }
        require(e.allowed[c]!=0);
        if(!(allPlayed&bit(c))) e.cards.push_back(c);
    }
    bool pb=p.contract.valid && p.contract.bid==7;
    e.exchanges=pb?(p.phase=="ExchangingBidderCard"?0:p.phase=="ExchangingPartnerCard"?1:2):0;
    int bidder=p.bidder,partner=bidder<0?-1:(bidder+2)%4;
    CardMask ownTotal=p.hand|e.played[p.seat];
    // Every scenario represents a legal reverse exchange, not a likelihood.
    // The DP counts the union, so worlds with more latent histories get no extra mass.
    for(int x=e.exchanges?0:-1;x<(e.exchanges?24:0);++x) {
        for(int y=e.exchanges==2?0:-1;y<(e.exchanges==2?24:0);++y) {
            guard(deadline); Scenario s{x,y,e.allowed};
            auto force=[&](int c,int seat) { if(c>=0) s.allowed[c]&=1U<<seat; };
            if(e.exchanges==1) force(x,partner);
            if(e.exchanges==2) { force(y,bidder); force(x,x==y?bidder:partner); }
            if(std::any_of(s.allowed.begin(),s.allowed.end(),[](auto a){return !a;})) continue;
            CardMask sent=p.seat==bidder?bit(x):p.seat==partner?bit(y):0;
            CardMask received=p.seat==bidder?bit(y):p.seat==partner?bit(x):0;
            if(!known_matches(o.recall.sent_exchange,sent) || !known_matches(o.recall.received_exchange,received)) continue;
            CardMask initial=ownTotal;
            if(x!=y) { initial&=~received; initial|=sent; }
            if(!known_matches(o.recall.initial_hand,initial)) continue;
            std::vector<ActionId> actions;
            for(auto [seat,bid]:p.auction) if(seat==p.seat) actions.push_back(static_cast<ActionId>(bid?bid-2:0));
            if(p.contract.valid && p.seat==bidder) actions.push_back(static_cast<ActionId>(p.contract.mode==beu::Mode::High?7:p.contract.mode==beu::Mode::Low?8:9+p.contract.trump));
            if(sent) actions.push_back(static_cast<ActionId>(13+std::countr_zero(sent)));
            for(auto play:e.plays) if(play.seat==p.seat) actions.push_back(static_cast<ActionId>(37+play.card));
            auto action=actions.begin(); bool matches=true;
            for(auto remembered:o.recall.own_actions) {
                action=std::find(action,actions.end(),remembered);
                if(action==actions.end()) { matches=false; break; } ++action;
            }
            if(matches) e.scenarios.push_back(s);
        }
    }
    require(!e.scenarios.empty());
    // Forced cards first reduces the union state before unconstrained allocation.
    std::stable_sort(e.cards.begin(),e.cards.end(),[&](int a,int b){return std::popcount(e.allowed[a])<std::popcount(e.allowed[b]);});
    return e;
}
// Active reverse-exchange scenarios constitute a union of hard assignments.
// A state remembers which scenarios remain possible, so overlaps count once.
using Active=std::vector<std::uint16_t>;
class Counter {
    const Evidence& e_;
    Deadline deadline_;
    std::uint64_t& nodes_;
    std::array<std::array<double,4>,24> weights_{};
    std::unordered_map<std::string,double> memo_;
    Active normalize(std::size_t i,Active active) const {
        if(active.size()==1 && active[0]==universal) return active;
        for(auto id:active) {
            bool unrestricted=true;
            for(std::size_t j=i;j<e_.cards.size();++j) if(e_.scenarios[id].allowed[e_.cards[j]]!=e_.allowed[e_.cards[j]]) { unrestricted=false; break; }
            if(unrestricted) return {universal};
        }
        return active;
    }
    Active next(const Active& active,int c,int seat) const {
        if(active.size()==1 && active[0]==universal) return active;
        Active result;
        for(auto id:active) if(e_.scenarios[id].allowed[c]&(1U<<seat)) result.push_back(id);
        return result;
    }
    static constexpr std::uint16_t universal=65535;
    static double add(double a,double b) {
        if(a==impossible) return b; if(b==impossible) return a;
        auto m=std::max(a,b); return m+std::log(std::exp(a-m)+std::exp(b-m));
    }
    double count(std::size_t i,std::array<int,4> capacity,Active active) {
        ++nodes_; guard(deadline_); if(active.empty()) return impossible;
        if(i==e_.cards.size()) return std::all_of(capacity.begin(),capacity.end(),[](int n){return n==0;})?0:impossible;
        active=normalize(i,std::move(active));
        std::string key; key.push_back(static_cast<char>(i)); for(int n:capacity) key.push_back(static_cast<char>(n));
        key.append(reinterpret_cast<const char*>(active.data()),active.size()*sizeof(active[0]));
        if(auto it=memo_.find(key);it!=memo_.end()) return it->second;
        // Explicit bound in addition to deadline, preventing adversarial union-state memory growth.
        if(memo_.size()>=250000) throw SampleFailure::Exhausted;
        double result=impossible; int c=e_.cards[i];
        for(int s=0;s<4;++s) if(capacity[s]>0 && (e_.allowed[c]&(1U<<s))) {
            --capacity[s]; auto child=count(i+1,capacity,next(active,c,s)); ++capacity[s];
            if(child!=impossible) result=add(result,weights_[c][s]+child);
        }
        memo_.emplace(std::move(key),result); return result;
    }
public:
    Counter(const Evidence& e,Deadline deadline,const std::optional<std::array<float,96>>& logits,std::uint64_t& nodes):e_(e),deadline_(deadline),nodes_(nodes) {
        if(logits) for(int c:e.cards) {
            double maximum=impossible;
            for(int s=0;s<4;++s) if(e.allowed[c]&(1U<<s)) {
                auto value=(*logits)[4*c+(s-e.p.seat+4)%4];
                if(!std::isfinite(value)) throw SampleFailure::Exhausted;
                maximum=std::max(maximum,static_cast<double>(value));
            }
            for(int s=0;s<4;++s) if(e.allowed[c]&(1U<<s)) weights_[c][s]=static_cast<double>((*logits)[4*c+(s-e.p.seat+4)%4])-maximum;
        }
    }
    std::array<CardMask,4> sample(Rng& rng) {
        auto capacity=e_.p.card_counts; Active active;
        for(std::size_t s=0;s<e_.scenarios.size();++s) active.push_back(static_cast<std::uint16_t>(s));
        require(count(0,capacity,active)!=impossible);
        std::array<CardMask,4> result{};
        for(std::size_t i=0;i<e_.cards.size();++i) {
            guard(deadline_); int c=e_.cards[i]; active=normalize(i,std::move(active));
            std::array<double,4> masses; masses.fill(impossible); double maximum=impossible;
            for(int s=0;s<4;++s) if(capacity[s]>0 && (e_.allowed[c]&(1U<<s))) {
                --capacity[s]; double child=count(i+1,capacity,next(active,c,s)); ++capacity[s];
                if(child!=impossible) maximum=std::max(maximum,masses[s]=weights_[c][s]+child);
            }
            double total=0; for(auto& mass:masses) { mass=mass==impossible?0:std::exp(mass-maximum); total+=mass; }
            double draw=std::generate_canonical<double,53>(rng)*total; int selected=-1;
            for(int s=0;s<4;++s) if(masses[s]>0) { selected=s; if(draw<masses[s]) break; draw-=masses[s]; }
            require(selected>=0); result[selected]|=bit(c); --capacity[selected]; active=next(active,c,selected);
        }
        return result;
    }
};
PublicSummary prior_summary(const SeatObservation& o) {
    auto current=SeatMemory{}.observe(o.position).summary; auto prior=o.summary;
    auto subtract=[](std::uint64_t& a,std::uint64_t b){require(a>=b);a-=b;};
    for(int s=0;s<4;++s) {
        auto& a=prior.seats[s]; const auto& b=current.seats[s];
        subtract(a.bid_observations,b.bid_observations); subtract(a.passes,b.passes); subtract(a.solo_bids,b.solo_bids);
        subtract(a.numeric_bids,b.numeric_bids); subtract(a.numeric_bid_sum,b.numeric_bid_sum); subtract(a.contract_observations,b.contract_observations);
        for(int mode=0;mode<3;++mode) subtract(a.modes[mode],b.modes[mode]);
    }
    return prior;
}
SampledWorld replay(const SeatObservation& o,const Evidence& e,const std::array<CardMask,4>& remaining,Rng& rng,Deadline deadline) {
    const auto& p=e.p; std::array<CardMask,4> total=remaining;
    for(int s=0;s<4;++s) total[s]|=e.played[s];
    std::vector<const Scenario*> compatible;
    for(const auto& scenario:e.scenarios) {
        guard(deadline); bool valid=true;
        for(int s=0;s<4;++s) for(int c:beu::cards_in(total[s])) if(!(scenario.allowed[c]&(1U<<s))) valid=false;
        if(valid) compatible.push_back(&scenario);
    }
    require(!compatible.empty()); auto scenario=*compatible[std::uniform_int_distribution<std::size_t>(0,compatible.size()-1)(rng)];
    Deal initial{total}; int partner=(p.bidder+2)%4;
    if(e.exchanges && scenario.sent!=scenario.returned) {
        initial.hands[p.bidder]&=~bit(scenario.returned); initial.hands[p.bidder]|=bit(scenario.sent);
        initial.hands[partner]&=~bit(scenario.sent); initial.hands[partner]|=bit(scenario.returned);
    }
    auto scores=p.scores;
    bool terminal=p.phase=="HandComplete" || p.phase=="GameComplete";
    int penalized=-1;
    if(terminal) {
        std::vector<std::pair<int,std::array<int,2>>> candidates;
        // A penalty terminal reveals scores/plays but not the penalized seat.
        // Enumerate compatible public score histories; do not invent root recall.
        for(int kind=0;kind<(p.completed_tricks.size()==6?1:2);++kind) {
            std::array<int,2> delta{}; int offender=-1;
            if(p.completed_tricks.size()==6) delta=score_hand(p.contract,p.bidder,p.tricks,{0,0}).deltas;
            else {
                int team=p.bidder%2; offender=kind==0?p.bidder:(p.bidder+1)%4;
                if(kind==0) { delta[team]=p.contract.bid==7?-12:p.contract.bid==8?-24:-p.contract.required_tricks(); delta[1-team]=6; }
                else delta[team]=p.contract.bid==7?12:p.contract.bid==8?24:6;
            }
            std::array<int,2> before{}; bool valid=true;
            for(int t=0;t<2;++t) {
                auto value=static_cast<long long>(scores[t])-delta[t];
                if(value<std::numeric_limits<int>::min() || value>std::numeric_limits<int>::max()) valid=false;
                else before[t]=static_cast<int>(value);
            }
            if(valid && !((before[0]>=40 || before[1]>=40) && before[0]!=before[1])) candidates.emplace_back(offender,before);
        }
        require(!candidates.empty()); auto chosen=candidates[std::uniform_int_distribution<std::size_t>(0,candidates.size()-1)(rng)];
        penalized=chosen.first; scores=chosen.second;
    }
    SampledWorld w; auto prior=prior_summary(o);
    for(auto& memory:w.memories) memory=SeatMemory::with_public_summary(prior);
    const auto* serial=o.position.at("game").find("dealSerial");
    w.match.start(initial,p.dealer,scores,p.hand_number,serial && !serial->is_null()?static_cast<std::uint64_t>(serial->number()):1);
    std::vector<ActionId> rootActions;
    auto apply=[&](int seat,ActionId action) {
        guard(deadline); auto view=w.match.view(seat); require(beu::parse_position(view).current_seat==seat);
        w.memories[seat].observe(view); w.memories[seat].record_own_action(action);
        if(seat==p.seat) rootActions.push_back(action);
        w.match.apply(action);
    };
    for(auto [seat,bid]:p.auction) apply(seat,static_cast<ActionId>(bid?bid-2:0));
    if(p.contract.valid) apply(p.bidder,static_cast<ActionId>(p.contract.mode==beu::Mode::High?7:p.contract.mode==beu::Mode::Low?8:9+p.contract.trump));
    if(e.exchanges>=1) apply(p.bidder,static_cast<ActionId>(13+scenario.sent));
    if(e.exchanges==2) apply(partner,static_cast<ActionId>(13+scenario.returned));
    for(auto play:e.plays) apply(play.seat,static_cast<ActionId>(37+play.card));
    if(penalized>=0) { guard(deadline); w.match.apply_illegal_play_penalty(penalized); }
    if(terminal) for(int s=0;s<4;++s) w.memories[s].observe(w.match.view(s));
    // Own actions can start late or omit unobserved decisions, but cannot invent one.
    auto action=rootActions.begin();
    for(auto remembered:o.recall.own_actions) { action=std::find(action,rootActions.end(),remembered); require(action!=rootActions.end()); ++action; }
    auto actual=SeatMemory{}.observe(w.match.view(p.seat)).position;
    // Legacy positions lack a serial; equality still covers every supplied field.
    if(!serial || serial->is_null()) { auto r=actual.object(); auto g=r.at("game").object(); if(serial) g["dealSerial"]=Json(); else g.erase("dealSerial"); r["game"]=Json(g); actual=Json(r); }
    require(json_stringify(actual)==json_stringify(o.position));
    w.memories[p.seat]=SeatMemory::from_observation(o);
    guard(deadline); return w;
}
} // namespace
SampleResult BeliefSampler::sample(const SeatObservation& o,const std::optional<std::array<float,96>>& logits,Rng& rng,Deadline deadline) {
    ++diagnostics_.attempts;
    try {
        guard(deadline); auto e=evidence(o,deadline);
        bool learned=logits && std::generate_canonical<double,53>(rng)<0.9;
        std::array<CardMask,4> remaining;
        try { remaining=Counter(e,deadline,learned?logits:std::nullopt,diagnostics_.counting_nodes).sample(rng); }
        catch(SampleFailure failure) {
            if(!learned || failure==SampleFailure::Deadline || failure==SampleFailure::Inconsistent) throw;
            learned=false; remaining=Counter(e,deadline,{},diagnostics_.counting_nodes).sample(rng);
        }
        auto result=replay(o,e,remaining,rng,deadline);
        ++diagnostics_.feasible; if(learned) ++diagnostics_.learned_samples; else ++diagnostics_.uniform_samples;
        if(unique_.size()<4096) unique_.insert(remaining); diagnostics_.unique=unique_.size();
        return result;
    } catch(SampleFailure failure) { if(failure==SampleFailure::Inconsistent) ++diagnostics_.constraint_failures; return failure; }
    catch(const std::bad_alloc&) { return SampleFailure::Exhausted; }
    catch(const std::exception&) { ++diagnostics_.constraint_failures; return SampleFailure::Inconsistent; }
}
} // namespace hybrid
