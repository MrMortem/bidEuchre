#include "test_support.hpp"
#include "hybrid/match.hpp"
namespace hybrid::test {
void test_parity() {
    std::ifstream input(HYBRID_FIXTURE_PATH);
    CHECK("authority corpus present", input.good());
    std::string line; int count=0;
    while (std::getline(input,line)) { replay_fixture(beu::json::parse(line)); ++count; }
    CHECK("authority and randomized traces", count>=62);
    auto f=load_case("high").object();
    auto steps=f.at("steps").array(); auto step=steps[0].object();
    auto expected=step.at("expected").object(); auto state=expected.at("state").object();
    state["dealer"]=Json(99.0); expected["state"]=Json(state); step["expected"]=Json(expected); steps[0]=Json(step); f["steps"]=Json(steps);
    std::string error;
    try { replay_fixture(Json(f)); } catch(const std::exception& e) { error=e.what(); }
    CHECK("first divergence identifies field", error.find("high: steps[0]: steps[0].expected.state.dealer")!=std::string::npos);
    f=load_case("partners-best-same-card").object();
    auto deliveries=f.at("deliveries").array(); deliveries.erase(deliveries.begin()); f["deliveries"]=Json(deliveries);
    CHECK("delivery cadence cannot omit observation", rejects([&]{replay_fixture(Json(f));}));
    f=load_case("high").object(); steps=f.at("steps").array(); step=steps[0].object(); step["id"]=Json(257.0); steps[0]=Json(step); f["steps"]=Json(steps);
    CHECK("action cannot wrap uint8", rejects([&]{replay_fixture(Json(f));}));
}
}
