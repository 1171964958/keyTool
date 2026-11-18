import AppKit
import Foundation

struct MacroTask: Codable, Identifiable, Equatable {
    var id = UUID()
    var name: String
    var target: TargetApplication?
    var actions: [MacroAction]
    var schedule: TaskSchedule
    var isScheduleEnabled: Bool
    var loopCount: Int
    var isLoopInfinite: Bool
    var shortcuts: TaskShortcuts
    var updatedAt: Date

    static let loopCountRange = 1...10_000

    init(
        id: UUID = UUID(),
        name: String,
        target: TargetApplication?,
        actions: [MacroAction],
        schedule: TaskSchedule,
        isScheduleEnabled: Bool,
        loopCount: Int = 1,
        isLoopInfinite: Bool = false,
        shortcuts: TaskShortcuts = .empty,
        updatedAt: Date
    ) {
        self.id = id
        self.name = name
        self.target = target
        self.actions = actions
        self.schedule = schedule
        self.isScheduleEnabled = isScheduleEnabled
        self.loopCount = min(max(loopCount, 1), 10_000)
        self.isLoopInfinite = isLoopInfinite
        self.shortcuts = shortcuts
        self.updatedAt = updatedAt
    }

    private enum CodingKeys: String, CodingKey {
        case id
        case name
        case target
        case actions
        case schedule
        case isScheduleEnabled
        case loopCount
        case isLoopInfinite
        case shortcuts
        case updatedAt
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        name = try container.decode(String.self, forKey: .name)
        target = try container.decodeIfPresent(TargetApplication.self, forKey: .target)
        actions = try container.decode([MacroAction].self, forKey: .actions)
        schedule = try container.decode(TaskSchedule.self, forKey: .schedule)
        isScheduleEnabled = try container.decode(Bool.self, forKey: .isScheduleEnabled)
        let decodedLoopCount = try container.decodeIfPresent(Int.self, forKey: .loopCount) ?? 1
        loopCount = min(max(decodedLoopCount, 1), 10_000)
        isLoopInfinite = try container.decodeIfPresent(
            Bool.self,
            forKey: .isLoopInfinite
        ) ?? false
        shortcuts = try container.decodeIfPresent(
            TaskShortcuts.self,
            forKey: .shortcuts
        ) ?? .empty
        updatedAt = try container.decode(Date.self, forKey: .updatedAt)
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(id, forKey: .id)
        try container.encode(name, forKey: .name)
        try container.encodeIfPresent(target, forKey: .target)
        try container.encode(actions, forKey: .actions)
        try container.encode(schedule, forKey: .schedule)
        try container.encode(isScheduleEnabled, forKey: .isScheduleEnabled)
        try container.encode(loopCount, forKey: .loopCount)
        try container.encode(isLoopInfinite, forKey: .isLoopInfinite)
        try container.encode(shortcuts, forKey: .shortcuts)
        try container.encode(updatedAt, forKey: .updatedAt)
    }

    static func newTask() -> MacroTask {
        MacroTask(
            name: "新任务",
            target: nil,
            actions: [
                .delay(DelayAction(seconds: 0.2))
            ],
            schedule: .manual,
            isScheduleEnabled: false,
            loopCount: 1,
            isLoopInfinite: false,
            shortcuts: .empty,
            updatedAt: Date()
        )
    }
}

struct TaskShortcuts: Codable, Equatable {
    var start: GlobalShortcut?
    var stop: GlobalShortcut?

    static let empty = TaskShortcuts(start: nil, stop: nil)
}

struct GlobalShortcut: Codable, Equatable, Hashable {
    var keyCode: UInt32
    var modifiers: UInt64
    var keyLabel: String

    var displayName: String {
        KeyboardAction.modifierSymbols(modifiers)
            + KeyCodeLabel.normalized(
                storedLabel: keyLabel,
                keyCode: UInt16(clamping: keyCode)
            )
    }

    var hasModifier: Bool {
        modifiers != 0
    }
}

struct TargetApplication: Codable, Equatable, Hashable, Identifiable {
    var name: String
    var bundleIdentifier: String?
    var processID: Int32

    var id: Int32 { processID }
}

enum MacroAction: Codable, Identifiable, Equatable {
    case keyboard(KeyboardAction)
    case mouseClick(MouseClickAction)
    case mouseMove(MouseMoveAction)
    case scroll(ScrollAction)
    case delay(DelayAction)

    var id: UUID {
        switch self {
        case .keyboard(let action): action.id
        case .mouseClick(let action): action.id
        case .mouseMove(let action): action.id
        case .scroll(let action): action.id
        case .delay(let action): action.id
        }
    }

    var title: String {
        switch self {
        case .keyboard: "按键组合"
        case .mouseClick: "鼠标点击"
        case .mouseMove: "鼠标移动"
        case .scroll: "鼠标滚动"
        case .delay: "等待"
        }
    }

    var summary: String {
        switch self {
        case .keyboard(let action):
            let repetition = action.repeatCount > 1
                ? " × \(action.repeatCount)"
                : ""
            return action.displayName + repetition
        case .mouseClick(let action):
            return "\(action.button.label)键 @ \(Int(action.x)), \(Int(action.y))"
        case .mouseMove(let action):
            return "移动到 \(Int(action.x)), \(Int(action.y))"
        case .scroll(let action):
            return "水平 \(Int(action.deltaX))，垂直 \(Int(action.deltaY))"
        case .delay(let action):
            return String(format: "%.2f 秒", action.seconds)
        }
    }
}

struct KeyboardAction: Codable, Identifiable, Equatable {
    static let repeatCountRange = 1...10_000

    var id = UUID()
    var keyCode: UInt16
    var modifiers: UInt64
    var keyLabel: String
    var repeatCount: Int

    init(
        id: UUID = UUID(),
        keyCode: UInt16,
        modifiers: UInt64,
        keyLabel: String,
        repeatCount: Int = 1
    ) {
        self.id = id
        self.keyCode = keyCode
        self.modifiers = modifiers
        self.keyLabel = keyLabel
        self.repeatCount = min(
            max(repeatCount, Self.repeatCountRange.lowerBound),
            Self.repeatCountRange.upperBound
        )
    }

    private enum CodingKeys: String, CodingKey {
        case id
        case keyCode
        case modifiers
        case keyLabel
        case repeatCount
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        keyCode = try container.decode(UInt16.self, forKey: .keyCode)
        modifiers = try container.decode(UInt64.self, forKey: .modifiers)
        keyLabel = try container.decode(String.self, forKey: .keyLabel)
        let decodedRepeatCount = try container.decodeIfPresent(
            Int.self,
            forKey: .repeatCount
        ) ?? 1
        repeatCount = min(
            max(decodedRepeatCount, Self.repeatCountRange.lowerBound),
            Self.repeatCountRange.upperBound
        )
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(id, forKey: .id)
        try container.encode(keyCode, forKey: .keyCode)
        try container.encode(modifiers, forKey: .modifiers)
        try container.encode(keyLabel, forKey: .keyLabel)
        try container.encode(repeatCount, forKey: .repeatCount)
    }

    var displayName: String {
        Self.modifierSymbols(modifiers)
            + KeyCodeLabel.normalized(
                storedLabel: keyLabel,
                keyCode: keyCode
            )
    }

    static func modifierSymbols(_ rawValue: UInt64) -> String {
        let flags = NSEvent.ModifierFlags(rawValue: UInt(rawValue))
        var symbols = ""
        if flags.contains(.control) { symbols += "⌃" }
        if flags.contains(.option) { symbols += "⌥" }
        if flags.contains(.shift) { symbols += "⇧" }
        if flags.contains(.command) { symbols += "⌘" }
        return symbols
    }
}

struct MouseClickAction: Codable, Identifiable, Equatable {
    var id = UUID()
    var x: Double
    var y: Double
    var button: MouseButtonKind
}

struct MouseMoveAction: Codable, Identifiable, Equatable {
    var id = UUID()
    var x: Double
    var y: Double
}

struct ScrollAction: Codable, Identifiable, Equatable {
    var id = UUID()
    var deltaX: Int32
    var deltaY: Int32
}

struct DelayAction: Codable, Identifiable, Equatable {
    var id = UUID()
    var seconds: Double
}

enum MouseButtonKind: String, Codable, CaseIterable, Identifiable {
    case left
    case right
    case center

    var id: String { rawValue }

    var label: String {
        switch self {
        case .left: "左"
        case .right: "右"
        case .center: "中"
        }
    }
}

struct TaskSchedule: Codable, Equatable {
    static let intervalRange = 1...86_400

    enum Mode: String, Codable, CaseIterable, Identifiable {
        case manual
        case interval
        case daily

        var id: String { rawValue }

        var label: String {
            switch self {
            case .manual: "手动"
            case .interval: "按间隔"
            case .daily: "每天定时"
            }
        }
    }

    var mode: Mode
    var intervalSeconds: Int
    var hour: Int
    var minute: Int

    static let manual = TaskSchedule(
        mode: .manual,
        intervalSeconds: 60,
        hour: 9,
        minute: 0
    )

    func nextRun(after date: Date, calendar: Calendar = .current) -> Date? {
        switch mode {
        case .manual:
            return nil
        case .interval:
            return date.addingTimeInterval(TimeInterval(max(1, intervalSeconds)))
        case .daily:
            let components = DateComponents(hour: hour, minute: minute)
            return calendar.nextDate(
                after: date,
                matching: components,
                matchingPolicy: .nextTime
            )
        }
    }
}
