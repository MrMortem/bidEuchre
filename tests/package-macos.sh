#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf -- "$fixture"' EXIT
mkdir -p "$fixture/Pixel"
printf 'int main(void) { return 0; }\n' > "$fixture/main.c"
xcrun clang "$fixture/main.c" -o "$fixture/BidEuchre"
rm "$fixture/main.c"
printf 'Rules fixture\n' > "$fixture/bid-euchre-final-rules.md"
printf 'License fixture\n' > "$fixture/Pixel/FONT-LICENSE.txt"
"$project_dir/scripts/create-macos-app.sh" "$fixture"
bundle="$fixture/Bid Euchre.app/Contents"
[[ -x "$bundle/MacOS/BidEuchre" ]]
cmp "$fixture/bid-euchre-final-rules.md" "$bundle/MacOS/bid-euchre-final-rules.md"
cmp "$fixture/Pixel/FONT-LICENSE.txt" "$bundle/MacOS/Pixel/FONT-LICENSE.txt"
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$bundle/Info.plist")" == BidEuchre ]]
codesign --verify --deep --strict "$fixture/Bid Euchre.app"
# Bundle signing must seal shipped resources, not just the Mach-O executable.
printf 'Tampered rules\n' > "$bundle/MacOS/bid-euchre-final-rules.md"
if codesign --verify --deep --strict "$fixture/Bid Euchre.app" 2>/dev/null; then
  echo 'FAIL  Bundle signature accepted a modified resource' >&2
  exit 1
fi
"$project_dir/scripts/create-macos-app.sh" "$fixture"
[[ ! -d "$bundle/MacOS/Bid Euchre.app" ]]
codesign --verify --deep --strict "$fixture/Bid Euchre.app"
echo 'PASS  Mac bundle preserves files, seals resources, and can be rebuilt with a valid signature'
