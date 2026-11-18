#!/bin/zsh

set -euo pipefail

platform_root="$(cd "$(dirname "$0")/.." && pwd)"
repo_root="$(cd "$platform_root/.." && pwd)"
app_path="$repo_root/dist/mac/KeyTool.app"
contents_path="$app_path/Contents"
arm_binary="$platform_root/.build/release/KeyTool"
x86_binary="$platform_root/.build-x86_64/out/Products/Release/KeyTool"

swift build --package-path "$platform_root" -c release
swift build \
    --package-path "$platform_root" \
    -c release \
    --triple x86_64-apple-macosx14.0 \
    --scratch-path "$platform_root/.build-x86_64"

if [[ "$app_path" != "$repo_root/dist/mac/KeyTool.app" ]]; then
    echo "Unexpected app path: $app_path" >&2
    exit 1
fi

rm -rf "$app_path"
mkdir -p "$repo_root/dist/mac" "$contents_path/MacOS" "$contents_path/Resources"
lipo -create "$arm_binary" "$x86_binary" -output "$contents_path/MacOS/KeyTool"
cp "$platform_root/Support/Info.plist" "$contents_path/Info.plist"
cp "$platform_root/Support/AppIcon.icns" "$contents_path/Resources/AppIcon.icns"

codesign \
    --force \
    --sign - \
    --identifier com.keytool.desktop \
    --requirements '=designated => identifier "com.keytool.desktop"' \
    "$app_path"

echo "Built $app_path"
