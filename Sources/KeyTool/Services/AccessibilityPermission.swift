import AppKit
import ApplicationServices

enum AccessibilityPermission {
    static var isTrusted: Bool {
        CGPreflightPostEventAccess()
    }

    static func request() {
        if !CGRequestPostEventAccess() {
            let options = [
                kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true
            ] as CFDictionary
            AXIsProcessTrustedWithOptions(options)
        }
    }

    static func openSystemSettings() {
        guard let url = URL(
            string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"
        ) else {
            return
        }
        NSWorkspace.shared.open(url)
    }
}
