#include "hybrid/generation.hpp"
#include "hybrid/json_io.hpp"
#include <iostream>
struct Stream:hybrid::RowSink {
    void emit(const hybrid::Json& row) override {std::cout<<hybrid::json_stringify(row)<<'\n';std::cout.flush();if(!std::cout)throw std::runtime_error("stream write failed");}
};
int main(){try{std::string input;char c;while(std::cin.get(c)&&c!='\n'){if(input.size()>=65536)throw std::invalid_argument("job exceeds 64 KiB");input+=c;}auto job=hybrid::generation_job(beu::json::parse(input));Stream stream;auto result=hybrid::generate(job,stream,std::chrono::steady_clock::now()+std::chrono::seconds(job.durationSeconds));
    std::cerr<<"reported_memory_bytes="<<hybrid::generation_memory_bytes()<<'\n';return result==hybrid::GenerationStatus::Complete?0:result==hybrid::GenerationStatus::Interrupted?2:result==hybrid::GenerationStatus::Capacity?3:1;
}catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
