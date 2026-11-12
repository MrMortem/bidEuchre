#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project_dir="$(cd "$engine_dir/../.." && pwd)"
. "$engine_dir/scripts/dotnet.sh"
# Supply a real private SDK executable, not a fake CLI: apphost discovery is the
# regression. Relative link chains and spaces model standard PATH SDK shims.
export BIDEUCHRE_DOTNET="${1:?Pass an absolute private SDK dotnet executable}"
bideuchre_find_dotnet "$project_dir"
sdk="$BIDEUCHRE_DOTNET"
expected_root="$(cd -P "$(dirname "$sdk")" && pwd)"
temporary="$(mktemp -d "${TMPDIR:-/tmp}/bid euchre sdk.XXXXXX")"
trap 'rm -rf "$temporary"' EXIT
mkdir -p "$temporary/shim bin" "$temporary/links" "$temporary/repo"
ln -s "$sdk" "$temporary/links/sdk target"
ln -s '../links/sdk target' "$temporary/shim bin/dotnet"
ln -s '../shim bin' "$temporary/repo/.dotnet"
apphost="$project_dir/src/BidEuchre.Arena/bin/Debug/net10.0/BidEuchre.Arena"
for mode in explicit path root repository; do
    (
        unset BIDEUCHRE_DOTNET DOTNET_ROOT DOTNET_ROOT_ARM64 DOTNET_ROOT_X64
        export DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_MULTILEVEL_LOOKUP=0
        case "$mode" in
            explicit) export BIDEUCHRE_DOTNET="$temporary/shim bin/dotnet" ;;
            path) export PATH="$temporary/shim bin:$PATH" ;;
            root) export DOTNET_ROOT="$temporary/shim bin" ;;
            repository) : ;;
        esac
        # Avoid the repository fallback in the PATH case; the other modes have
        # precedence over it. All checks start outside the real repository.
        cd "$temporary"
        discovery_repo="$temporary/repo"
        if [ "$mode" = path ]; then discovery_repo="$temporary/absent-repo"; fi
        bideuchre_find_dotnet "$discovery_repo"
        "$BIDEUCHRE_DOTNET" --version > "$temporary/$mode-cli.log"
        "$apphost" export-fixtures --output "$temporary/$mode.jsonl"
        [ "$DOTNET_ROOT" = "$expected_root" ]
        [ "$(wc -l < "$temporary/$mode.jsonl" | tr -d ' ')" = 62 ]
        echo "PASS SDK discovery $mode: real apphost exported 62 traces from $DOTNET_ROOT"
    )
done
