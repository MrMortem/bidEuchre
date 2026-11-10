#include "hybrid/model.hpp"
#include "hybrid/observation.hpp"
#include "hybrid/match.hpp"
#include "hybrid/json_io.hpp"
#include <fstream>
#include <iostream>
#include <stdexcept>
namespace {
using namespace hybrid;
template<class Range> Json array(const Range& values) {
    Json::Array result;for(auto value:values) result.emplace_back(static_cast<double>(value));return Json(result);
}
Json mask_json(const ActionMask& mask) {Json::Array result;for(bool value:mask) result.emplace_back(value);return Json(result);}
template<std::size_t N> std::array<float,N> features(const Json& value) {
    if(value.array().size()!=N) throw std::invalid_argument("invalid input shape");
    std::array<float,N> result{};for(std::size_t i=0;i<N;++i) result[i]=static_cast<float>(value.array()[i].number());return result;
}
void emit(const FrozenModel& model,const Features& x,const ActionMask& legal,const BoundaryFeatures& boundary,Json exact={}) {
    auto raw=model.evaluate(x);auto p=normalize_output(raw,legal);float boundary_logit=model.boundary_logit(boundary);
    std::size_t phase=0;for(std::size_t i=1;i<8;++i) if(x[i]>x[phase]) phase=i;
    Json::Object row={{"phase",Json(std::string(schema::phases[phase]))},{"features",array(x)},{"legal",mask_json(legal)},
        {"boundary_features",array(boundary)},{"policy_logits",array(raw.policy_logits)},
        {"win_logit",array(std::array{raw.win_logit})},{"trick_logits",array(raw.trick_logits)},
        {"ownership_logits",array(raw.ownership_logits)},{"policy",array(p.policy)},
        {"win",array(std::array{p.win})},{"trick",array(p.trick)},{"ownership",array(p.ownership)},
        {"boundary_logit",array(std::array{boundary_logit})},{"terminal_win_probability",exact}};
    std::cout<<json_stringify(Json(row))<<'\n';
}
void observe(const FrozenModel& model,SeatMemory& memory,const Json& position) {
    auto observation=memory.observe(position);const auto& game=observation.position.at("game");
    const auto& scores=game.at("scores").array();int seat=observation.position.at("seat").integer();
    auto boundary=encode_boundary({scores[0].integer(),scores[1].integer()},(game.at("dealer").integer()+1)%4,seat,observation.summary);
    Json exact;
    if(game.at("phase").string()=="GameComplete" && !game.at("gameWinner").is_null())
        exact=Json(game.at("gameWinner").integer()==seat%2?1.:0.);
    emit(model,observation.features,observation.legal,boundary,exact);
}
void fixture(const FrozenModel& model,const Json& value) {
    const auto& initial=value.at("initial");const auto& scores=initial.at("scores").array();NativeMatch match;
    match.start(deal_from_json(initial),initial.at("dealer").integer(),{scores[0].integer(),scores[1].integer()},
        initial.at("handNumber").integer(),static_cast<std::uint64_t>(initial.at("dealSerial").number()));
    std::array<SeatMemory,4> memories;
    for(const auto& step:value.at("steps").array()) {
        const auto& kind=step.at("kind").string();
        if(kind=="redeal") {match.redeal(deal_from_json(step.at("deal")));continue;}
        int seat=step.at("seat").integer();if(seat<0 || seat>3) throw std::invalid_argument("invalid fixture seat");
        observe(model,memories[seat],match.view(seat));
        if(kind=="action") {
            int id=step.at("id").integer();if(id<0 || id>60) throw std::invalid_argument("invalid fixture action");
            memories[seat].record_own_action(static_cast<ActionId>(id));match.apply(static_cast<ActionId>(id));
        } else if(kind=="penalty") match.apply_illegal_play_penalty(seat);
        else throw std::invalid_argument("invalid fixture step");
    }
    for(int seat=0;seat<4;++seat) observe(model,memories[seat],match.view(seat));
}
}
int main(int argc,char** argv) {
    try {
        if(argc!=5 || std::string(argv[1])!="--manifest" || std::string(argv[3])!="--inputs")
            throw std::invalid_argument("Usage: bideuchre-hybrid-model-check --manifest PATH --inputs PATH");
        auto model=hybrid::FrozenModel::load(argv[2]);
        std::ifstream input(argv[4]);if(!input) throw std::invalid_argument("cannot open inputs");
        std::string line;
        while(std::getline(input,line)) {
            if(line.empty()) continue;auto record=beu::json::parse(line);
            if(auto f=record.find("fixture")) {fixture(model,*f);continue;}
            if(auto p=record.find("position")) {hybrid::SeatMemory memory;observe(model,memory,*p);continue;}
            auto x=features<1188>(record.at("features"));auto boundary=features<38>(record.at("boundary_features"));
            const auto& values=record.at("legal").array();if(values.size()!=61) throw std::invalid_argument("invalid legal shape");
            hybrid::ActionMask mask{};for(std::size_t i=0;i<61;++i) mask[i]=values[i].boolean();
            emit(model,x,mask,boundary);
        }
        return 0;
    } catch(const std::exception& error) {std::cerr<<"Model check failure: "<<error.what()<<'\n';return 1;}
}
