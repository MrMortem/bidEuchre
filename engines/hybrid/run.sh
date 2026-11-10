#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$engine_dir/build/bideuchre-hybrid" "$@"
