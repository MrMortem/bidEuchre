#include "hybrid/observation.hpp"
#include "hybrid/actions.hpp"
#include "hybrid/json_io.hpp"
#include <algorithm>
#include <bit>
#include <cmath>
#include <stdexcept>

namespace hybrid {
namespace {
using namespace schema;
using beu::CardMask;
void require(bool ok, const char* message) { if(!ok) throw beu::json::Error(message); }
int relative(int seat,int observer) { require(seat>=0 && seat<4,"invalid seat"); return (seat-observer+4)%4; }
std::size_t phase_index(const std::string& phase) {
    auto it=std::find(phases.begin(),phases.end(),phase);
    require(it!=phases.end(),"unknown phase"); return it-phases.begin();
}
Json select(const Json& value,std::initializer_list<const char*> keys) {
    Json::Object result;
    for(auto key:keys) if(const auto* v=value.find(key)) result.emplace(key,*v);
    return Json(result);
}
Json cards(const Json& value) {
    Json::Array result;
    for(const auto& c:value.array()) result.push_back(select(c,{"code"}));
    return Json(result);
}
Json plays(const Json& value) {
    Json::Array result;
    for(const auto& p:value.array()) {
        auto o=select(p,{"seat"}).object(); o["card"]=select(p.at("card"),{"code"}); result.emplace_back(o);
    }
    return Json(result);
}
Json sanitize(const Json& input) {
    int observer=input.at("seat").integer(); relative(observer,observer);
    const auto& game=input.at("game");
    auto g=select(game,{"phase","handNumber","dealSerial","dealer","currentSeat","scores","highBid","bidder","tricksByTeam","gameWinner"}).object();
    Json::Array players,auction,tricks;
    for(const auto& p:game.at("players").array()) {
        auto o=select(p,{"seat","cardCount","isSittingOut"}).object();
        o["cards"]=p.at("seat").integer()==observer?cards(p.at("cards")):Json(); players.emplace_back(o);
    }
    for(const auto& a:game.at("auction").array()) auction.push_back(select(a,{"seat","bid"}));
    for(const auto& t:game.at("completedTricks").array()) {
        auto o=select(t,{"leader","winner"}).object(); o["plays"]=plays(t.at("plays")); tricks.emplace_back(o);
    }
    g["players"]=Json(players); g["auction"]=Json(auction); g["completedTricks"]=Json(tricks);
    g["currentTrick"]=plays(game.at("currentTrick"));
    g["contract"]=game.at("contract").is_null()?Json():select(game.at("contract"),{"bid","mode","trump"});
    const auto& legal=game.at("legalActions"); auto l=select(legal,{"canPass","bids","contractModes","trumpSuits"}).object();
    if(const auto* c=legal.find("cards")) l["cards"]=cards(*c);
    g["legalActions"]=Json(l);
    return Json(Json::Object{{"seat",input.at("seat")},{"game",Json(g)}});
}
std::optional<std::uint64_t> serial(const Json& position) {
    const auto* v=position.at("game").find("dealSerial");
    if(!v || v->is_null()) return {};
    double n=v->number(); require(std::isfinite(n) && n>=1 && n<=9007199254740991.0 && std::floor(n)==n,"invalid dealSerial");
    return static_cast<std::uint64_t>(n);
}
std::vector<beu::CardPlay> all_plays(const beu::Position& p) {
    std::vector<beu::CardPlay> result;
    for(const auto& t:p.completed_tricks) result.insert(result.end(),t.plays.begin(),t.plays.end());
    result.insert(result.end(),p.current_trick.begin(),p.current_trick.end()); return result;
}
CardMask own_played(const beu::Position& p) {
    CardMask result=0; for(auto play:all_plays(p)) if(play.seat==p.seat) result|=1U<<play.card; return result;
}
bool same_contract(const beu::Contract& a,const beu::Contract& b) {
    return a.valid==b.valid && (!a.valid || (a.bid==b.bid && a.mode==b.mode && a.trump==b.trump));
}
bool public_extension(const beu::Position& before,const beu::Position& after) {
    if(before.dealer!=after.dealer || phase_index(after.phase)<phase_index(before.phase) ||
       before.auction.size()>after.auction.size() || !std::equal(before.auction.begin(),before.auction.end(),after.auction.begin()) ||
       (before.contract.valid && !same_contract(before.contract,after.contract))) return false;
    auto a=all_plays(before),b=all_plays(after);
    return a.size()<=b.size() && std::equal(a.begin(),a.end(),b.begin(),[](auto x,auto y){return x.seat==y.seat && x.card==y.card;});
}
void validate(const beu::Position& p,const Json& position) {
    relative(p.dealer,p.seat); if(p.current_seat>=0) relative(p.current_seat,p.seat); if(p.bidder>=0) relative(p.bidder,p.seat);
    phase_index(p.phase);
    require(p.auction.size()<=4 && p.current_trick.size()<=4 && p.completed_tricks.size()<=6,"oversized public history");
    std::array<bool,4> seen{};
    for(const auto& player:position.at("game").at("players").array()) {
        int s=player.at("seat").integer(); relative(s,p.seat); require(!seen[s],"duplicate player seat"); seen[s]=true;
        require(p.card_counts[s]>=0 && p.card_counts[s]<=7,"invalid card count");
    }
    require(std::popcount(p.hand)==p.card_counts[p.seat],"private card count mismatch");
    for(auto [s,b]:p.auction) { relative(s,p.seat); require(b==0 || (b>=3 && b<=8),"invalid bid"); }
    for(const auto& t:p.completed_tricks) { relative(t.winner,p.seat); require(!t.plays.empty() && t.plays.size()<=4,"invalid completed trick"); }
    for(auto play:all_plays(p)) { relative(play.seat,p.seat); require(play.card>=0 && play.card<24,"invalid play"); }
    if(p.contract.valid) require(p.contract.mode!=beu::Mode::Trump || (p.contract.trump>=0 && p.contract.trump<4),"missing trump");
    if(const auto* w=position.at("game").find("gameWinner"); w && !w->is_null()) require(w->integer()>=0 && w->integer()<2,"invalid winner");
}
// All evidence is private to this observer or publicly played. Subtracting the
// post-discard base also identifies a sent card returned unchanged.
void update_recall(SeatRecall& r,const beu::Position& p,bool fresh) {
    auto stage=phase_index(p.phase);
    if(fresh && stage>=1 && stage<=3 && std::popcount(p.hand)==6) r.initial_hand=p.hand;
    bool participant=p.contract.valid && p.contract.bid==7 && (p.seat==p.bidder || p.seat==(p.bidder+2)%4);
    if(stage<4 || (p.contract.valid && !participant)) {
        if(!r.sent_exchange) r.sent_exchange=0;
        if(!r.received_exchange) r.received_exchange=0;
    }
    if(!participant) return;
    CardMask total=p.hand|own_played(p);
    if(p.seat==p.bidder) {
        if(stage==4 && r.initial_hand && std::popcount(total)==5 && !(total&~*r.initial_hand)) r.sent_exchange=*r.initial_hand&~total;
        if(stage>=4 && (!r.sent_exchange || !*r.sent_exchange)) r.sent_exchange.reset();
        if(stage>=5) {
            r.received_exchange.reset();
            if(r.initial_hand && r.sent_exchange) {
                CardMask base=*r.initial_hand&~*r.sent_exchange, received=total&~base;
                if((total&base)==base && std::popcount(total)==6 && std::popcount(received)==1) r.received_exchange=received;
            }
        }
    } else {
        if(stage>=4 && r.initial_hand && (!r.received_exchange || !*r.received_exchange)) {
            CardMask received=total&~*r.initial_hand;
            if(std::popcount(received)==1) r.received_exchange=received;
            else r.received_exchange.reset();
        }
        if(stage>=5 && (!r.sent_exchange || !*r.sent_exchange)) {
            r.sent_exchange.reset();
            if(r.initial_hand && r.received_exchange) {
                CardMask seven=*r.initial_hand|*r.received_exchange, sent=seven&~total;
                if(!(total&~seven) && std::popcount(sent)==1) r.sent_exchange=sent;
            }
        }
    }
}
bool private_extension(const beu::Position& p,const beu::Position& old,const SeatRecall& recall) {
    CardMask before=old.hand|own_played(old),after=p.hand|own_played(p);
    if(before==after) return true;
    bool pb=p.contract.valid && p.contract.bid==7 && (p.seat==p.bidder || p.seat==(p.bidder+2)%4);
    if(!pb) return false;
    auto candidate=recall; update_recall(candidate,p,false);
    if(!candidate.initial_hand) return false;
    CardMask expected=*candidate.initial_hand;
    if(candidate.sent_exchange) expected&=~*candidate.sent_exchange;
    if(candidate.received_exchange) expected|=*candidate.received_exchange;
    return expected==after;
}
template<std::size_t N> void summaries(std::array<float,N>& f,std::size_t offset,int observer,const PublicSummary& summary) {
    for(int s=0;s<4;++s) {
        const auto& c=summary.seats[s]; auto base=offset+relative(s,observer)*session_summary_size;
        f[base+session_summary_fields_bid_observations_offset]=std::min<std::uint64_t>(c.bid_observations,100)/100.0f;
        f[base+session_summary_fields_pass_rate_offset]=static_cast<float>((c.passes+1.0)/(c.bid_observations+2.0));
        f[base+session_summary_fields_solo_bid_rate_offset]=static_cast<float>((c.solo_bids+1.0)/(c.bid_observations+2.0));
        f[base+session_summary_fields_average_numeric_bid_offset]=c.numeric_bids?static_cast<float>(c.numeric_bid_sum/(c.numeric_bids*6.0)):0;
        f[base+session_summary_fields_contract_observations_offset]=std::min<std::uint64_t>(c.contract_observations,100)/100.0f;
        f[base+session_summary_fields_high_rate_offset]=static_cast<float>((c.modes[0]+1.0)/(c.contract_observations+3.0));
        f[base+session_summary_fields_low_rate_offset]=static_cast<float>((c.modes[1]+1.0)/(c.contract_observations+3.0));
        f[base+session_summary_fields_trump_rate_offset]=static_cast<float>((c.modes[2]+1.0)/(c.contract_observations+3.0));
    }
}
Features encode(const beu::Position& p,const SeatObservation& o) {
    Features f{};
    auto rel=[&](int s){return relative(s,p.seat);};
    f[main_blocks_phase_offset+phase_index(p.phase)]=1;
    f[main_blocks_relative_dealer_offset+rel(p.dealer)]=1;
    f[main_blocks_current_seat_offset+(p.current_seat<0?4:rel(p.current_seat))]=1;
    for(int t=0;t<2;++t) { f[main_blocks_scores_offset+t]=p.scores[(p.seat+t)%2]/scores_divisor; f[main_blocks_team_tricks_offset+t]=p.tricks[(p.seat+t)%2]/trick_counts_divisor; }
    for(int s=0;s<4;++s) { f[main_blocks_card_counts_offset+rel(s)]=p.card_counts[s]/card_counts_divisor; f[main_blocks_sitting_flags_offset+rel(s)]=p.sitting[s]; }
    auto mask=[&](std::size_t offset,CardMask cards){for(int c:beu::cards_in(cards)) f[offset+c]=1;};
    mask(main_blocks_current_hand_offset,p.hand);
    auto recall=[&](std::size_t offset,std::optional<CardMask> cards){if(cards){mask(offset+recall_fields_cards_offset,*cards);f[offset+recall_fields_known_offset]=1;}};
    recall(main_blocks_initial_hand_offset,o.recall.initial_hand); recall(main_blocks_sent_exchange_offset,o.recall.sent_exchange); recall(main_blocks_received_exchange_offset,o.recall.received_exchange);
    for(std::size_t i=0;i<p.auction.size();++i) { auto [s,b]=p.auction[i]; auto base=main_blocks_auction_offset+i*auction_action_size;
        f[base+auction_action_fields_present_offset]=1; f[base+auction_action_fields_seat_offset+rel(s)]=1; f[base+auction_action_fields_bid_offset+(b?b-2:0)]=1; }
    f[main_blocks_high_bid_offset+(p.high_bid?p.high_bid-2:0)]=1;
    f[main_blocks_bidder_offset+(p.bidder<0?4:rel(p.bidder))]=1;
    auto base=main_blocks_contract_offset; const auto& c=p.contract;
    f[base+contract_fields_present_offset]=c.valid;
    f[base+contract_fields_bid_offset+(c.valid?c.bid-2:0)]=1;
    f[base+contract_fields_mode_offset+(c.valid?static_cast<int>(c.mode)+1:0)]=1;
    f[base+contract_fields_trump_offset+(c.valid && c.mode==beu::Mode::Trump?c.trump+1:0)]=1;
    f[base+contract_fields_partner_sits_offset]=c.valid && c.partner_sits();
    auto write_plays=[&](std::size_t offset,const auto& ps){ for(std::size_t i=0;i<ps.size();++i) {
        auto b=offset+i*play_size; f[b+play_fields_present_offset]=1; f[b+play_fields_seat_offset+rel(ps[i].seat)]=1; f[b+play_fields_card_offset+ps[i].card]=1;
        if(i>0) { int led=beu::effective_suit(ps[0].card,c); if(beu::effective_suit(ps[i].card,c)!=led) f[main_blocks_public_voids_offset+rel(ps[i].seat)*4+led]=1; }
    }};
    write_plays(main_blocks_current_trick_offset,p.current_trick);
    for(std::size_t i=0;i<p.completed_tricks.size();++i) { const auto& t=p.completed_tricks[i]; auto b=main_blocks_completed_tricks_offset+i*completed_trick_size;
        f[b+completed_trick_fields_present_offset]=1; f[b+completed_trick_fields_leader_offset+rel(t.plays.front().seat)]=1; f[b+completed_trick_fields_winner_offset+rel(t.winner)]=1;
        write_plays(b+completed_trick_fields_plays_offset,t.plays); }
    const auto* w=o.position.at("game").find("gameWinner"); f[main_blocks_winner_offset+(!w || w->is_null()?2:(w->integer()+p.seat)%2)]=1;
    for(std::size_t i=0;i<o.legal.size();++i) f[main_blocks_legal_mask_offset+i]=o.legal[i];
    for(int card:beu::cards_in(p.hand)) f[main_blocks_own_effective_suit_counts_offset+beu::effective_suit(card,c)]+=1.0f/suit_counts_divisor;
    summaries(f,main_blocks_public_session_summaries_offset,p.seat,o.summary); return f;
}
} // namespace

SeatObservation SeatMemory::observe(const Json& privatePosition) {
    SeatObservation result; result.position=sanitize(privatePosition);
    auto p=beu::parse_position(result.position); validate(p,result.position);
    result.legal=legal_mask(result.position); auto nextSerial=serial(result.position);
    auto snapshot=json_stringify(result.position);
    if(previous_) require(previous_->seat==p.seat,"SeatMemory cannot change observer without new_game");
    if(previous_ && snapshot_==snapshot) {
        result.recall=recall_; result.summary=summary_; result.features=encode(p,result); return result;
    }
    bool fresh=!previous_ || serial_!=nextSerial || previous_->hand_number!=p.hand_number;
    if(previous_ && !fresh && (!public_extension(*previous_,p) || (!nextSerial && !private_extension(p,*previous_,recall_)))) fresh=true;
    if(fresh) { recall_={}; action_recorded_=false; if(!nextSerial) ++legacy_epoch_; }
    else if(snapshot_!=snapshot) action_recorded_=false;
    update_recall(recall_,p,fresh);
    // Each chronological auction location and the unique contract location are
    // counted once per deal, even when intermediate observations were skipped.
    const auto dealKey=nextSerial ? "serial:"+std::to_string(*nextSerial) : "legacy:"+std::to_string(legacy_epoch_);
    for(std::size_t i=0;i<p.auction.size();++i) {
        if(!public_events_.insert(dealKey+":bid:"+std::to_string(i)).second) continue;
        auto [seat,bid]=p.auction[i]; auto& c=summary_.seats[seat]; ++c.bid_observations;
        if(bid==0) ++c.passes;
        else if(bid>=7) ++c.solo_bids;
        else { ++c.numeric_bids; c.numeric_bid_sum+=bid; }
    }
    if(p.contract.valid && public_events_.insert(dealKey+":contract").second) { require(p.bidder>=0,"contract requires bidder"); auto& c=summary_.seats[p.bidder]; ++c.contract_observations; ++c.modes[static_cast<int>(p.contract.mode)]; }
    previous_=p; serial_=nextSerial; snapshot_=snapshot; legal_=result.legal;
    result.recall=recall_; result.summary=summary_; result.features=encode(p,result); return result;
}
void SeatMemory::record_own_action(ActionId action) {
    require(previous_.has_value() && previous_->current_seat==previous_->seat,"action needs own decision observation");
    require(!action_recorded_,"action already recorded for this observation");
    require(action<action_count && legal_[action],"cannot record illegal action");
    recall_.own_actions.push_back(action); action_recorded_=true;
    if(action>=13 && action<=36) recall_.sent_exchange=1U<<(action-13);
}
void SeatMemory::new_game() { *this=SeatMemory{}; }
SeatMemory SeatMemory::from_observation(const SeatObservation& observation) {
    SeatMemory memory; memory.observe(observation.position);
    memory.recall_=observation.recall; memory.summary_=observation.summary;
    return memory;
}
SeatMemory SeatMemory::with_public_summary(const PublicSummary& summary) {
    SeatMemory memory; memory.summary_=summary; return memory;
}
SeatMemory SeatMemory::from_private_history(const std::vector<Json>& positions,const std::vector<ActionId>& ownActions) {
    SeatMemory memory; std::size_t action=0;
    for(const auto& position:positions) {
        auto observation=memory.observe(position);
        if(action<ownActions.size() && !memory.action_recorded_ && std::any_of(observation.legal.begin(),observation.legal.end(),[](bool v){return v;})) memory.record_own_action(ownActions[action++]);
    }
    require(action==ownActions.size(),"history actions lack matching decision observations"); return memory;
}
BoundaryFeatures encode_boundary(const std::array<int,2>& scores,int nextDealer,int observer,const PublicSummary& summary) {
    relative(observer,observer); BoundaryFeatures f{};
    for(int t=0;t<2;++t) f[boundary_blocks_scores_offset+t]=scores[(observer+t)%2]/scores_divisor;
    f[boundary_blocks_next_dealer_offset+relative(nextDealer,observer)]=1;
    summaries(f,boundary_blocks_public_session_summaries_offset,observer,summary); return f;
}
} // namespace hybrid
