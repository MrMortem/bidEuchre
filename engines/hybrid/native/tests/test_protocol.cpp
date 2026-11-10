#include "test_support.hpp"
#include "hybrid/baseline_process.hpp"
#include "hybrid/json_io.hpp"
#include "hybrid/match.hpp"
#include "hybrid/actions.hpp"
#include "bot.hpp"
#include <filesystem>
#include <spawn.h>
#include <fcntl.h>
#include <poll.h>
#include <signal.h>
#include <sys/wait.h>
#include <unistd.h>
#include <iostream>
extern char** environ;
namespace hybrid::test {
namespace {
std::string encode(const std::string& s) { const char* alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";std::string out;unsigned bits=0;int n=0;for(unsigned char c:s) {bits=(bits<<8)|c;n+=8;while(n>=6) {n-=6;out+=alphabet[(bits>>n)&63];}}if(n)out+=alphabet[(bits<<(6-n))&63];return out; }
struct Child {
    pid_t pid=-1;int input=-1,output=-1;std::string buffered;
    explicit Child(std::vector<std::string> options) {
        int in[2],out[2];CHECK("pipes",pipe(in)==0 && pipe(out)==0);
        for(int fd:{in[0],in[1],out[0],out[1]})fcntl(fd,F_SETFD,FD_CLOEXEC);
        posix_spawn_file_actions_t actions;posix_spawn_file_actions_init(&actions);
        posix_spawn_file_actions_adddup2(&actions,in[0],0);posix_spawn_file_actions_adddup2(&actions,out[1],1);
        for(int fd:{in[0],in[1],out[0],out[1]})posix_spawn_file_actions_addclose(&actions,fd);
        options.insert(options.begin(),HYBRID_ENGINE_PATH);std::vector<char*> argv;for(auto& s:options)argv.push_back(s.data());argv.push_back(nullptr);
        int err=posix_spawn(&pid,argv[0],&actions,nullptr,argv.data(),environ);posix_spawn_file_actions_destroy(&actions);close(in[0]);close(out[1]);input=in[1];output=out[0];CHECK("spawn engine",err==0);
    }
    ~Child(){close(input);close(output);kill(pid,SIGKILL);waitpid(pid,nullptr,0);}
    void send(const std::string& s){auto text=s+"\n";CHECK("send",write(input,text.data(),text.size())==static_cast<ssize_t>(text.size()));}
    std::string until(const std::string& suffix,int timeout=4500) {
        auto end=std::chrono::steady_clock::now()+std::chrono::milliseconds(timeout);std::string all;
        while(std::chrono::steady_clock::now()<end) {
            auto nl=buffered.find('\n');if(nl!=std::string::npos){auto line=buffered.substr(0,nl);buffered.erase(0,nl+1);all+=line+"\n";if(line.starts_with(suffix))return all;continue;}
            pollfd fd{output,POLLIN,0};if(poll(&fd,1,10)>0){char data[4096];auto n=read(output,data,sizeof(data));if(n<=0)break;buffered.append(data,n);}
        }
        throw std::runtime_error("engine timeout waiting for "+suffix+": "+all);
    }
};
}
void test_protocol() {
    auto f=load_case("high");NativeMatch m;m.start(deal_from_json(f.at("initial")),0,{0,0});auto position=m.view(1);auto payload=encode(json_stringify(position));
    for(const auto& model:std::vector<std::string>{HYBRID_TEST_MODEL,"/missing/model.json"}) {
        Child c({"--model",model,"--max-simulations","7","--decision-ms","150","--stop-ms","200"});
        auto started=std::chrono::steady_clock::now();c.send("BeUcI\r");auto greeting=c.until("beuciok");CHECK("identity",greeting.find("Hybrid Bot")!=std::string::npos);
        c.send("ISREADY\r");c.until("readyok");CHECK("startup ready",std::chrono::steady_clock::now()-started<std::chrono::seconds(5));
        c.send("newgame");c.send("position !");c.send("go");c.until("error");
        c.send("position "+payload);c.send("go");c.send("stop");c.send("isready");auto answer=c.until("readyok");
        CHECK("one answer per go",answer.find("bestaction ")!=std::string::npos && answer.find("bestaction ",answer.find("bestaction ")+1)==std::string::npos);
        CHECK("backend diagnostic",answer.find("backend=")!=std::string::npos);
        if(model==HYBRID_TEST_MODEL)CHECK("successful real hybrid search identified",answer.find("backend=hybrid ")!=std::string::npos && answer.find("balanced=1")!=std::string::npos && answer.find("model_sha256=none")==std::string::npos);
        c.send("go");auto repeated=c.until("bestaction ");CHECK("repeat go has answer",repeated.find("bestaction ")!=std::string::npos);
        c.send("position "+payload);c.send("go");c.until("bestaction ");
        c.send("quit");
    }
    BaselineProcess ordinary(HYBRID_ENGINE_PATH);CHECK("lazy baseline construction",!ordinary.running());
    beu::HeuristicBot reference;reference.set_position(beu::parse_position(position));auto expected=parse_action(reference.choose_action());
    auto baselineAction=ordinary.choose(position,std::chrono::steady_clock::now()+std::chrono::seconds(3));
    CHECK("unchanged baseline action",baselineAction && *baselineAction==expected);ordinary.reset();CHECK("reset reaps baseline",!ordinary.running());
    // A corrupt artifact and finite weights overflowing during inference both
    // route to the unchanged worker and identify the effective backend.
    auto corrupt=std::filesystem::path(HYBRID_TEST_MODEL).parent_path()/"corrupt.json";{std::ofstream file(corrupt);file<<"{}";}
    for(const auto& path:std::vector<std::string>{corrupt.string(),(std::filesystem::path(HYBRID_TEST_MODEL).parent_path().parent_path()/"overflow-model/manifest.json").string()}) {
        Child fallback({"--model",path});fallback.send("beuci");auto hello=fallback.until("beuciok");
        fallback.send("position "+payload);fallback.send("go");auto reply=fallback.until("bestaction ");
        CHECK("model failure uses baseline worker",reply.find("backend=baseline ")!=std::string::npos);
        if(path.find("overflow")!=std::string::npos)CHECK("overflow model loaded before fallback",hello.find("backend=hybrid ")!=std::string::npos);
    }
    BaselineProcess child(HYBRID_ENGINE_PATH,10000);auto start=std::chrono::steady_clock::now();
    CHECK("delayed child times out",!child.choose(position,start+std::chrono::milliseconds(80)));
    CHECK("child timeout bounded",std::chrono::steady_clock::now()-start<std::chrono::milliseconds(250));
    CHECK("child killed and reaped",!child.running());
    std::vector<std::unique_ptr<Child>> clients;
    for(int seat=0;seat<4;++seat){clients.push_back(std::make_unique<Child>(std::vector<std::string>{"--model",HYBRID_TEST_MODEL,"--max-simulations","0"}));clients.back()->send("beuci");}
    for(auto& c:clients){c->until("beuciok");c->send("isready");c->until("readyok");}
    Child slow({"--model",HYBRID_TEST_MODEL,"--sampler-delay-ms","10000"});slow.send("beuci");slow.until("beuciok");slow.send("position "+payload);auto begin=std::chrono::steady_clock::now();slow.send("go");auto result=slow.until("bestaction ");
    auto ms=std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now()-begin).count();CHECK("real process stops by3500",ms<=3500);CHECK("slow sampler has no target",result.find("balanced=0")!=std::string::npos);std::cout<<"Slow sampler wall clock "<<ms<<" ms\n";
}
}
