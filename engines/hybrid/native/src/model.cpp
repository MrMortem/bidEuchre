#include "hybrid/model.hpp"
#include "hybrid/integrity.hpp"
#include <algorithm>
#include <bit>
#include <cmath>
#include <fstream>
#include <limits>
#include <sstream>
#include <stdexcept>
namespace hybrid {
namespace {
struct Spec {const char* name;std::size_t out,in;bool relu;};
constexpr std::array<Spec,9> specs={{{"main.fc1",256,1188,true},{"main.fc2",128,256,true},
    {"main.policy",61,128,false},{"main.win",1,128,false},{"main.trick",7,128,false},{"main.ownership",96,128,false},
    {"boundary.fc1",64,38,true},{"boundary.fc2",32,64,true},{"boundary.out",1,32,false}}};
void require(bool condition,const char* message) {if(!condition) throw std::invalid_argument(message);}
bool hex(const std::string& value,std::size_t length=64) {
    return value.size()==length && std::all_of(value.begin(),value.end(),[](char c){return (c>='0' && c<='9') || (c>='a' && c<='f');});
}
void finite(std::span<const float> values) {for(auto x:values) if(!std::isfinite(x)) throw std::domain_error("nonfinite network input/result");}
std::string read(const std::filesystem::path& path,std::size_t maximum) {
    std::ifstream stream(path,std::ios::binary|std::ios::ate);
    require(bool(stream),"cannot open artifact file");
    auto size=stream.tellg();require(size>=0 && static_cast<std::uintmax_t>(size)<=maximum,"invalid artifact file length");
    std::string bytes(static_cast<std::size_t>(size),'\0');stream.seekg(0);
    stream.read(bytes.data(),static_cast<std::streamsize>(bytes.size()));require(bool(stream),"truncated artifact");return bytes;
}
void number(const Json& value,std::size_t expected) {require(value.is_number() && value.number()==static_cast<double>(expected),"invalid artifact numeric field");}
void softmax(std::span<const float> logits,std::span<float> probabilities) {
    float maximum=*std::max_element(logits.begin(),logits.end());double sum=0;
    for(std::size_t i=0;i<logits.size();++i) {probabilities[i]=std::exp(logits[i]-maximum);sum+=probabilities[i];}
    for(auto& p:probabilities) p=static_cast<float>(p/sum);
}
}
FrozenModel FrozenModel::load(const std::filesystem::path& manifest) {
    static_assert(sizeof(float)==4 && std::numeric_limits<float>::is_iec559);
    auto m=beu::json::parse(read(manifest,65536));
    number(m.at("version"),1);
    require(m.at("architecture").string()=="hybrid-mlp-v1" && m.at("dtype").string()=="float32" &&
        m.at("byte_order").string()=="little" && m.at("weights").string()=="weights.bin","incompatible artifact encoding");
    require(m.at("schema_sha256").string()==schema::schema_sha256 && m.at("rule_sha256").string()==schema::rule_sha256,"incompatible artifact fingerprints");
    require(hex(m.at("source_sha256").string()) && hex(m.at("model_sha256").string()),"invalid artifact fingerprint");
    const auto& provenance=m.at("provenance");
    require(hex(provenance.at("git_revision").string(),40) && hex(provenance.at("source_diff_sha256").string()) && provenance.at("metadata").is_object(),"invalid source provenance");
    const auto& tensors=m.at("tensors").array();require(tensors.size()==specs.size()*2,"invalid tensor count");
    std::size_t offset=0;
    for(std::size_t index=0;index<specs.size();++index) {
        const auto& spec=specs[index];
        for(int bias=0;bias<2;++bias) {
            const auto& t=tensors[index*2+bias];const auto& shape=t.at("shape").array();
            require(t.object().size()==5,"invalid tensor fields");
            require(t.at("name").string()==std::string(spec.name)+(bias?".bias":".weight"),"invalid tensor name");
            require(shape.size()==(bias?1u:2u),"invalid tensor rank");number(shape[0],spec.out);if(!bias) number(shape[1],spec.in);
            number(t.at("offset"),offset);auto bytes=spec.out*(bias?1:spec.in)*4;number(t.at("byte_length"),bytes);
            require(t.at("activation").string()==(!bias && spec.relu?"relu":"none"),"invalid tensor activation");offset+=bytes;
        }
    }
    number(m.at("byte_length"),offset);
    auto data=read(manifest.parent_path()/"weights.bin",offset);require(data.size()==offset,"invalid weight byte length");
    require(sha256(std::as_bytes(std::span(data.data(),data.size())))==m.at("model_sha256").string(),"weight SHA256 mismatch");
    FrozenModel model;offset=0;
    auto floats=[&](std::size_t count) {
        std::vector<float> values;values.reserve(count);
        for(std::size_t i=0;i<count;++i) {
            std::uint32_t bits=0;for(int b=0;b<4;++b) bits|=static_cast<std::uint32_t>(static_cast<unsigned char>(data[offset++]))<<(8*b);
            values.push_back(std::bit_cast<float>(bits));
        }
        finite(values);return values;
    };
    for(const auto& spec:specs) model.layers_.push_back({spec.in,spec.out,spec.relu,floats(spec.in*spec.out),floats(spec.out)});
    return model;
}
std::vector<float> FrozenModel::linear(std::size_t index,std::span<const float> input) const {
    finite(input);const auto& layer=layers_.at(index);require(input.size()==layer.in,"invalid inference input shape");
    std::vector<float> output(layer.out);
    for(std::size_t row=0;row<layer.out;++row) {
        float sum=layer.bias[row];
        for(std::size_t col=0;col<layer.in;++col) sum+=input[col]*layer.weights[row*layer.in+col];
        if(!std::isfinite(sum)) throw std::domain_error("nonfinite network inference");
        output[row]=layer.relu?std::max(0.f,sum):sum;
    }
    return output;
}
NetworkOutput FrozenModel::evaluate(const Features& features) const {
    auto a=linear(0,features),b=linear(1,a);NetworkOutput result;
    auto copy=[&](std::size_t i,auto& output){auto values=linear(i,b);std::copy(values.begin(),values.end(),output.begin());};
    copy(2,result.policy_logits);result.win_logit=linear(3,b)[0];copy(4,result.trick_logits);copy(5,result.ownership_logits);return result;
}
float FrozenModel::boundary_logit(const BoundaryFeatures& features) const {auto a=linear(6,features),b=linear(7,a);return linear(8,b)[0];}
NormalizedOutput normalize_output(const NetworkOutput& raw,const ActionMask& legal) {
    finite(raw.policy_logits);finite(std::span(&raw.win_logit,1));finite(raw.trick_logits);finite(raw.ownership_logits);
    NormalizedOutput result;float maximum=-std::numeric_limits<float>::infinity();
    for(std::size_t i=0;i<legal.size();++i) if(legal[i]) maximum=std::max(maximum,raw.policy_logits[i]);
    if(std::isfinite(maximum)) {
        double sum=0;for(std::size_t i=0;i<legal.size();++i) if(legal[i]) {result.policy[i]=std::exp(raw.policy_logits[i]-maximum);sum+=result.policy[i];}
        for(auto& p:result.policy) p=static_cast<float>(p/sum);
    }
    result.win=raw.win_logit>=0 ? 1.f/(1.f+std::exp(-raw.win_logit)) : std::exp(raw.win_logit)/(1.f+std::exp(raw.win_logit));
    softmax(raw.trick_logits,result.trick);
    for(std::size_t card=0;card<24;++card) softmax(std::span(raw.ownership_logits).subspan(card*4,4),std::span(result.ownership).subspan(card*4,4));
    return result;
}
}
