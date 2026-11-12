#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
build_dir="$engine_dir/build"
python_bin="${HYBRID_PYTHON:-$engine_dir/.venv/bin/python}"
cmake_bin="${HYBRID_CMAKE:-$engine_dir/.venv/bin/cmake}"
if [ ! -x "$python_bin" ] || [ ! -x "$cmake_bin" ]; then
    echo "Run engines/hybrid/install.sh first (or set HYBRID_PYTHON and HYBRID_CMAKE)." >&2
    exit 1
fi
"$cmake_bin" -S "$engine_dir" -B "$build_dir" -DPython3_EXECUTABLE="$python_bin" -DHYBRID_TRAINING_PYTHON="$python_bin" -DBUILD_TESTING=ON -DCMAKE_BUILD_TYPE=Release "$@"
"$cmake_bin" --build "$build_dir" --parallel 2
