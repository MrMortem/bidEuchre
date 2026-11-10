#!/bin/bash
# Source this file, then pass the repository root. No Python is needed.
bideuchre_find_dotnet() {
    local repository="$1"
    dotnet_command="${BIDEUCHRE_DOTNET:-${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}}"
    if [ -z "$dotnet_command" ]; then
        if [ -x "$repository/.dotnet/dotnet" ]; then dotnet_command="$repository/.dotnet/dotnet";
        else dotnet_command="$(command -v dotnet || true)"; fi
    fi
    if [ ! -x "$dotnet_command" ]; then
        echo "Set BIDEUCHRE_DOTNET to a .NET 10 SDK dotnet executable." >&2
        return 1
    fi
    # macOS readlink has no portable -f. Resolve each file link relative to its
    # physical parent, then canonicalize the final directory (including /tmp).
    local link_target directory links=0
    while [ -L "$dotnet_command" ]; do
        links=$((links + 1))
        if [ "$links" -gt 64 ]; then echo "Too many dotnet executable symlinks." >&2; return 1; fi
        directory="$(cd -P "$(dirname "$dotnet_command")" && pwd)" || return 1
        link_target="$(readlink "$dotnet_command")" || return 1
        case "$link_target" in
            /*) dotnet_command="$link_target" ;;
            *) dotnet_command="$directory/$link_target" ;;
        esac
    done
    directory="$(cd -P "$(dirname "$dotnet_command")" && pwd)" || return 1
    dotnet_command="$directory/$(basename "$dotnet_command")"
    export BIDEUCHRE_DOTNET="$dotnet_command"
    export DOTNET_ROOT="$directory"
}
