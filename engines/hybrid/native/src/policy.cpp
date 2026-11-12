#include "hybrid/policy.hpp"
#include <algorithm>
namespace hybrid {
std::array<float,61> FrozenPolicy::distribution(const SeatObservation& o) const {
    auto p=normalize_output(model_.evaluate(o.features),o.legal).policy;
    auto count=std::count(o.legal.begin(),o.legal.end(),true);
    if(count) for(int id=0;id<61;++id) if(o.legal[id]) p[id]=.9f*p[id]+.1f/count;
    return p;
}
}
