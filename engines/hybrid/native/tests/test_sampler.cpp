#include "test_support.hpp"
#include "hybrid/sampler.hpp"
#include "hybrid/json_io.hpp"
#include <algorithm>
#include <bit>
#include <cmath>
#include <iostream>
#include <functional>
#include <map>

namespace hybrid::test {
namespace {
Deadline soon() { return std::chrono::steady_clock::now()+std::chrono::seconds(5); }
NativeMatch initial(const Json& f) {
    const auto& i=f.at("initial"); NativeMatch m;
    m.start(deal_from_json(i),i.at("dealer").integer(),{i.at("scores").array()[0].integer(),i.at("scores").array()[1].integer()},i.at("handNumber").integer(),static_cast<std::uint64_t>(i.at("dealSerial").number())); return m;
}
SampledWorld world(SampleResult r) {
    CHECK("feasible sample", std::holds_alternative<SampledWorld>(r)); return std::get<SampledWorld>(std::move(r));
}
void validate(const SampledWorld& w,const SeatObservation& o) {
    auto p=beu::parse_position(o.position); auto actual=SeatMemory{}.observe(w.match.view(p.seat));
    CHECK("replayed root public/private position",json_stringify(actual.position)==json_stringify(o.position));
    beu::CardMask seen=0;
    for(const auto& t:p.completed_tricks) for(auto a:t.plays) { CHECK("public cards unique",!(seen&(1U<<a.card))); seen|=1U<<a.card; }
    for(auto a:p.current_trick) { CHECK("current cards unique",!(seen&(1U<<a.card))); seen|=1U<<a.card; }
    for(int s=0;s<4;++s) {
        auto hand=beu::parse_position(w.match.view(s)).hand;
        CHECK("exact capacities",std::popcount(hand)==p.card_counts[s]); CHECK("disjoint cards",!(seen&hand)); seen|=hand;
        for(int c:beu::cards_in(hand)) CHECK("effective suit void retained",!o.features[schema::main_blocks_public_voids_offset+((s-p.seat+4)%4)*4+beu::effective_suit(c,p.contract)]);
    }
    CHECK("24-card conservation",seen==0xffffffU);
    auto copy=w.memories[p.seat]; auto restored=copy.observe(o.position);
    CHECK("root exact unknown/known recall",restored.recall==o.recall);
    CHECK("root exact public summary",restored.summary==o.summary);
    CHECK("root exact features",restored.features==o.features);
}
std::array<beu::CardMask,4> hands(const NativeMatch& m) {
    std::array<beu::CardMask,4> h{}; for(int seat=0;seat<4;++seat) h[seat]=beu::parse_position(m.view(seat)).hand; return h;
}
// Independent tiny brute force: enumerate capacities, then ask production rules
// to replay every candidate. No sampler DP or void extraction is reused here.
std::vector<std::array<beu::CardMask,4>> enumerate(const SeatObservation& o) {
    auto p=beu::parse_position(o.position); beu::CardMask played=0;
    std::array<beu::CardMask,4> original{};
    for(const auto& t:p.completed_tricks) for(auto play:t.plays) { played|=1U<<play.card; original[play.seat]|=1U<<play.card; }
    for(auto play:p.current_trick) { played|=1U<<play.card; original[play.seat]|=1U<<play.card; }
    auto unknown=beu::cards_in(0xffffffU&~(played|p.hand));
    CHECK("tiny exhaustive fixture",unknown.size()<=6);
    std::array<beu::CardMask,4> candidate{}; candidate[p.seat]=p.hand;
    std::vector<std::array<beu::CardMask,4>> result;
    std::function<void(std::size_t)> visit=[&](std::size_t index) {
        if(index<unknown.size()) {
            for(int seat=0;seat<4;++seat) if(seat!=p.seat && std::popcount(candidate[seat])<p.card_counts[seat]) {
                candidate[seat]|=1U<<unknown[index]; visit(index+1); candidate[seat]&=~(1U<<unknown[index]);
            }
            return;
        }
        try {
            Deal d; for(int seat=0;seat<4;++seat) d.hands[seat]=original[seat]|candidate[seat];
            NativeMatch m; m.start(d,p.dealer,p.scores,p.hand_number);
            for(auto [seat,bid]:p.auction) { (void)seat; m.apply(static_cast<ActionId>(bid?bid-2:0)); }
            m.apply(static_cast<ActionId>(p.contract.mode==beu::Mode::High?7:p.contract.mode==beu::Mode::Low?8:9+p.contract.trump));
            for(const auto& t:p.completed_tricks) for(auto play:t.plays) m.apply(static_cast<ActionId>(37+play.card));
            for(auto play:p.current_trick) m.apply(static_cast<ActionId>(37+play.card));
            result.push_back(candidate);
        } catch(const std::exception&) {}
    };
    visit(0); return result;
}
void frequencies() {
    SeatObservation o; std::vector<std::array<beu::CardMask,4>> feasible;
    // Deterministic miniature: stop the authoritative High fixture with eight
    // cards remaining, leaving six unobserved cards for exhaustive enumeration.
    auto f=load_case("high"); auto m=initial(f); std::array<SeatMemory,4> mem;
    for(const auto& step:f.at("steps").array()) {
        int seat=m.full_state().at("currentSeat").integer(); auto current=mem[seat].observe(m.view(seat));
        if(beu::parse_position(current.position).completed_tricks.size()==4) { o=current; feasible=enumerate(o); break; }
        auto action=static_cast<ActionId>(step.at("id").integer()); mem[seat].record_own_action(action); m.apply(action);
    }
    CHECK("multiple exact worlds",feasible.size()>1);
    constexpr int n=2400;
    // Predeclared absolute frequency tolerance 0.05, >4.8 binomial SE even at p=.5.
    constexpr double tolerance=.05;
    std::map<std::array<beu::CardMask,4>,int> uniform,weighted;
    auto p=beu::parse_position(o.position); std::array<float,96> logits{};
    for(int c=0;c<24;++c) for(int seat=0;seat<4;++seat) logits[4*c+seat]=static_cast<float>(((c+2*seat)%5)-2)*.4f;
    std::vector<double> mass; double total=0;
    for(auto h:feasible) {
        double sum=0; for(int seat=0;seat<4;++seat) for(int c:beu::cards_in(h[seat])) sum+=logits[4*c+(seat-p.seat+4)%4];
        mass.push_back(std::exp(sum)); total+=mass.back();
    }
    BeliefSampler sampler; Rng rng(98213);
    for(int i=0;i<n;++i) {
        ++uniform[hands(world(sampler.sample(o,{},rng,soon())).match)];
        ++weighted[hands(world(sampler.sample(o,logits,rng,soon())).match)];
    }
    CHECK("no infeasible uniform assignments",uniform.size()==feasible.size());
    CHECK("learned mixture retains all feasible support",weighted.size()==feasible.size());
    for(std::size_t i=0;i<feasible.size();++i) {
        CHECK("uniform completion frequencies",std::abs(uniform[feasible[i]]/double(n)-1.0/feasible.size())<tolerance);
        CHECK("weighted completion mixture frequencies",std::abs(weighted[feasible[i]]/double(n)-(.1/feasible.size()+.9*mass[i]/total))<tolerance);
    }
    const auto& d=sampler.diagnostics();
    CHECK("diagnostic successful samples",d.attempts==2*n && d.feasible==2*n && d.unique==feasible.size() && !d.constraint_failures);
    CHECK("90/10 component frequency",std::abs(d.learned_samples/double(n)-.9)<.04);
    std::cout << "Sampler exact distribution: " << feasible.size() << " worlds, " << n << " per arm, tolerance " << tolerance << ", learned=" << d.learned_samples << " uniform=" << d.uniform_samples << std::endl;
}
void corpus_and_calibration() {
    std::ifstream corpus(HYBRID_FIXTURE_PATH); std::string line; BeliefSampler sampler; Rng rng(408);
    int snapshots=0,calibrated=0,labelCards=0; double uniformBrier=0,learnedBrier=0;
    while(std::getline(corpus,line)) {
        auto f=beu::json::parse(line); auto m=initial(f); std::array<SeatMemory,4> mem; bool calibratedCase=false;
        for(const auto& step:f.at("steps").array()) {
            auto kind=step.at("kind").string();
            if(kind=="redeal") { m.redeal(deal_from_json(step.at("deal"))); continue; }
            int seat=m.full_state().at("currentSeat").integer(); auto o=mem[seat].observe(m.view(seat));
            auto r=sampler.sample(o,{},rng,soon());
            if(!std::holds_alternative<SampledWorld>(r)) throw std::runtime_error("sampler corpus failure "+f.at("id").string()+" "+beu::parse_position(o.position).phase);
            auto w=world(std::move(r)); validate(w,o); ++snapshots;
            // Recreate sampled opponents' own actor-only histories independently.
            // Every seat already observed its initial hand during the auction.
            if(beu::parse_position(o.position).phase=="Playing") for(int other=0;other<4;++other) if(other!=seat) {
                auto copy=w.memories[other]; auto observed=copy.observe(w.match.view(other));
                CHECK("other seat retains own initial hand",observed.recall.initial_hand.has_value());
                CHECK("other memory not root recall",observed.recall.initial_hand!=o.recall.initial_hand);
            }
            if(f.at("id").string().starts_with("random-") && calibrated<8 && !calibratedCase && beu::parse_position(o.position).phase=="Playing") {
                // Fixed, untrained ownership probe independent of held-out labels.
                // This is calibration plumbing evidence, not model quality/strength.
                std::array<float,96> logits{}; for(int c=0;c<24;++c) for(int s=0;s<4;++s) logits[4*c+s]=static_cast<float>(((c+3*s)%7)-3)*.3f;
                std::array<std::array<double,4>,24> u{},l{}; constexpr int draws=24;
                for(int n=0;n<draws;++n) {
                    auto a=hands(world(sampler.sample(o,{},rng,soon())).match),b=hands(world(sampler.sample(o,logits,rng,soon())).match);
                    for(int s=0;s<4;++s) { for(int c:beu::cards_in(a[s])) u[c][s]+=1.0/draws; for(int c:beu::cards_in(b[s])) l[c][s]+=1.0/draws; }
                }
                auto truth=hands(m); int cards=0;
                for(int s=0;s<4;++s) if(s!=seat) for(int c:beu::cards_in(truth[s])) {
                    ++cards; for(int owner=0;owner<4;++owner) { uniformBrier+=std::pow(u[c][owner]-(owner==s),2); learnedBrier+=std::pow(l[c][owner]-(owner==s),2); }
                }
                CHECK("heldout labels contain unknown cards",cards>0); ++calibrated; labelCards+=cards; calibratedCase=true;
            }
            if(kind=="action") { auto a=static_cast<ActionId>(step.at("id").integer()); mem[seat].record_own_action(a); m.apply(a); }
            else m.apply_illegal_play_penalty(seat);
        }
        for(int seat=0;seat<4;++seat) { auto o=mem[seat].observe(m.view(seat)); validate(world(sampler.sample(o,{},rng,soon())),o); ++snapshots; }
    }
    std::cout << "Sampler corpus: " << snapshots << " observations; heldout untrained-probe Brier means (" << calibrated << " observations, 24 draws/arm): uniform=" << uniformBrier/labelCards << " learned90/uniform10=" << learnedBrier/labelCards << std::endl;
}

}
void test_sampler() {
    BeliefSampler sampler; Rng rng(813);
    auto f=load_case("partners-best-same-card"); auto m=initial(f); std::array<SeatMemory,4> memories;
    auto first=memories[0].observe(m.view(0));
    CHECK("deadline honored",std::get<SampleFailure>(sampler.sample(first,{},rng,std::chrono::steady_clock::now()-std::chrono::seconds(1)))==SampleFailure::Deadline);
    for(const auto& step:f.at("steps").array()) {
        int seat=m.full_state().at("currentSeat").integer(); auto o=memories[seat].observe(m.view(seat));
        validate(world(sampler.sample(o,{},rng,soon())),o);
        auto id=static_cast<ActionId>(step.at("id").integer()); memories[seat].record_own_action(id); m.apply(id);
    }
    auto root=memories[0].observe(m.view(0)); auto w=world(sampler.sample(root,{},rng,soon())); validate(w,root);
    CHECK("same-card recalled",root.recall.sent_exchange==root.recall.received_exchange && root.recall.sent_exchange==1U);
    auto bad=root; bad.recall.received_exchange=2U;
    CHECK("constraint retained",std::get<SampleFailure>(sampler.sample(bad,{},rng,soon()))==SampleFailure::Inconsistent);
    auto late=SeatMemory{}.observe(m.view(0)); validate(world(sampler.sample(late,{},rng,soon())),late);
    auto restored=SeatMemory::from_observation(late);
    CHECK("restore same snapshot",restored.observe(late.position).features==late.features);
    PublicSummary prior; prior.seats[0].bid_observations=20; prior.seats[0].passes=10;
    auto seeded=SeatMemory::with_public_summary(prior); auto seededFirst=seeded.observe(first.position);
    CHECK("card-free public seed",seededFirst.summary.seats[0].bid_observations==20);
    auto repeat=seeded.observe(first.position); CHECK("public seed dedupe",repeat.summary==seededFirst.summary);
    // Only an own exchange action is known after a mid-hand restart.
    auto actionOnly=late; actionOnly.recall.own_actions={13};
    for(int n=0;n<20;++n) validate(world(sampler.sample(actionOnly,{},rng,soon())),actionOnly);
    // Fixed seed reproduces both component selection and hidden worlds.
    Rng a(99),b(99); auto x=world(sampler.sample(first,{},a,soon())); auto y=world(sampler.sample(first,{},b,soon()));
    CHECK("deterministic seed",json_stringify(x.match.full_state())==json_stringify(y.match.full_state()));
    // Find an already-entered counting recursion that is interrupted, rather
    // than merely checking the expired-deadline guard at sample entry.
    bool interruptedCounting=false;
    for(int micros=25;micros<=25600 && !interruptedCounting;micros*=2) {
        BeliefSampler bounded; Rng seed(76);
        auto result=bounded.sample(first,{},seed,std::chrono::steady_clock::now()+std::chrono::microseconds(micros));
        interruptedCounting=std::holds_alternative<SampleFailure>(result) && std::get<SampleFailure>(result)==SampleFailure::Deadline && bounded.diagnostics().counting_nodes>1;
    }
    CHECK("deadline interrupts internal counting recursion",interruptedCounting);
    frequencies(); corpus_and_calibration();
}
}
