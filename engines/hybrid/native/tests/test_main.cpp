#include "test_support.hpp"
#include <iostream>
#include <string_view>
namespace hybrid::test { void test_sampler(); void test_observation(); void test_model(); }
int main(int argc, char** argv) {
    try {
        if(argc>1 && (argc!=2 || std::string_view(argv[1])!="--sampler")) throw std::invalid_argument("usage: hybrid-tests [--sampler]");
        hybrid::test::test_sampler();
        if(argc>1) { std::cout << hybrid::test::checks << " sampler checks passed\n"; return 0; }
        hybrid::test::test_model();
        hybrid::test::test_observation();
        hybrid::test::test_schema();
        hybrid::test::test_match();
        hybrid::test::test_parity();
        std::cout << hybrid::test::checks << " checks passed\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
