#pragma once
#include "hybrid/contracts.hpp"
#include <optional>
#include <string>
#include <vector>
namespace hybrid {
// Owns one supervised same-executable worker. No shell or shared private state.
class BaselineProcess {
public:
    explicit BaselineProcess(std::string executable,int delayMs=0);
    ~BaselineProcess();
    BaselineProcess(const BaselineProcess&)=delete;
    BaselineProcess& operator=(const BaselineProcess&)=delete;
    void observe(const Json& privatePosition);
    [[nodiscard]] std::optional<ActionId> choose(const Json& privatePosition,Deadline);
    void reset();
    void terminate();
    [[nodiscard]] bool running() const {return pid_>0;}
private:
    bool start(Deadline);
    bool write_line(const std::string&,Deadline);
    std::optional<std::string> read_line(Deadline);
    std::string executable_,buffer_;
    std::vector<std::string> history_;
    int delayMs_=0,pid_=-1,input_=-1,output_=-1;
};
}
