#include "hybrid/generation.hpp"
#include "hybrid/actions.hpp"
#include "hybrid/json_io.hpp"
#include <algorithm>
#include <cmath>
#include <fstream>
#include <numeric>
#include <set>
#include <sys/resource.h>
namespace hybrid {
namespace {
Json n(double v){return Json(v);} Json s(std::string v){return Json(std::move(v));}
template<class T> Json array(const T& values){Json::Array a;for(auto v:values) {if constexpr(std::is_same_v<decltype(v),bool>)a.emplace_back(v);else a.emplace_back(double(v));}return Json(a);}
std::uint64_t natural(const Json& j){double v=j.number();if(!std::isfinite(v)||v<0||v>9007199254740991.||std::floor(v)!=v)throw std::invalid_argument("invalid natural number");return std::uint64_t(v);}
std::string execution_mode(const GenerationJob& j){
    bool timed=false;for(const auto& member:j.population)timed|=member.kind=="baseline"||(member.kind=="model"&&(j.searchFraction>0||j.playSearchFraction>0)&&!j.search.maxSimulations);
    if(j.executionMode!="auto"&&j.executionMode!="fixed_seeded"&&j.executionMode!="seeded_timed")throw std::invalid_argument("unknown execution mode");
    if(j.executionMode=="fixed_seeded"&&timed)throw std::invalid_argument("fixed_seeded excludes timed baseline/search");
    return j.executionMode=="auto"?(timed?"seeded_timed":"fixed_seeded"):j.executionMode;
}
void validate(const GenerationJob& j){
    (void)execution_mode(j);
    if(j.jobId.empty()||j.jobId.size()>128||(j.split!="train"&&j.split!="validation"&&j.split!="test")||j.population.size()<2||j.population.size()>16||j.incumbent>=j.population.size()||j.learner>=j.population.size())throw std::invalid_argument("invalid job identity/population");
    if(j.seed>9007199254740991ULL||j.scheduleIndex>9007199254740991ULL)throw std::invalid_argument("seed/index exceed exact JSON range");
    if(j.dealer<0||j.dealer>3||j.maxHands<1||j.maxHands>100000||j.maxDecisions<1||j.maxDecisions>10000000||j.durationSeconds<1||j.durationSeconds>86400||j.memoryBytes<1048576||j.diskBytes<1024||j.workers<1||j.workers>2||j.trainingProcesses<0||j.trainingProcesses>1||j.device!="cpu")throw std::invalid_argument("invalid resource limits");
    for(auto p:{j.exploration,j.searchFraction,j.playSearchFraction})if(!std::isfinite(p)||p<0||p>1)throw std::invalid_argument("invalid probability");
    if(j.search.decisionMs<0||j.search.stopMs<0||(j.search.maxSimulations&&*j.search.maxSimulations<0)||j.baseline.samples<1||j.baseline.play_time_ms<1||j.baseline.search_depth<1||j.baseline.search_nodes<1)throw std::invalid_argument("invalid policy limits");
    std::set<std::string> ids;int champions=0;
    for(const auto& p:j.population){if(p.id.empty()||p.version.empty()||!ids.insert(p.id).second||(p.kind!="baseline"&&p.kind!="fixed"&&p.kind!="reverse"&&p.kind!="uniform"&&p.kind!="model"))throw std::invalid_argument("invalid member");if(p.previousChampion)++champions;if(p.kind=="model"&&(p.modelPath.empty()||p.modelSha256.size()!=64))throw std::invalid_argument("model hash required");}
    if(champions>4)throw std::invalid_argument("at most four previous champions");
}
Deal deal(Rng& rng){std::array<int,24> deck;std::iota(deck.begin(),deck.end(),0);for(int i=23;i>0;--i)std::swap(deck[i],deck[rng()%(i+1)]);Deal d;for(int i=0;i<24;++i)d.hands[i/6]|=1U<<deck[i];return d;}
ActionId draw(const std::array<double,61>& p,Rng& rng){double x=std::generate_canonical<double,53>(rng);int last=0;for(int i=0;i<61;++i)if(p[i]>0){last=i;x-=p[i];if(x<0)return ActionId(i);}return ActionId(last);}
}
std::uint64_t generation_memory_bytes(){rusage r{};getrusage(RUSAGE_SELF,&r);
#ifdef __APPLE__
return r.ru_maxrss;
#else
return std::uint64_t(r.ru_maxrss)*1024;
#endif
}
std::array<std::size_t,4> generation_seats(const GenerationJob& j){validate(j);std::vector<std::size_t> other;for(std::size_t i=0;i<j.population.size();++i)if(i!=j.incumbent)other.push_back(i);
    // Two-match blocks: half incumbent opposition, half rotating frozen pool.
    auto opponent=j.scheduleIndex%2==0||other.empty()?j.incumbent:other[(j.scheduleIndex/2)%other.size()];
    std::array<std::size_t,4> seats{j.learner,opponent,j.learner,opponent};
    if((j.scheduleIndex/2)%2){seats[2]=(j.learner+1+(j.scheduleIndex/4)%std::max<std::size_t>(1,j.population.size()-1))%j.population.size();seats[3]=j.scheduleIndex%2==0&&!other.empty()?other[(j.scheduleIndex/2)%other.size()]:j.incumbent;}
    return seats;
}
Json generation_manifest(const GenerationJob& j){validate(j);Json::Array population;for(const auto& p:j.population)population.emplace_back(Json::Object{{"id",s(p.id)},{"kind",s(p.kind)},{"version",s(p.version)},{"model_path",s(p.modelPath)},{"model_sha256",s(p.modelSha256)},{"previous_champion",Json(p.previousChampion)},{"model_manifest_sha256",s(p.modelManifestSha256)}});
    return Json(Json::Object{{"job_id",s(j.jobId)},{"split",s(j.split)},{"execution_mode",s(execution_mode(j))},{"population",Json(population)},{"incumbent",n(j.incumbent)},{"learner",n(j.learner)},{"seed",n(j.seed)},{"schedule_index",n(j.scheduleIndex)},{"seats",array(generation_seats(j))},{"dealer",n(j.dealer)},{"max_hands",n(j.maxHands)},{"max_decisions",n(j.maxDecisions)},{"duration_seconds",n(j.durationSeconds)},{"memory_bytes",n(j.memoryBytes)},{"disk_bytes",n(j.diskBytes)},{"workers",n(j.workers)},{"training_processes",n(j.trainingProcesses)},{"device",s(j.device)},{"exploration",n(j.exploration)},{"search_fraction",n(j.searchFraction)},{"play_search_fraction",n(j.playSearchFraction)},{"decision_ms",n(j.search.decisionMs)},{"stop_ms",n(j.search.stopMs)},{"max_simulations",j.search.maxSimulations?n(*j.search.maxSimulations):Json()},{"search_temperature",n(.1)},{"baseline_samples",n(j.baseline.samples)},{"baseline_play_ms",n(j.baseline.play_time_ms)},{"baseline_depth",n(j.baseline.search_depth)},{"baseline_nodes",n(j.baseline.search_nodes)}});
}
GenerationJob generation_job(const Json& v){GenerationJob j;j.jobId=v.at("job_id").string();
    auto str=[&](const char* k,std::string& x){if(auto p=v.find(k))x=p->string();};auto integer=[&](const char* k,int& x){if(auto p=v.find(k))x=p->integer();};auto real=[&](const char* k,double& x){if(auto p=v.find(k))x=p->number();};
    str("split",j.split);str("execution_mode",j.executionMode);str("device",j.device);integer("dealer",j.dealer);integer("max_hands",j.maxHands);integer("max_decisions",j.maxDecisions);integer("duration_seconds",j.durationSeconds);integer("workers",j.workers);integer("training_processes",j.trainingProcesses);integer("decision_ms",j.search.decisionMs);integer("stop_ms",j.search.stopMs);integer("baseline_samples",j.baseline.samples);integer("baseline_play_ms",j.baseline.play_time_ms);integer("baseline_depth",j.baseline.search_depth);integer("baseline_nodes",j.baseline.search_nodes);
    real("exploration",j.exploration);real("search_fraction",j.searchFraction);real("play_search_fraction",j.playSearchFraction);
    if(auto p=v.find("seed"))j.seed=natural(*p);if(auto p=v.find("schedule_index"))j.scheduleIndex=natural(*p);if(auto p=v.find("memory_bytes"))j.memoryBytes=natural(*p);if(auto p=v.find("disk_bytes"))j.diskBytes=natural(*p);if(auto p=v.find("incumbent"))j.incumbent=natural(*p);if(auto p=v.find("learner"))j.learner=natural(*p);if(auto p=v.find("max_simulations");p&&!p->is_null())j.search.maxSimulations=p->integer();
    for(const auto& p:v.at("population").array()){PopulationMember m;m.id=p.at("id").string();m.kind=p.at("kind").string();m.version=p.at("version").string();if(auto q=p.find("model_path"))m.modelPath=q->string();if(auto q=p.find("model_sha256"))m.modelSha256=q->string();if(auto q=p.find("previous_champion"))m.previousChampion=q->boolean();if(auto q=p.find("model_manifest_sha256"))m.modelManifestSha256=q->string();j.population.push_back(m);}validate(j);return j;
}
GenerationStatus generate(const GenerationJob& j,RowSink& sink,Deadline deadline){
    std::size_t emitted=0;int decisions=0,hands=0;bool header=false;
    auto emit=[&](Json row){auto o=row.object();o["version"]=n(1);auto size=json_stringify(Json(o)).size()+1;if(o.at("type").string()!="footer"&&emitted+size+1024>j.diskBytes)throw std::overflow_error("stream capacity");sink.emit(Json(o));emitted+=size;};
    auto finish=[&](GenerationStatus st,std::string reason,std::optional<int> winner={}){Json::Object o{{"type",s("footer")},{"job_id",s(j.jobId)},{"status",s(st==GenerationStatus::Complete?"complete":st==GenerationStatus::Interrupted?"interrupted":st==GenerationStatus::Capacity?"capacity":"failed")},{"decisions",n(decisions)},{"hands",n(hands)},{"reason",s(reason)}};if(winner)o["winner"]=n(*winner);emit(Json(o));return st;};
    try {
        validate(j);deadline=std::min(deadline,std::chrono::steady_clock::now()+std::chrono::seconds(j.durationSeconds));
        emit(Json(Json::Object{{"type",s("header")},{"schema_sha256",s(std::string(schema::schema_sha256))},{"rule_sha256",s(std::string(schema::rule_sha256))},{"job",generation_manifest(j)}}));header=true;
        auto seats=generation_seats(j);std::vector<std::optional<FrozenModel>> models(j.population.size());
        for(std::size_t i=0;i<models.size();++i)if(j.population[i].kind=="model"){
            models[i]=FrozenModel::load(j.population[i].modelPath,j.population[i].modelSha256,j.population[i].modelManifestSha256);
        }
        std::array<SeatMemory,4> memories;std::vector<beu::HeuristicBot> bots;for(int seat=0;seat<4;++seat){auto opts=j.baseline;opts.seed=j.seed+seat;bots.emplace_back(opts);}
        Rng rng(j.seed);NativeMatch match;match.start(deal(rng),j.dealer,{0,0});
        while(true){
            if(generation_memory_bytes()>j.memoryBytes||emitted>j.diskBytes)return finish(GenerationStatus::Capacity,"memory or stream limit");
            auto publicPosition=beu::parse_position(match.view(0));
            if(publicPosition.phase=="HandComplete"||publicPosition.phase=="GameComplete"){
                auto result=match.hand_result();++hands;
                emit(Json(Json::Object{{"type",s("hand_end")},{"deal",n(hands)},{"tricks",array(result.tricks)},{"scores",array(result.scoresAfter)}}));
                for(int seat=0;seat<4;++seat){auto final=memories[seat].observe(match.view(seat));bots[seat].set_position(beu::parse_position(final.position));auto boundary=encode_boundary(result.scoresAfter,(publicPosition.dealer+1)%4,seat,final.summary);emit(Json(Json::Object{{"type",s("boundary")},{"deal",n(hands)},{"seat",n(seat)},{"features",array(boundary)}}));}
                if(result.winner)return finish(GenerationStatus::Complete,"",result.winner);
                if(hands>=j.maxHands)return finish(GenerationStatus::Interrupted,"hand limit");
                match.start_next_hand(deal(rng));continue;
            }
            if(std::chrono::steady_clock::now()>=deadline||decisions>=j.maxDecisions)return finish(GenerationStatus::Interrupted,"decision or duration limit");
            int seat=publicPosition.current_seat;auto o=memories[seat].observe(match.view(seat));auto p=beu::parse_position(o.position);const auto& member=j.population[seats[seat]];
            std::vector<int> legal;for(int id=0;id<61;++id)if(o.legal[id])legal.push_back(id);
            std::array<double,61> probability{};std::array<float,61> target{};double weight=0,epsilon=0,gate=0,branchProbability=1;std::string teacher="none",behavior=member.kind;Json searchStats;
            if(member.kind=="baseline") {bots[seat].set_position(p);auto action=parse_action(bots[seat].choose_action());if(!o.legal[action])throw std::runtime_error("baseline illegal action");probability[action]=1;target[action]=1;weight=1;teacher="baseline";}
            else if(member.kind=="model"){
                auto& model=*models[seats[seat]];auto raw=normalize_output(model.evaluate(o.features),o.legal).policy;for(int id:legal)probability[id]=raw[id];epsilon=j.exploration;
                gate=legal.size()>1?(p.phase=="Playing"?j.playSearchFraction:j.searchFraction):0;
                bool selected=std::generate_canonical<double,53>(rng)<gate;branchProbability=selected?gate:1-gate;
                if(selected){
                    auto limits=j.search;limits.seed=rng();auto remaining=std::chrono::duration_cast<std::chrono::milliseconds>(deadline-std::chrono::steady_clock::now()).count();limits.decisionMs=std::max<long long>(0,std::min<long long>(limits.decisionMs,remaining));limits.stopMs=std::max<long long>(0,std::min<long long>(limits.stopMs,remaining));
                    auto result=RootSearch{}.choose(o,memories[seat],model,limits);
                    if(execution_mode(j)=="fixed_seeded"){
                        int k=static_cast<int>(legal.size()),cap=limits.maxSimulations.value_or(0);int expected=cap<k?0:k+2*((cap-k)/2);
                        if(result.stats.simulations!=expected)return finish(GenerationStatus::Interrupted,"fixed simulation budget did not complete");
                    }
                    probability.fill(0);probability[result.action]=1;behavior="search_fallback";
                    if(result.stats.completedBalancedPass&&result.stats.target){target=*result.stats.target;weight=1;teacher="search";behavior="search";for(int id:legal)probability[id]=target[id];}
                    searchStats=Json(Json::Object{{"counts",array(result.stats.counts)},{"means",array(result.stats.means)},{"simulations",n(result.stats.simulations)},{"balanced",Json(result.stats.completedBalancedPass)},{"temperature",n(result.stats.temperature)},{"seed",s(std::to_string(limits.seed))}});
                }
            }else if(member.kind=="uniform")for(int id:legal)probability[id]=1.0/legal.size();
            else {int id=member.kind=="reverse"?legal.back():legal.front();probability[id]=1;}
            auto baseProbability=probability;double total=0;for(int id:legal)total+=baseProbability[id];
            if(!std::isfinite(total)||total<=0)throw std::runtime_error("invalid behavior distribution");
            for(int id:legal){baseProbability[id]/=total;probability[id]=(1-epsilon)*baseProbability[id]+epsilon/legal.size();}
            const auto policyBranch=behavior;
            bool explored=epsilon>0&&std::generate_canonical<double,53>(rng)<epsilon;
            auto sampling=baseProbability;if(explored)for(int id:legal)sampling[id]=1.0/legal.size();
            auto action=draw(sampling,rng);
            if(explored){target.fill(0);weight=0;teacher="none";behavior="exploration";}
            // Labels are extracted through a separate privileged path, after policy evaluation.
            std::array<int,24> ownership;ownership.fill(-1);std::array<bool,24> ownershipMask{};
            auto full=match.full_state();for(const auto& player:full.at("players").array())for(const auto& card:player.at("cards").array()){int c=card.integer();ownership[c]=(player.at("seat").integer()-seat+4)%4;ownershipMask[c]=true;}
            Json row(Json::Object{{"type",s("decision")},{"decision",n(decisions)},{"deal",n(hands+1)},{"seat",n(seat)},{"features",array(o.features)},{"legal",array(o.legal)},{"action",n(action)},{"behavior_prob",n(probability[action])},{"policy_target",array(target)},{"policy_weight",n(weight)},{"labels",Json(Json::Object{{"ownership_target",array(ownership)},{"ownership_mask",array(ownershipMask)}})},{"metadata",Json(Json::Object{{"behavior_kind",s(behavior)},{"policy_branch",s(policyBranch)},{"teacher_kind",s(teacher)},{"policy_id",s(member.id)},{"policy_version",s(member.version)},{"exploration",n(epsilon)},{"exploration_selected",Json(explored)},{"search_gate_probability",n(gate)},{"branch_probability",n(branchProbability)},{"joint_branch_action_prob",n(branchProbability*probability[action])},{"probability_conditioning",s("executed_teacher_branch")},{"search",searchStats}})}});
            emit(row);memories[seat].record_own_action(action);match.apply(action);++decisions;
        }
    }catch(const std::overflow_error& e){if(header){try{return finish(GenerationStatus::Capacity,e.what());}catch(...){}}return GenerationStatus::Capacity;
    }catch(const std::exception& e){if(header){try{return finish(GenerationStatus::Failed,e.what());}catch(...){}}return GenerationStatus::Failed;}
}
}
