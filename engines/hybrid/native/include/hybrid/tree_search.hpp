#pragma once
#include "hybrid/search.hpp"
namespace hybrid {
using ObserverKey=std::string;
ObserverKey make_observer_key(const SeatObservation&);
// An edge is eligible only when currently available. Values are observer-team wins.
struct InformationNode {
 std::array<int,61> visits{},availability{};std::array<double,61> total{};
 void observe(const ActionMask&);
 void update(ActionId,double);
 double mean(ActionId) const;
 ActionId select(const ActionMask&,const std::array<float,61>&) const;
};
class InformationTreeSearch {
public:
 SearchResult choose(const SeatObservation&,const SeatMemory&,const FrozenModel&,SearchLimits) const;
 // Only the querying observer's information enters this nonrecursive search.
 SearchResult query(const SeatObservation&,const FrozenModel&,SearchLimits,Deadline) const;
};
}
