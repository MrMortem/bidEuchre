#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"$engine_dir/build.sh" "$@"
# The baseline is EXCLUDE_FROM_ALL; explicitly build its registered CTest target.
cmake --build "$engine_dir/build" --target bideuchre-cpp-heuristic-tests --parallel 2
ctest --test-dir "$engine_dir/build" --output-on-failure
