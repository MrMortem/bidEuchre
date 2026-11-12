#pragma once
#include "hybrid/model.hpp"
#include "hybrid/observation.hpp"
namespace hybrid {
class FrozenPolicy {
public:
    explicit FrozenPolicy(const FrozenModel& model):model_(model){}
    [[nodiscard]] std::array<float,61> distribution(const SeatObservation&) const;
private:
    const FrozenModel& model_;
};
}
