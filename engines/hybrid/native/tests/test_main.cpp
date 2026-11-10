#include "test_support.hpp"
#include <iostream>
#include <string_view>
namespace hybrid::test { void test_information_tree(); void test_dd_leaf(); void test_generation(); void test_protocol(); void test_search(); void test_sampler(); void test_observation(); void test_model(); }
int main(int argc, char** argv) {
    try {
        if(argc>1 && (argc!=2 || (std::string_view(argv[1])!="--experiments" && std::string_view(argv[1])!="--generation" && std::string_view(argv[1])!="--sampler" && std::string_view(argv[1])!="--search" && std::string_view(argv[1])!="--protocol"))) throw std::invalid_argument("usage: hybrid-tests [--experiments|--generation|--sampler|--search|--protocol]");
        if(argc>1 && std::string_view(argv[1])=="--experiments") { hybrid::test::test_information_tree(); hybrid::test::test_dd_leaf(); std::cout << hybrid::test::checks << " experiment checks passed\n"; return 0; }
        if(argc>1 && std::string_view(argv[1])=="--generation") { hybrid::test::test_generation(); std::cout << hybrid::test::checks << " generation checks passed\n"; return 0; }
        if(argc>1 && std::string_view(argv[1])=="--protocol") { hybrid::test::test_protocol(); std::cout << hybrid::test::checks << " protocol checks passed\n"; return 0; }
        if(argc>1 && std::string_view(argv[1])=="--search") { hybrid::test::test_search(); std::cout << hybrid::test::checks << " search checks passed\n"; return 0; }
        if(argc==1) { hybrid::test::test_information_tree(); hybrid::test::test_dd_leaf(); hybrid::test::test_generation(); hybrid::test::test_protocol(); hybrid::test::test_search(); }
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
