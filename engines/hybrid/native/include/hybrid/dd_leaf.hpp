#pragma once
#include "hybrid/search.hpp"
namespace hybrid {
// Perfect-information estimate for one sampled world, never a hidden-information solution.
class DoubleDummyLeaf {
public:
 explicit DoubleDummyLeaf(const FrozenModel& model,std::function<float(const BoundaryFeatures&)> boundary={},int maximumNodes=180000):model_(model),boundary_(std::move(boundary)),maximumNodes_(maximumNodes){}
 std::optional<float> evaluate(const SampledWorld&,int perspectiveSeat,Deadline) const;
private:
 const FrozenModel& model_;std::function<float(const BoundaryFeatures&)> boundary_;int maximumNodes_;
};
}
