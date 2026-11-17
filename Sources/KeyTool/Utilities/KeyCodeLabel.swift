import Foundation

enum KeyCodeLabel {
    private static let names: [UInt16: String] = [
        36: "Return",
        48: "Tab",
        49: "Space",
        51: "Delete",
        53: "Escape",
        71: "Clear",
        76: "Enter",
        114: "Help",
        115: "Home",
        116: "Page Up",
        117: "Forward Delete",
        119: "End",
        121: "Page Down",
        122: "F1",
        120: "F2",
        99: "F3",
        118: "F4",
        96: "F5",
        97: "F6",
        98: "F7",
        100: "F8",
        101: "F9",
        109: "F10",
        103: "F11",
        111: "F12",
        105: "F13",
        107: "F14",
        113: "F15",
        106: "F16",
        64: "F17",
        79: "F18",
        80: "F19",
        123: "←",
        124: "→",
        125: "↓",
        126: "↑"
    ]

    static func label(for keyCode: UInt16, characters: String?) -> String {
        if let name = names[keyCode] {
            return name
        }
        guard let characters, !characters.isEmpty else {
            return "Key \(keyCode)"
        }
        return characters.uppercased()
    }

    static func normalized(
        storedLabel: String,
        keyCode: UInt16
    ) -> String {
        if let name = names[keyCode] {
            return name
        }
        if storedLabel.unicodeScalars.contains(where: {
            (0xE000...0xF8FF).contains(Int($0.value))
        }) {
            return "Key \(keyCode)"
        }
        return storedLabel
    }
}
