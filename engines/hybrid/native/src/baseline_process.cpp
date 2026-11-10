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
    if(pid_>0){kill(pid_,SIGKILL);while(waitpid(pid_,nullptr,0)<0 && errno==EINTR){}}pid_=-1;buffer_.clear();
}
void BaselineProcess::reset(){terminate();history_.clear();}
void BaselineProcess::observe(const Json& position) {
    // Sanitize again at the process boundary; never serialize an arbitrary view.
    auto clean=SeatMemory{}.observe(position);auto line=json_stringify(clean.position);
    if(history_.empty() || history_.back()!=line)history_.push_back(std::move(line));
    // Baseline recall is per hand; 64 own observations cover a complete deal.
    if(history_.size()>64)history_.erase(history_.begin());
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
    try {
        observe(position);
        // Replaying the bounded own-view history restores baseline recall after
        // a killed worker as well as a lazy first start; no other seat is sent.
        if(pid_<0 && !start(end)){terminate();return {};}
        if(!write_line("newgame",end)){terminate();return {};}
        for(const auto& view:history_)if(!write_line("position "+view,end)){terminate();return {};}
        if(!write_line("go",end)){terminate();return {};}
        auto line=read_line(end);if(line) {auto action=parse_action(*line);if(legal_mask(position)[action])return action;}
    }catch(const std::exception&){}
    terminate();return {};
}
}
