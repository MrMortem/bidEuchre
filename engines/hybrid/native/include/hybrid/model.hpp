#pragma once
#include "hybrid/contracts.hpp"
#include <filesystem>
#include <span>
#include <vector>
namespace hybrid {
struct NetworkOutput {
    std::array<float,61> policy_logits{};
    float win_logit=0;
    std::array<float,7> trick_logits{};
    std::array<float,96> ownership_logits{}; // [card][relative seat]
};
struct NormalizedOutput {
    std::array<float,61> policy{};
    float win=0;
    std::array<float,7> trick{};
    std::array<float,96> ownership{};
};
// Nonfinite inputs/results throw domain_error: callers must use their fallback.
// An empty legal mask returns an all-zero policy. Terminal values belong to callers.
[[nodiscard]] NormalizedOutput normalize_output(const NetworkOutput&,const ActionMask&);
class FrozenModel {
public:
    [[nodiscard]] static FrozenModel load(const std::filesystem::path& manifest);
    [[nodiscard]] const std::string& model_sha256() const { return model_sha256_; }
    [[nodiscard]] NetworkOutput evaluate(const Features&) const;
    [[nodiscard]] float boundary_logit(const BoundaryFeatures&) const;
private:
    FrozenModel()=default;
    struct Layer {std::size_t in,out;bool relu;std::vector<float> weights,bias;};
    std::vector<Layer> layers_;
    std::string model_sha256_;
    [[nodiscard]] std::vector<float> linear(std::size_t,std::span<const float>) const;
};
}
