#include "hybrid/search.hpp"
#include "hybrid/baseline_process.hpp"
#include "hybrid/actions.hpp"
#include "hybrid/json_io.hpp"
#include "bot.hpp"
#include <algorithm>
#include <cctype>
#include <csignal>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <thread>
#ifdef __APPLE__
#include <mach-o/dyld.h>
#endif
namespace {
using namespace hybrid;
using Clock=std::chrono::steady_clock;
void output(const std::string& line){std::cout<<line<<'\n'<<std::flush;}
std::string lower(std::string s){for(auto& c:s)c=static_cast<char>(std::tolower(static_cast<unsigned char>(c)));return s;}
std::string executable(char* argv0) {
#ifdef __APPLE__
    std::uint32_t length=0;_NSGetExecutablePath(nullptr,&length);std::string path(length,'\0');if(_NSGetExecutablePath(path.data(),&length)==0){path.resize(path.find('\0'));return std::filesystem::canonical(path).string();}
#endif
    return std::filesystem::canonical(argv0).string();
}
int worker(int delayMs) {
    beu::HeuristicBot bot;output("workerready");std::string line;
    while(std::getline(std::cin,line)) {
        if(line=="newgame")bot.new_game();
        else if(line.starts_with("position "))bot.set_position(beu::parse_position(beu::json::parse(line.substr(9))));
        else if(line=="go") {if(delayMs)std::this_thread::sleep_for(std::chrono::milliseconds(delayMs));output(bot.choose_action());}
        else if(line=="quit")break;
    }return 0;
}
}
int main(int argc,char** argv) {
    using namespace hybrid;
    std::signal(SIGPIPE,SIG_IGN);
    try {
        SearchLimits limits;std::string manifest;bool isWorker=false;int workerDelay=0,samplerDelay=0;
        for(int i=1;i<argc;++i) {
            std::string arg=argv[i];auto value=[&](){if(i+1>=argc)throw std::invalid_argument("missing option value");return std::string(argv[++i]);};
            if(arg=="--baseline-worker")isWorker=true;
            else if(arg=="--model")manifest=value();else if(arg=="--seed")limits.seed=std::stoull(value());
            else if(arg=="--decision-ms")limits.decisionMs=std::clamp(std::stoi(value()),0,3000);
            else if(arg=="--stop-ms")limits.stopMs=std::clamp(std::stoi(value()),0,3500);
            else if(arg=="--max-simulations")limits.maxSimulations=std::max(0,std::stoi(value()));
            else if(arg=="--worker-delay-ms")workerDelay=std::clamp(std::stoi(value()),0,10000);
            else if(arg=="--sampler-delay-ms")samplerDelay=std::clamp(std::stoi(value()),0,10000);
            else throw std::invalid_argument("unknown engine option");
        }
        if(isWorker)return worker(workerDelay);
        if(manifest.empty())if(auto env=std::getenv("HYBRID_MODEL"))manifest=env;
        std::optional<FrozenModel> model;std::string hash="none";
        try {model=FrozenModel::load(manifest);hash=model->model_sha256();}catch(const std::exception&){model.reset();}
        BaselineProcess baseline(executable(argv[0]),workerDelay);SeatMemory memory;std::optional<SeatObservation> observation;
        std::optional<ActionId> cachedAction;std::string cachedPosition,cachedInfo;
        SearchHooks hooks;
        if(samplerDelay)hooks.sample=[&](const SeatObservation& o,const std::optional<std::array<float,96>>& logits,Rng& rng,Deadline end)->SampleResult {
            auto until=std::min(end,Clock::now()+std::chrono::milliseconds(samplerDelay));
            while(Clock::now()<until)std::this_thread::sleep_for(std::chrono::milliseconds(1));
            return BeliefSampler{}.sample(o,logits,rng,end);
        };
        RootSearch search(hooks);std::string line;
        while(std::getline(std::cin,line)) {
            if(!line.empty() && line.back()=='\r')line.pop_back();std::istringstream input(line);std::string command;input>>command;command=lower(command);
            try {
                if(command.empty())continue;
                if(command=="quit")break;
                if(command=="beuci") {output("id name \"Hybrid Bot\"");output("id author \"Bid Euchre Project\"");output("protocol bideuchre 1");output("info backend="+std::string(model?"hybrid":"baseline")+" model_sha256="+hash);output("beuciok");}
                else if(command=="isready")output("readyok");
                else if(command=="newgame"){memory.new_game();baseline.reset();observation.reset();cachedAction.reset();cachedPosition.clear();}
                else if(command=="position") {
                    observation.reset();std::string payload,extra;input>>payload;if(payload.empty() || input>>extra)throw std::invalid_argument("invalid position payload");
                    observation=memory.observe(beu::json::parse(beu::json::decode_base64url(payload)));baseline.observe(observation->position);
                } else if(command=="go") {
                    const auto began=Clock::now();
                    if(!observation)throw std::invalid_argument("position required before go");
                    auto& o=*observation;auto positionKey=json_stringify(o.position);
                    if(cachedAction && positionKey==cachedPosition){output(cachedInfo);output(format_action(*cachedAction));continue;}
                    int first=-1;for(int id=0;id<61;++id)if(o.legal[id]){first=id;break;}if(first<0)throw std::invalid_argument("no legal action");
                    ActionId action=static_cast<ActionId>(first);RootStats stats;stats.seed=limits.seed;std::string backend="immediate";
                    bool useBaseline=!model;
                    if(model)try {auto result=search.choose(o,memory,*model,limits);action=result.action;stats=result.stats;backend=stats.completedBalancedPass?"hybrid":"hybrid-policy";}catch(const std::exception&){useBaseline=true;}
                    if(useBaseline) {
                        auto deadline=began+std::chrono::milliseconds(std::max(0,std::min(limits.decisionMs,limits.stopMs)-20));
                        if(auto chosen=baseline.choose(o.position,deadline)){action=*chosen;backend="baseline";}else backend="immediate";
                    }
                    // All work is synchronous; exactly this dispatch owns stdout.
                    std::ostringstream info;info<<"info backend="<<backend<<" model_sha256="<<hash<<" balanced="<<stats.completedBalancedPass<<" simulations="<<stats.simulations<<" temperature="<<stats.temperature<<" seed="<<stats.seed<<" counts=";
                    for(int id=0;id<61;++id)if(o.legal[id])info<<id<<':'<<stats.counts[id]<<',';
                    info<<" fallback_reason="<<(backend=="immediate"?baseline.fallback_reason():"none");
                    info<<" elapsed_ms="<<std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now()-began).count();
                    memory.record_own_action(action);baseline.record_own_action(action);cachedAction=action;cachedPosition=positionKey;cachedInfo=info.str();output(cachedInfo);output(format_action(action));
                } else if(command=="stop" || command=="setoption") { /* synchronous bounded search; no late second answer */ }
                else output("error unknown command");
            }catch(const std::exception&) {output("error invalid position or command");}
        }
        return 0;
    }catch(const std::exception& e){std::cerr<<"Hybrid Bot: "<<e.what()<<'\n';return 2;}
}
