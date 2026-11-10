#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python_bin="${HYBRID_PYTHON:-python3}"
"$python_bin" -c 'import sys; assert (3,11) <= sys.version_info[:2] < (3,14), "Hybrid requires Python 3.11–3.13; set HYBRID_PYTHON"'
if [ ! -x "$engine_dir/.venv/bin/python" ]; then
    "$python_bin" -m venv --copies "$engine_dir/.venv"
fi
"$engine_dir/.venv/bin/python" -m pip install -e "$engine_dir"
