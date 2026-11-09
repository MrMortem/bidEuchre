#include "hybrid/match.hpp"
#include <algorithm>
#include <bit>
#include <limits>
#include <stdexcept>

namespace hybrid {
namespace {
Json num(double n) { return Json(n); }
Json str(std::string_view s) { return Json(std::string(s)); }
Json optional(int n) { return n<0 ? Json() : num(n); }
Json ints(std::array<int,2> a) { return Json(Json::Array{num(a[0]),num(a[1])}); }
void seat_valid(int seat) { if (seat<0 || seat>3) throw std::invalid_argument("seat must be 0..3"); }
void validate_deal(const Deal& d) {
    beu::CardMask seen=0;
    for (auto hand:d.hands) {
        if ((hand >> 24) || std::popcount(hand)!=6 || (seen&hand)) throw std::invalid_argument("deal requires unique four six-card hands");
        seen |= hand;
    }
    if (seen!=0xffffff) throw std::invalid_argument("deal must contain complete deck");
}
void validate_contract(const beu::Contract& c) {
    if (!c.valid || c.bid<3 || c.bid>8 ||
        (c.mode!=beu::Mode::High && c.mode!=beu::Mode::Low && c.mode!=beu::Mode::Trump) ||
        ((c.bid==3 || c.bid==7) && c.mode!=beu::Mode::Trump) ||
        (c.mode==beu::Mode::Trump ? c.trump<0 || c.trump>3 : c.trump!=-1))
        throw std::invalid_argument("invalid contract");
}
std::optional<int> winner(std::array<int,2> s) {
    if ((s[0]<40 && s[1]<40) || s[0]==s[1]) return {};
    return s[0]>s[1] ? 0:1;
}
std::array<int,2> add_scores(std::array<int,2> s,std::array<int,2> d) {
    for (int i=0;i<2;++i) {
        const auto n=static_cast<long long>(s[i])+d[i];
        if (n<std::numeric_limits<int>::min() || n>std::numeric_limits<int>::max()) throw std::overflow_error("score overflow");
        s[i]=static_cast<int>(n);
    }
    return s;
}
std::string_view mode_name(beu::Mode m) { return m==beu::Mode::High ? "High" : m==beu::Mode::Low ? "Low" : "Trump"; }
Json contract_json(const beu::Contract& c, bool wire=false) {
    if (!c.valid) return {};
    Json::Object o{{"bid",wire?str(schema::bid_names[c.bid-3]):num(c.bid)},
                   {"mode",str(mode_name(c.mode))},{"trump",c.trump<0?Json():wire?str(schema::suit_names[c.trump]):num(c.trump)}};
    if (wire) { o["requiredTricks"]=num(c.required_tricks()); o["isPartnersBest"]=Json(c.bid==7); o["isAlone"]=Json(c.bid==8); }
    return Json(o);
}
Json card_json(int c) {
    static constexpr std::array<std::string_view,6> ranks{"Nine","Ten","Jack","Queen","King","Ace"};
    return Json(Json::Object{{"suit",str(schema::suit_names[c/6])},{"rank",str(ranks[c%6])},{"code",str(schema::card_codes[c])}});
}
Json plays(const std::vector<beu::CardPlay>& ps, bool wire=false) {
    Json::Array a;
    for (auto p:ps) a.emplace_back(Json::Object{{"seat",num(p.seat)},{"card",wire?card_json(p.card):num(p.card)}});
    return Json(a);
}
} // namespace

HandResult score_hand(const beu::Contract& c,int bidder,std::array<int,2> tricks,std::array<int,2> before) {
    seat_valid(bidder); validate_contract(c);
    if (tricks[0]<0 || tricks[1]<0 || tricks[0]>6 || tricks[1]>6 || tricks[0]+tricks[1]!=6)
        throw std::invalid_argument("completed hand requires six tricks");
    int team=bidder%2; std::array<int,2> deltas{};
    deltas[team]=c.bid>=7 ? (tricks[team]==6?1:-1)*(c.bid==7?12:24)
                         : tricks[team]>=c.required_tricks()?tricks[team]:-c.required_tricks();
    deltas[1-team]=tricks[1-team];
    auto after=add_scores(before,deltas);
    return {before,deltas,tricks,after,winner(after)};
}

void NativeMatch::start(const Deal& d,int dealer,std::array<int,2> scores,int handNumber,std::uint64_t serial) {
    validate_deal(d); seat_valid(dealer);
    // JSON numbers exactly represent identities through 2^53-1.
    if (handNumber<1 || serial<1 || serial>9007199254740991ULL) throw std::invalid_argument("invalid hand identity");
    if (winner(scores)) throw std::invalid_argument("cannot start a won match");
    dealer_=dealer; scores_=scores; handNumber_=handNumber; serial_=serial;
    history_.clear(); result_.reset(); deal(d);
}
void NativeMatch::deal(const Deal& d) {
    deal_=d; phase_=Phase::Bidding; actor_=(dealer_+1)%4;
    highBid_=0; bidder_=-1; contract_={}; auction_.clear(); trick_.clear(); completed_.clear();
    tricks_={}; winner_.reset();
}
int NativeMatch::sitting() const {
    return contract_.valid && contract_.partner_sits() && phase_>=Phase::Playing ? (bidder_+2)%4 : -1;
}
ActionMask NativeMatch::legal_actions() const {
    ActionMask mask{};
    if (phase_==Phase::Bidding) {
        mask[0]=actor_!=dealer_ || highBid_!=0;
        for(int bid=3;bid<=8;++bid) if(bid>highBid_) mask[bid-2]=true;
    } else if(phase_==Phase::ChoosingContract) {
        if(highBid_!=3 && highBid_!=7) mask[7]=mask[8]=true;
        for(int id=9;id<=12;++id) mask[id]=true;
    } else if(phase_==Phase::ExchangingBidderCard || phase_==Phase::ExchangingPartnerCard || phase_==Phase::Playing) {
        auto cards=phase_==Phase::Playing ? beu::legal_cards(deal_.hands[actor_],trick_,contract_):deal_.hands[actor_];
        for(int c:beu::cards_in(cards)) mask[c+(phase_==Phase::Playing?37:13)]=true;
    }
    return mask;
}
void NativeMatch::begin_play() { phase_=Phase::Playing; actor_=beu::next_active(dealer_,sitting()); }
void NativeMatch::apply(ActionId id) {
    if(id>=61 || !legal_actions()[id]) throw std::invalid_argument("illegal action");
    if(phase_==Phase::Bidding) {
        int bid=id==0?0:id+2; auction_.emplace_back(actor_,bid);
        if(bid) { highBid_=bid; bidder_=actor_; }
        if(auction_.size()==4) { phase_=Phase::ChoosingContract; actor_=bidder_; }
        else actor_=(actor_+1)%4;
    } else if(phase_==Phase::ChoosingContract) {
        contract_={highBid_,id==7?beu::Mode::High:id==8?beu::Mode::Low:beu::Mode::Trump,id>=9?id-9:-1,true};
        if(highBid_==7) phase_=Phase::ExchangingBidderCard;
        else begin_play();
    } else if(phase_==Phase::ExchangingBidderCard || phase_==Phase::ExchangingPartnerCard) {
        auto card=beu::CardMask{1}<<(id-13);
        deal_.hands[actor_]&=~card; deal_.hands[(actor_+2)%4]|=card;
        if(phase_==Phase::ExchangingBidderCard) { phase_=Phase::ExchangingPartnerCard; actor_=(actor_+2)%4; }
        else begin_play();
    } else {
        int card=id-37; deal_.hands[actor_]&=~(beu::CardMask{1}<<card); trick_.push_back({actor_,card});
        if(trick_.size()<static_cast<std::size_t>(contract_.partner_sits()?3:4)) actor_=beu::next_active(actor_,sitting());
        else {
            int won=beu::trick_winner(trick_,contract_);
            completed_.push_back({won,trick_}); ++tricks_[won%2]; trick_.clear(); actor_=won;
            if(completed_.size()==6) complete(score_hand(contract_,bidder_,tricks_,scores_).deltas);
        }
    }
}
void NativeMatch::complete(std::array<int,2> deltas) {
    auto after=add_scores(scores_,deltas);
    result_=HandResult{scores_,deltas,tricks_,after,winner(after)};
    scores_=after; winner_=result_->winner; actor_=-1;
    phase_=winner_?Phase::GameComplete:Phase::HandComplete;
    history_.emplace_back(Json::Object{{"handNumber",num(handNumber_)},{"bidder",num(bidder_)},
        {"contract",contract_json(contract_)},{"biddingTeamTricks",num(tricks_[bidder_%2])},
        {"defendingTeamTricks",num(tricks_[1-bidder_%2])},{"teamZeroDelta",num(deltas[0])},{"teamOneDelta",num(deltas[1])}});
}
HandResult NativeMatch::hand_result() const {
    if(phase_!=Phase::HandComplete && phase_!=Phase::GameComplete) throw std::logic_error("hand is not complete");
    return result_.value();
}
void NativeMatch::start_next_hand(const Deal& d) {
    if(phase_!=Phase::HandComplete || winner_) throw std::logic_error("next hand requires unwon HandComplete");
    validate_deal(d);
    if(handNumber_==std::numeric_limits<int>::max() || serial_==9007199254740991ULL) throw std::overflow_error("hand identity overflow");
    dealer_=(dealer_+1)%4; ++handNumber_; ++serial_; deal(d);
}
void NativeMatch::redeal(const Deal& d) {
    if(phase_==Phase::NotStarted || phase_==Phase::GameComplete) throw std::logic_error("no active hand to redeal");
    validate_deal(d);
    if(serial_==9007199254740991ULL) throw std::overflow_error("hand identity overflow");
    ++serial_; deal(d);
}
void NativeMatch::apply_illegal_play_penalty(int seat) {
    seat_valid(seat);
    if(phase_!=Phase::Playing) throw std::logic_error("penalty requires Playing");
    std::array<int,2> deltas{}; int team=bidder_%2;
    if(seat%2==team) { deltas[team]=contract_.bid==7?-12:contract_.bid==8?-24:-contract_.required_tricks(); deltas[1-team]=6; }
    else deltas[team]=contract_.bid==7?12:contract_.bid==8?24:6;
    complete(deltas);
}
Json NativeMatch::project(int seat,bool all) const {
    Json::Array players,auction,completed,legal;
    for(int s=0;s<4;++s) {
        Json cards;
        if(all || seat==s) { Json::Array a; for(int c:beu::cards_in(deal_.hands[s])) a.push_back(num(c)); cards=Json(a); }
        players.emplace_back(Json::Object{{"seat",num(s)},{"team",num(s%2)},{"cardCount",num(std::popcount(deal_.hands[s]))},{"cards",cards},{"isSittingOut",Json(s==sitting())}});
    }
    for(auto [s,b]:auction_) auction.emplace_back(Json::Object{{"seat",num(s)},{"bid",b?num(b):Json()}});
    for(std::size_t i=0;i<completed_.size();++i) {
        const auto& t=completed_[i]; completed.emplace_back(Json::Object{{"number",num(i+1)},{"leader",num(t.plays.front().seat)},{"winner",num(t.winner)},{"plays",plays(t.plays)}});
    }
    if(seat>=0 && seat==actor_) { auto mask=legal_actions(); for(int id=0;id<61;++id) if(mask[id]) legal.push_back(num(id)); }
    return Json(Json::Object{{"phase",str(schema::phases[static_cast<int>(phase_)])},{"handNumber",num(handNumber_)},{"dealSerial",num(static_cast<double>(serial_))},
        {"dealer",num(dealer_)},{"currentSeat",optional(actor_)},{"scores",ints(scores_)},{"players",Json(players)},
        {"auction",Json(auction)},{"highBid",highBid_?num(highBid_):Json()},{"bidder",optional(bidder_)},{"contract",contract_json(contract_)},
        {"currentTrick",plays(trick_)},{"completedTricks",Json(completed)},{"tricksByTeam",ints(tricks_)},
        {"gameWinner",winner_?num(*winner_):Json()},{"legal",Json(legal)},{"handResults",Json(history_)}});
}
Json NativeMatch::full_state() const { return project(-1,true); }
Json NativeMatch::private_projection(int seat) const { seat_valid(seat); return project(seat,false); }
Json NativeMatch::view(int seat) const {
    seat_valid(seat);
    auto game=project(seat,false).object(); game.erase("legal"); game.erase("handResults");
    Json::Array players;
    for(const auto& p:game.at("players").array()) {
        auto o=p.object(); int s=o.at("seat").integer(); o["name"]=str("Seat "+std::to_string(s));
        if(!o.at("cards").is_null()) {
            auto cs=beu::cards_in(deal_.hands[s]);
            std::sort(cs.begin(),cs.end(),[](int a,int b){return beu::card_code(a)<beu::card_code(b);});
            Json::Array a; for(int c:cs) a.push_back(card_json(c)); o["cards"]=Json(a);
        }
        players.emplace_back(o);
    }
    game["players"]=Json(players);
    Json::Array auction;
    for(auto [s,b]:auction_) auction.emplace_back(Json::Object{{"seat",num(s)},{"bid",b?str(schema::bid_names[b-3]):Json()},{"isPass",Json(b==0)}});
    game["auction"]=Json(auction); game["highBid"]=highBid_?str(schema::bid_names[highBid_-3]):Json();
    game["contract"]=contract_json(contract_,true); game["currentTrick"]=plays(trick_,true);
    Json::Array completed;
    for(std::size_t i=0;i<completed_.size();++i) { const auto& t=completed_[i]; completed.emplace_back(Json::Object{{"number",num(i+1)},{"leader",num(t.plays.front().seat)},{"winner",num(t.winner)},{"plays",plays(t.plays,true)}}); }
    game["completedTricks"]=Json(completed);
    ActionMask mask{}; if(seat==actor_) mask=legal_actions();
    Json::Array bids,modes,suits,cards;
    for(int id=1;id<=6;++id) if(mask[id]) bids.push_back(str(schema::bid_names[id-1]));
    if(mask[7]) modes.push_back(str("High")); if(mask[8]) modes.push_back(str("Low"));
    if(mask[9]) modes.push_back(str("Trump"));
    for(int id=9;id<=12;++id) if(mask[id]) suits.push_back(str(schema::suit_names[id-9]));
    for(int c=0;c<24;++c) if(mask[c+13] || mask[c+37]) cards.push_back(card_json(c));
    game["legalActions"]=Json(Json::Object{{"canPass",Json(mask[0])},{"bids",Json(bids)},{"contractModes",Json(modes)},{"trumpSuits",Json(suits)},{"cards",Json(cards)}});
    game["events"]=Json(Json::Array{});
    return Json(Json::Object{{"seat",num(seat)},{"game",Json(game)}});
}
Deal deal_from_json(const Json& value) {
    Deal d; const auto& hands=value.at("hands").array();
    if(hands.size()!=4) throw std::invalid_argument("four hands required");
    for(int s=0;s<4;++s) for(const auto& c:hands[s].array()) {
        int id=c.integer(); if(id<0 || id>=24 || (d.hands[s]&(1U<<id))) throw std::invalid_argument("invalid/duplicate card");
        d.hands[s]|=1U<<id;
    }
    validate_deal(d); return d;
}
} // namespace hybrid
