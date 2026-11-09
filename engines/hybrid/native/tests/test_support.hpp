#pragma once
#include "hybrid/contracts.hpp"
#include <fstream>
#include <stdexcept>
#include <string_view>

namespace hybrid::test {
inline int checks = 0;
inline void check(std::string_view name, bool condition) {
    ++checks;
    if (!condition) throw std::runtime_error(std::string(name));
}
template<class F> bool rejects(F&& action) {
    try { action(); } catch (const std::exception&) { return true; }
    return false;
}
// Task 5 supplies the authority corpus; each JSONL record is identified by id.
inline Json load_case(std::string_view name) {
    std::ifstream file(HYBRID_FIXTURE_PATH);
    if (!file) throw std::runtime_error("authority fixture corpus not found");
    std::string line;
    while (std::getline(file, line)) {
        if (line.empty()) continue;
        auto value = beu::json::parse(line);
        if (value.at("id").string() == name) return value;
    }
    throw std::runtime_error("fixture not found: " + std::string(name));
}
void test_schema();
}
#define CHECK(name, condition) ::hybrid::test::check(name, (condition))
