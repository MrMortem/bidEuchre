#pragma once
#include "hybrid/search.hpp"
#include "bot.hpp"
#include <string>
namespace hybrid {
struct PopulationMember {
    std::string id, kind, version;
    std::string modelPath, modelSha256;
    bool previousChampion=false;
    std::string modelManifestSha256;
};
struct GenerationJob {
    std::string jobId, split="train", executionMode="auto";
    std::vector<PopulationMember> population;
    std::size_t incumbent=0, learner=0;
    std::uint64_t seed=0, scheduleIndex=0;
    int dealer=0, maxHands=1000, maxDecisions=100000, durationSeconds=1800;
    std::uint64_t memoryBytes=2ULL<<30, diskBytes=5ULL<<30;
    int workers=2, trainingProcesses=1;
    std::string device="cpu";
    double exploration=.1, searchFraction=.25, playSearchFraction=.05;
    SearchLimits search;
    beu::BotOptions baseline;
};
enum class GenerationStatus { Complete, Interrupted, Failed, Capacity };
struct RowSink { virtual ~RowSink()=default; virtual void emit(const Json&)=0; };
[[nodiscard]] GenerationJob generation_job(const Json&);
[[nodiscard]] Json generation_manifest(const GenerationJob&);
[[nodiscard]] std::array<std::size_t,4> generation_seats(const GenerationJob&);
[[nodiscard]] std::uint64_t generation_memory_bytes();
[[nodiscard]] GenerationStatus generate(const GenerationJob&,RowSink&,Deadline);
}
