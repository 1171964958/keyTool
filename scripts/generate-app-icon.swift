#!/usr/bin/env swift

import AppKit
import Foundation

let projectRoot = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent()
    .deletingLastPathComponent()
let iconsetURL = projectRoot.appendingPathComponent("Support/AppIcon.iconset")

try FileManager.default.createDirectory(
    at: iconsetURL,
    withIntermediateDirectories: true
)

let variants: [(name: String, pixels: Int)] = [
    ("icon_16x16.png", 16),
    ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32),
    ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128),
    ("icon_128x128@2x.png", 256),
    ("icon_256x256.png", 256),
    ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512),
    ("icon_512x512@2x.png", 1024)
]

for variant in variants {
    let data = try renderIcon(pixels: variant.pixels)
    try data.write(to: iconsetURL.appendingPathComponent(variant.name))
}

let previewData = try renderIcon(pixels: 1024)
try previewData.write(to: projectRoot.appendingPathComponent("Support/AppIcon-1024.png"))

private func renderIcon(pixels: Int) throws -> Data {
    guard let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: pixels,
        pixelsHigh: pixels,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
    ), let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
        throw IconError.renderFailed
    }

    bitmap.size = NSSize(width: pixels, height: pixels)
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    context.shouldAntialias = true
    context.imageInterpolation = .high

    let canvas = NSRect(x: 0, y: 0, width: pixels, height: pixels)
    NSColor.clear.setFill()
    canvas.fill()

    let scale = CGFloat(pixels) / 1_024
    let tile = NSBezierPath(
        roundedRect: NSRect(
            x: 78 * scale,
            y: 78 * scale,
            width: 868 * scale,
            height: 868 * scale
        ),
        xRadius: 196 * scale,
        yRadius: 196 * scale
    )

    if pixels >= 64 {
        let shadow = NSShadow()
        shadow.shadowColor = NSColor.black.withAlphaComponent(0.26)
        shadow.shadowBlurRadius = 42 * scale
        shadow.shadowOffset = NSSize(width: 0, height: -18 * scale)
        shadow.set()
    }

    let background = NSGradient(colors: [
        NSColor(srgbRed: 0.18, green: 0.15, blue: 0.52, alpha: 1),
        NSColor(srgbRed: 0.42, green: 0.25, blue: 0.88, alpha: 1),
        NSColor(srgbRed: 0.55, green: 0.32, blue: 0.95, alpha: 1)
    ])
    background?.draw(in: tile, angle: -48)

    NSGraphicsContext.current?.saveGraphicsState()
    tile.addClip()

    let glow = NSGradient(colors: [
        NSColor.white.withAlphaComponent(0.22),
        NSColor.white.withAlphaComponent(0)
    ])
    glow?.draw(
        in: NSBezierPath(
            ovalIn: NSRect(
                x: -120 * scale,
                y: 520 * scale,
                width: 920 * scale,
                height: 760 * scale
            )
        ),
        relativeCenterPosition: NSPoint(x: -0.25, y: 0.4)
    )
    NSGraphicsContext.current?.restoreGraphicsState()

    NSColor.white.withAlphaComponent(0.16).setStroke()
    tile.lineWidth = max(1, 3 * scale)
    tile.stroke()

    let keycap = NSBezierPath(
        roundedRect: NSRect(
            x: 244 * scale,
            y: 244 * scale,
            width: 536 * scale,
            height: 536 * scale
        ),
        xRadius: 148 * scale,
        yRadius: 148 * scale
    )
    NSColor.white.withAlphaComponent(0.13).setFill()
    keycap.fill()
    NSColor.white.withAlphaComponent(0.25).setStroke()
    keycap.lineWidth = max(1, 5 * scale)
    keycap.stroke()

    let command = "⌘" as NSString
    let font = NSFont.systemFont(ofSize: 410 * scale, weight: .semibold)
    let attributes: [NSAttributedString.Key: Any] = [
        .font: font,
        .foregroundColor: NSColor.white
    ]
    let textSize = command.size(withAttributes: attributes)
    command.draw(
        at: NSPoint(
            x: (CGFloat(pixels) - textSize.width) / 2,
            y: (CGFloat(pixels) - textSize.height) / 2 + 8 * scale
        ),
        withAttributes: attributes
    )

    let accentDot = NSBezierPath(
        ovalIn: NSRect(
            x: 690 * scale,
            y: 208 * scale,
            width: 74 * scale,
            height: 74 * scale
        )
    )
    NSColor(srgbRed: 0.35, green: 0.92, blue: 0.96, alpha: 1).setFill()
    accentDot.fill()

    context.flushGraphics()
    NSGraphicsContext.restoreGraphicsState()

    guard let data = bitmap.representation(using: .png, properties: [:]) else {
        throw IconError.renderFailed
    }
    return data
}

private enum IconError: Error {
    case renderFailed
}
