#include "test_support.hpp"
#include <iostream>
int main() {
    try {
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
