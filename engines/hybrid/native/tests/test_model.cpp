#include "test_support.hpp"
#include "hybrid/integrity.hpp"
#include "hybrid/model.hpp"
#include <cmath>
#include <limits>
namespace hybrid::test {
void test_model() {
    auto hash=[](std::string_view s) { return sha256(std::as_bytes(std::span(s.data(),s.size()))); };
    CHECK("SHA256 empty",hash("")=="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    CHECK("SHA256 abc",hash("abc")=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    CHECK("SHA256 multiblock",hash(std::string(1000000,'a'))=="cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0");
    NetworkOutput raw{}; raw.policy_logits.fill(10000); raw.win_logit=10000;
    ActionMask mask{}; mask[1]=mask[4]=true;
    auto p=normalize_output(raw,mask);
    CHECK("illegal exactly zero",p.policy[0]==0);
    CHECK("masked probability",p.policy[1]==.5f && p.policy[4]==.5f);
    CHECK("stable sigmoid",p.win==1);
    raw.policy_logits[0]=std::numeric_limits<float>::quiet_NaN();
    CHECK("invalid logits fail for caller fallback",rejects([&]{(void)normalize_output(raw,mask);}));
}
}
