#pragma once
#include "hybrid/match.hpp"
#include "hybrid/observation.hpp"
#include <set>
#include <variant>

namespace hybrid {
struct SampledWorld { NativeMatch match; std::array<SeatMemory,4> memories; };
enum class SampleFailure { Inconsistent, Exhausted, Deadline };
using SampleResult=std::variant<SampledWorld,SampleFailure>;
struct BeliefDiagnostics {
    std::uint64_t attempts=0, feasible=0, unique=0, constraint_failures=0;
    std::uint64_t uniform_samples=0, learned_samples=0;
    std::uint64_t counting_nodes=0; // includes memo hits, useful for deadline diagnostics
};
class BeliefSampler {
public:
    SampleResult sample(const SeatObservation&,const std::optional<std::array<float,96>>& logits,Rng&,Deadline);
    [[nodiscard]] const BeliefDiagnostics& diagnostics() const { return diagnostics_; }
    void reset_diagnostics() { diagnostics_={}; unique_.clear(); }
private:
    BeliefDiagnostics diagnostics_;
    // Bounded diagnostic reservoir: unique counts saturate at 4096 per reset.
    std::set<std::array<beu::CardMask,4>> unique_;
};
} // namespace hybrid
