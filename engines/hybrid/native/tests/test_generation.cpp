#include "test_support.hpp"
#include "hybrid/generation.hpp"
#include "hybrid/actions.hpp"
#include "hybrid/json_io.hpp"
#include <cmath>
#include <algorithm>
namespace hybrid::test {
struct Capture:RowSink { std::vector<Json> rows; void emit(const Json& row) override { rows.push_back(row); } };
template<class T> void equal_array(const Json& row,const T& expected){CHECK("array dimension",row.array().size()==expected.size());for(std::size_t i=0;i<expected.size();++i)CHECK("private canonical value",std::abs(row.array()[i].number()-expected[i])<1e-7);}
void audit(const Capture& captured){
    NativeMatch match;std::array<SeatMemory,4> memories;bool first=true,next=false;int boundaries=0,decisions=0,deals=0;
    auto job=generation_job(captured.rows.front().at("job"));auto assigned=generation_seats(job);
    std::vector<beu::HeuristicBot> bots;for(int seat=0;seat<4;++seat){auto opts=job.baseline;opts.seed=job.seed+seat;bots.emplace_back(opts);}
    for(const auto& row:captured.rows){auto type=row.at("type").string();
        if(type=="decision"){
            int seat=row.at("seat").integer();auto owners=row.at("labels").at("ownership_target").array();
            if(first||next){CHECK("all prior boundaries before dealing",first||boundaries==4);Deal d;for(int c=0;c<24;++c){CHECK("new deal complete deck",owners[c].integer()>=0);d.hands[(seat+owners[c].integer())%4]|=1U<<c;}if(first)match.start(d,job.dealer,{0,0});else match.start_next_hand(d);first=next=false;boundaries=0;++deals;}
            auto o=memories[seat].observe(match.view(seat));equal_array(row.at("features"),o.features);
            CHECK("actor decision cadence",beu::parse_position(o.position).current_seat==seat);
            for(int s=0;s<4;++s)CHECK("private other hand absent",s==seat||o.position.at("game").at("players").array()[s].at("cards").is_null());
            auto action=ActionId(row.at("action").integer());CHECK("action legal",o.legal[action]);
            auto full=match.full_state();std::array<int,24> expected;expected.fill(-1);for(const auto& p:full.at("players").array())for(const auto& c:p.at("cards").array())expected[c.integer()]=(p.at("seat").integer()-seat+4)%4;equal_array(row.at("labels").at("ownership_target"),expected);
            const auto& member=job.population[assigned[seat]];double probability=row.at("behavior_prob").number();auto metadata=row.at("metadata");
            CHECK("branch joint likelihood",std::abs(metadata.at("joint_branch_action_prob").number()-probability*metadata.at("branch_probability").number())<1e-7);
            if(member.kind=="uniform")CHECK("uniform behavior likelihood",std::abs(probability-1./std::count(o.legal.begin(),o.legal.end(),true))<1e-7);
            if(member.kind=="fixed"||member.kind=="reverse"||member.kind=="uniform")CHECK("simple policy no imitation",row.at("policy_weight").number()==0);
            if(member.kind=="baseline"){bots[seat].set_position(beu::parse_position(o.position));CHECK("unchanged baseline action",parse_action(bots[seat].choose_action())==action);CHECK("baseline exact likelihood",probability==1);}
            if(metadata.at("exploration_selected").boolean())CHECK("exploration not imitated",row.at("policy_weight").number()==0);
            memories[seat].record_own_action(action);match.apply(action);++decisions;
        }else if(type=="hand_end"){
            auto terminal=match.hand_result();equal_array(row.at("tricks"),terminal.tricks);equal_array(row.at("scores"),terminal.scoresAfter);CHECK("hand numbered",row.at("deal").integer()==deals);next=true;
        }else if(type=="boundary"){
            int seat=row.at("seat").integer();auto o=memories[seat].observe(match.view(seat));auto p=beu::parse_position(o.position);bots[seat].set_position(p);CHECK("boundary no next deal",p.phase=="HandComplete"||p.phase=="GameComplete");equal_array(row.at("features"),encode_boundary(match.hand_result().scoresAfter,(p.dealer+1)%4,seat,o.summary));++boundaries;
        }else if(type=="footer"&&row.at("status").string()=="complete"){
            CHECK("actual terminal winner",match.hand_result().winner==row.at("winner").integer());CHECK("actual count",row.at("decisions").integer()==decisions);
        }
    }
}
void test_generation() {
    GenerationJob job; job.jobId="native-test"; job.population={{"incumbent","fixed","v1"},{"varied","uniform","v1"}};
    job.maxDecisions=20000; job.seed=901;
    Capture a,b;
    CHECK("full match complete",generate(job,a,std::chrono::steady_clock::now()+std::chrono::seconds(20))==GenerationStatus::Complete);
    CHECK("repeat complete",generate(job,b,std::chrono::steady_clock::now()+std::chrono::seconds(20))==GenerationStatus::Complete);
    CHECK("seed reproducible",json_stringify(Json(a.rows))==json_stringify(Json(b.rows)));audit(a);
    CHECK("scripted mode declared",a.rows.front().at("job").at("execution_mode").string()=="fixed_seeded");
    CHECK("terminal footer",a.rows.back().at("status").string()=="complete");
    job.scheduleIndex=1;Capture varied;CHECK("varied full match",generate(job,varied,std::chrono::steady_clock::now()+std::chrono::seconds(20))==GenerationStatus::Complete);audit(varied);job.scheduleIndex=0;
    Capture c; job.maxDecisions=1;
    CHECK("bounded interruption",generate(job,c,std::chrono::steady_clock::now()+std::chrono::seconds(20))==GenerationStatus::Interrupted);
    CHECK("no invented winner",c.rows.back().find("winner")==nullptr);CHECK("interrupted row no win",c.rows[1].find("win_target")==nullptr);
    int incumbents=0,matching=0;for(int index=0;index<4;++index){job.scheduleIndex=index;auto seats=generation_seats(job);incumbents+=(seats[1]==job.incumbent)+(seats[3]==job.incumbent);matching+=seats[0]==seats[2]&&seats[1]==seats[3];}
    CHECK("half incumbent opposition",incumbents==4);CHECK("half matching partners",matching==2);
    job.scheduleIndex=0;job.maxDecisions=30;job.population[0].kind="baseline";job.baseline.samples=1;job.baseline.play_time_ms=1;job.baseline.search_depth=1;job.baseline.search_nodes=1;
    Capture baseline;CHECK("baseline bounded real actions",generate(job,baseline,std::chrono::steady_clock::now()+std::chrono::seconds(20))==GenerationStatus::Interrupted);CHECK("baseline actions present",baseline.rows.size()>3);audit(baseline);CHECK("timed baseline declared",baseline.rows.front().at("job").at("execution_mode").string()=="seeded_timed");job.executionMode="fixed_seeded";CHECK("timed baseline rejected by fixed mode",rejects([&]{(void)generation_manifest(job);}));job.executionMode="auto";
    auto modelPath=std::filesystem::path(HYBRID_TEST_MODEL);std::ifstream input(modelPath);std::string manifest((std::istreambuf_iterator<char>(input)),{});auto hash=beu::json::parse(manifest).at("model_sha256").string();
    job.population[0]={"model","model","test",modelPath.string(),hash};job.maxDecisions=1;job.searchFraction=1;job.search.maxSimulations=7;job.exploration=0;
    Capture searched;CHECK("model bounded match",generate(job,searched,std::chrono::steady_clock::now()+std::chrono::seconds(20))==GenerationStatus::Interrupted);
    CHECK("complete search teacher",searched.rows[1].at("metadata").at("teacher_kind").string()=="search");CHECK("balanced K counts",searched.rows[1].at("metadata").at("search").at("simulations").integer()==7);audit(searched);
    job.search.maxSimulations=0;job.exploration=1;Capture explored;(void)generate(job,explored,std::chrono::steady_clock::now()+std::chrono::seconds(20));CHECK("exploration labeled",explored.rows[1].at("metadata").at("exploration_selected").boolean());CHECK("no partial teacher",explored.rows[1].at("policy_weight").number()==0);CHECK("exploration likelihood",std::abs(explored.rows[1].at("behavior_prob").number()-1./7)<1e-7);audit(explored);
    job.exploration=0;job.search.maxSimulations=7;job.search.decisionMs=0;Capture shortFixed;CHECK("fixed count interruption",generate(job,shortFixed,std::chrono::steady_clock::now()+std::chrono::seconds(20))==GenerationStatus::Interrupted);CHECK("incomplete fixed search emits no action",shortFixed.rows.size()==2);
    job.memoryBytes=1048576;Capture capacity;CHECK("capacity explicit",generate(job,capacity,std::chrono::steady_clock::now()+std::chrono::seconds(20))==GenerationStatus::Capacity);
}
}
