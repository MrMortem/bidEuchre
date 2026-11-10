#pragma once
#include "hybrid/model.hpp"
#include "hybrid/sampler.hpp"
#include <functional>
namespace hybrid {
struct SearchLimits {int decisionMs=3000;int stopMs=3500;std::optional<int> maxSimulations;std::uint64_t seed=0;};
struct RootStats {
    std::array<double,61> means{};
    std::array<int,61> counts{};
    bool completedBalancedPass=false;
    std::optional<std::array<float,61>> target;
    float temperature=.10f;
    std::uint64_t seed=0;
    // Only completed candidate rollouts in committed atomic batches count.
    int simulations=0;
};
struct SearchResult {ActionId action;RootStats stats;};
// Hooks retain the same private/card-free boundaries as production callbacks.
struct SearchHooks {
    std::function<SampleResult(const SeatObservation&,const std::optional<std::array<float,96>>&,Rng&,Deadline)> sample;
    std::function<std::array<float,61>(const SeatObservation&)> policy;
    std::function<float(const BoundaryFeatures&)> boundary;
};
class RootSearch {
public:
    explicit RootSearch(SearchHooks hooks={}):hooks_(std::move(hooks)){}
    [[nodiscard]] SearchResult choose(const SeatObservation&,const SeatMemory&,const FrozenModel&,SearchLimits) const;
private:
    SearchHooks hooks_;
};
}
