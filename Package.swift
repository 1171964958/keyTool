// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "KeyTool",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .executable(name: "KeyTool", targets: ["KeyTool"])
    ],
    targets: [
        .executableTarget(name: "KeyTool")
    ],
    swiftLanguageVersions: [.v5]
)
