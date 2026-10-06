// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "FengYuMuMac",
    platforms: [.macOS("26.0")],
    products: [
        .library(name: "FengYuMuCore", targets: ["FengYuMuCore"]),
        .executable(name: "FengYuMuMac", targets: ["FengYuMuMac"])
    ],
    targets: [
        .target(name: "FengYuMuCore"),
        .executableTarget(
            name: "FengYuMuMac",
            dependencies: ["FengYuMuCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("Carbon"),
                .linkedFramework("CoreGraphics"),
                .linkedFramework("ScreenCaptureKit"),
                .linkedFramework("Security"),
                .linkedFramework("Vision")
            ]
        ),
        .testTarget(name: "FengYuMuCoreTests", dependencies: ["FengYuMuCore"])
    ]
)
