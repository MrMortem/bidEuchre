#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf -- "$fixture"' EXIT
mkdir -p "$fixture/Pixel"
printf '#!/bin/sh\nexit 0\n' > "$fixture/BidEuchre"
chmod +x "$fixture/BidEuchre"
printf 'Rules fixture\n' > "$fixture/bid-euchre-final-rules.md"
printf 'License fixture\n' > "$fixture/Pixel/FONT-LICENSE.txt"
"$project_dir/scripts/create-macos-app.sh" "$fixture"
bundle="$fixture/Bid Euchre.app/Contents"
[[ -x "$bundle/MacOS/BidEuchre" ]]
cmp "$fixture/bid-euchre-final-rules.md" "$bundle/MacOS/bid-euchre-final-rules.md"
cmp "$fixture/Pixel/FONT-LICENSE.txt" "$bundle/MacOS/Pixel/FONT-LICENSE.txt"
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$bundle/Info.plist")" == BidEuchre ]]
"$project_dir/scripts/create-macos-app.sh" "$fixture"
[[ ! -d "$bundle/MacOS/Bid Euchre.app" ]]
echo 'PASS  Mac bundle preserves executable, rules, and license and can be rebuilt'
