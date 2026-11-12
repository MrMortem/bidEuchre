#include "hybrid/baseline_process.hpp"
#include "hybrid/actions.hpp"
#include "hybrid/observation.hpp"
#include "hybrid/json_io.hpp"
#include <algorithm>
#include <cerrno>
#include <fcntl.h>
#include <poll.h>
#include <signal.h>
#include <spawn.h>
#include <sys/wait.h>
#include <unistd.h>
extern char** environ;
namespace hybrid {
namespace {
using Clock=std::chrono::steady_clock;
bool ready(int fd,short events,Deadline end) {
    while(Clock::now()<end) {
        auto ms=std::chrono::duration_cast<std::chrono::milliseconds>(end-Clock::now()).count();
        pollfd p{fd,events,0};int n=poll(&p,1,static_cast<int>(std::clamp<long long>(ms,0,20)));
        if(n>0)return p.revents&events;if(n<0 && errno!=EINTR)return false;
    }return false;
}
}
BaselineProcess::BaselineProcess(std::string path,int delay):executable_(std::move(path)),delayMs_(delay){}
BaselineProcess::~BaselineProcess(){terminate();}
void BaselineProcess::terminate() {
    if(input_>=0)close(input_);if(output_>=0)close(output_);input_=output_=-1;
    if(pid_>0){kill(pid_,SIGKILL);while(waitpid(pid_,nullptr,0)<0 && errno==EINTR){}}pid_=-1;buffer_.clear();delivered_=0;
}
void BaselineProcess::reset(){terminate();history_.clear();dealKey_.clear();quarantined_=false;reason_="none";}
void BaselineProcess::observe(const Json& position) {
    // Sanitize again at the process boundary; never serialize an arbitrary view.
    auto clean=SeatMemory{}.observe(position);auto line=json_stringify(clean.position);
    const auto& game=clean.position.at("game");
    const auto* serial=game.find("dealSerial");
    auto key=serial && !serial->is_null()?"serial:"+json_stringify(*serial):"hand:"+json_stringify(game.at("handNumber"));
    bool redeal=false;
    if(!history_.empty() && game.at("phase").string()=="Bidding" && game.at("auction").array().empty()) {
        auto previous=beu::parse_position(beu::json::parse(history_.back().position));auto current=beu::parse_position(clean.position);
        redeal=previous.phase!="Bidding" || !previous.auction.empty() || previous.hand!=current.hand;
    }
    if((!dealKey_.empty() && key!=dealKey_) || redeal)reset();
    dealKey_=key;
    if(history_.empty() || history_.back().position!=line) {
        if(history_.size()==64){terminate();quarantined_=true;reason_="history-limit";return;}
        history_.push_back({std::move(line),game.at("phase").string()=="ExchangingBidderCard",{}});
    }
}
void BaselineProcess::record_own_action(ActionId action) {
    if(quarantined_)return;
    if(action>=61 || history_.empty() || !legal_mask(beu::json::parse(history_.back().position))[action])throw std::invalid_argument("invalid recorded baseline action");
    auto& recorded=history_.back().action;
    if(recorded && *recorded!=action){terminate();quarantined_=true;reason_="actual-action-mismatch";return;}
    recorded=action;
}
bool BaselineProcess::write_line(const std::string& line,Deadline end) {
    auto data=line+"\n";std::size_t offset=0;
    while(offset<data.size() && ready(input_,POLLOUT,end)) {
        auto n=write(input_,data.data()+offset,data.size()-offset);
        if(n>0)offset+=n;else if(n<0 && errno!=EAGAIN && errno!=EINTR)return false;
    }return offset==data.size();
}
std::optional<std::string> BaselineProcess::read_line(Deadline end) {
    while(Clock::now()<end) {
        auto nl=buffer_.find('\n');if(nl!=std::string::npos){auto line=buffer_.substr(0,nl);buffer_.erase(0,nl+1);return line;}
        if(!ready(output_,POLLIN,end))break;
        char data[4096];auto n=read(output_,data,sizeof(data));if(n<=0)break;buffer_.append(data,n);if(buffer_.size()>8192)break;
    }return {};
}
bool BaselineProcess::start(Deadline end) {
    if(Clock::now()>=end)return false;
    int in[2],out[2];if(pipe(in)!=0)return false;if(pipe(out)!=0){close(in[0]);close(in[1]);return false;}
    for(int fd:{in[0],in[1],out[0],out[1]})fcntl(fd,F_SETFD,FD_CLOEXEC);
    posix_spawn_file_actions_t actions;posix_spawn_file_actions_init(&actions);
    posix_spawn_file_actions_adddup2(&actions,in[0],STDIN_FILENO);posix_spawn_file_actions_adddup2(&actions,out[1],STDOUT_FILENO);
    posix_spawn_file_actions_addopen(&actions,STDERR_FILENO,"/dev/null",O_WRONLY,0);
    for(int fd:{in[0],in[1],out[0],out[1]})posix_spawn_file_actions_addclose(&actions,fd);
    std::string delay=std::to_string(delayMs_);
    std::vector<char*> args{executable_.data(),const_cast<char*>("--baseline-worker"),const_cast<char*>("--worker-delay-ms"),delay.data(),nullptr};
    pid_t child=-1;int error=posix_spawn(&child,executable_.c_str(),&actions,nullptr,args.data(),environ);
    posix_spawn_file_actions_destroy(&actions);close(in[0]);close(out[1]);
    if(error){close(in[1]);close(out[0]);return false;}
    pid_=child;input_=in[1];output_=out[0];fcntl(input_,F_SETFL,O_NONBLOCK);fcntl(output_,F_SETFL,O_NONBLOCK);
    auto greeting=read_line(end);return greeting && *greeting=="workerready";
}
std::optional<ActionId> BaselineProcess::choose(const Json& position,Deadline end) {
    bool restoring=false;
    try {
        observe(position);
        if(quarantined_)return {};
        reason_="none";
        if(pid_<0 && !start(end)){terminate();reason_="worker-start-failed";return {};}
        // Preserve a healthy worker. On a restart/lazy start, restore only the
        // historical choose calls that mutate baseline private exchange recall.
        // Their results must equal the actual action, including learned choices.
        while(delivered_+1<history_.size()) {
            const auto& entry=history_[delivered_];
            restoring=true;
            if(!write_line("position "+entry.position,end))throw std::runtime_error("restore write");
            if(entry.bidderExchange && entry.action) {
                if(!write_line("go",end))throw std::runtime_error("restore choose");
                auto reply=read_line(end);
                if(!reply)throw std::runtime_error("restore response");
                if(parse_action(*reply)!=*entry.action){terminate();quarantined_=true;reason_="restore-action-mismatch";return {};}
            }
            ++delivered_;
        }
        restoring=false;
        if(delivered_==history_.size() && history_.back().action)return history_.back().action;
        if(!write_line("position "+history_.back().position,end) || !write_line("go",end))throw std::runtime_error("worker write");
        auto line=read_line(end);
        if(line) {
            auto action=parse_action(*line);
            if(legal_mask(position)[action]){record_own_action(action);delivered_=history_.size();return action;}
        }
    }catch(const std::exception&){}
    terminate();
    if(restoring){quarantined_=true;reason_="restore-failed";}else reason_="worker-failed";
    return {};
}
}
