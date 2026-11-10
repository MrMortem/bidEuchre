#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Explicit arguments select a development model or an explicit registry. A
# configured default registry is mandatory when selected; failure is fail-closed.
if [ "$#" -eq 0 ]; then
    registry="${HYBRID_REGISTRY:-$engine_dir/state/registry}"
    if [ -n "${HYBRID_REGISTRY:-}" ] || [ -f "$registry/champion.json" ]; then
        exec "$engine_dir/build/bideuchre-hybrid" --registry "$registry"
    fi
fi
exec "$engine_dir/build/bideuchre-hybrid" "$@"
