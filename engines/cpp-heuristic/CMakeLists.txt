cmake_minimum_required(VERSION 3.20)
project(BidEuchreCppHeuristic VERSION 1.0.0 LANGUAGES CXX)

include(CTest)

add_library(bideuchre_heuristic_lib
    src/bot.cpp
    src/game.cpp
    src/json.cpp
    src/search.cpp)
target_include_directories(bideuchre_heuristic_lib PUBLIC src)
target_compile_features(bideuchre_heuristic_lib PUBLIC cxx_std_20)

if(MSVC)
    target_compile_options(bideuchre_heuristic_lib PRIVATE /W4 /permissive-)
else()
    target_compile_options(bideuchre_heuristic_lib PRIVATE -Wall -Wextra -Wpedantic)
endif()

add_executable(bideuchre-cpp-heuristic src/main.cpp)
target_link_libraries(bideuchre-cpp-heuristic PRIVATE bideuchre_heuristic_lib)

if(BUILD_TESTING)
    add_executable(bideuchre-cpp-heuristic-tests tests/tests.cpp)
    target_link_libraries(bideuchre-cpp-heuristic-tests PRIVATE bideuchre_heuristic_lib)
    add_test(NAME cpp-heuristic-unit COMMAND bideuchre-cpp-heuristic-tests)
endif()
