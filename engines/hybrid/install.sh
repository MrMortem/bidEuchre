#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python_bin="${HYBRID_PYTHON:-$engine_dir/.venv/bin/python}"
if [ ! -x "$python_bin" ]; then python_bin="${HYBRID_PYTHON:-python3}"; fi
"$python_bin" -c 'import sys; assert (3,10) <= sys.version_info[:2] < (3,14), "Hybrid requires Python 3.10–3.13; set HYBRID_PYTHON"'
if [ ! -x "$engine_dir/.venv/bin/python" ]; then
    "$python_bin" -m venv --copies "$engine_dir/.venv"
fi
"$engine_dir/.venv/bin/python" -m pip install "cmake==4.4.4" -e "$engine_dir"
# Exact local environment evidence; regenerated only on explicit installation.
"$engine_dir/.venv/bin/python" -m pip freeze --all > "$engine_dir/.venv/dependencies.lock"
"$engine_dir/.venv/bin/python" -m hybrid_training.cli --help >/dev/null
