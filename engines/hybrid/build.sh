#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
build_dir="$engine_dir/build"
cmake -S "$engine_dir" -B "$build_dir" -DBUILD_TESTING=ON "$@"
cmake --build "$build_dir" --parallel 2
