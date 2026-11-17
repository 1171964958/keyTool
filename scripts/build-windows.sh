#!/bin/zsh

set -euo pipefail

project_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_command="${KEYTOOL_DOTNET:-dotnet}"
project="$project_root/windows/KeyTool.Windows/KeyTool.Windows.csproj"
output="$project_root/dist/windows/win-x64"

"$dotnet_command" publish "$project" \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$output"

archive="$project_root/dist/windows/KeyTool-Windows-x64.zip"
if command -v ditto >/dev/null 2>&1; then
    ditto -c -k --norsrc --noextattr \
        --keepParent \
        "$output" \
        "$archive"
elif command -v zip >/dev/null 2>&1; then
    (
        cd "$project_root/dist/windows"
        zip -r -X -FS KeyTool-Windows-x64.zip win-x64
    )
fi
