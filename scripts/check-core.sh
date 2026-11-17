#!/bin/zsh

set -euo pipefail

project_root="$(cd "$(dirname "$0")/.." && pwd)"
binary_path="$project_root/.build/keytool-core-checks"

swiftc \
    "$project_root/Sources/KeyTool/Models/MacroTask.swift" \
    "$project_root/Sources/KeyTool/Utilities/KeyCodeLabel.swift" \
    "$project_root/scripts/CoreChecks/main.swift" \
    -o "$binary_path"

"$binary_path"
