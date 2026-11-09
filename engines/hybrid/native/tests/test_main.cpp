#include "test_support.hpp"
#include <iostream>
int main() {
    try {
        hybrid::test::test_schema();
        std::cout << hybrid::test::checks << " checks passed\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
