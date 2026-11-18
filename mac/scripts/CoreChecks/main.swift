import AppKit
import Darwin
import Foundation

var passed = 0

func check(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
    passed += 1
}

let task = MacroTask(
    name: "测试任务",
    target: TargetApplication(
        name: "TextEdit",
        bundleIdentifier: "com.apple.TextEdit",
        processID: 42
    ),
    actions: [
        .keyboard(
            KeyboardAction(
                keyCode: 0,
                modifiers: 1_048_576,
                keyLabel: "A",
                repeatCount: 4
            )
        ),
        .delay(DelayAction(seconds: 0.25))
    ],
    schedule: TaskSchedule(
        mode: .interval,
        intervalSeconds: 30,
        hour: 9,
        minute: 0
    ),
    isScheduleEnabled: true,
    loopCount: 3,
    shortcuts: TaskShortcuts(
        start: GlobalShortcut(
            keyCode: 40,
            modifiers: 1_048_576,
            keyLabel: "K"
        ),
        stop: nil
    ),
    updatedAt: Date(timeIntervalSince1970: 1_700_000_000)
)

let encodedTask = try JSONEncoder().encode(task)
let decodedTask = try JSONDecoder().decode(MacroTask.self, from: encodedTask)
check(decodedTask == task, "Task should round-trip through JSON")
check(
    decodedTask.actions.first?.summary == "⌘A × 4",
    "Keyboard action summaries should include repeat counts"
)

let legacyKeyboardJSON = try JSONEncoder().encode(
    KeyboardAction(
        keyCode: 0,
        modifiers: UInt64(NSEvent.ModifierFlags.command.rawValue),
        keyLabel: "A"
    )
)
var legacyKeyboardObject = try JSONSerialization.jsonObject(
    with: legacyKeyboardJSON
) as! [String: Any]
legacyKeyboardObject.removeValue(forKey: "repeatCount")
let migratedKeyboard = try JSONDecoder().decode(
    KeyboardAction.self,
    from: JSONSerialization.data(withJSONObject: legacyKeyboardObject)
)
check(
    migratedKeyboard.repeatCount == 1,
    "Legacy keyboard actions should default to one press"
)
check(
    KeyboardAction(
        keyCode: 0,
        modifiers: 0,
        keyLabel: "A",
        repeatCount: 0
    ).repeatCount == 1,
    "Keyboard repeat counts should clamp to the supported minimum"
)
check(
    KeyboardAction(
        keyCode: 0,
        modifiers: 0,
        keyLabel: "A",
        repeatCount: 10_001
    ).repeatCount == 10_000,
    "Keyboard repeat counts should clamp to the supported maximum"
)

var legacyJSON = try JSONSerialization.jsonObject(with: encodedTask) as! [String: Any]
legacyJSON.removeValue(forKey: "loopCount")
legacyJSON.removeValue(forKey: "isLoopInfinite")
legacyJSON.removeValue(forKey: "shortcuts")
let legacyData = try JSONSerialization.data(withJSONObject: legacyJSON)
let migratedTask = try JSONDecoder().decode(MacroTask.self, from: legacyData)
check(migratedTask.loopCount == 1, "Legacy tasks should default to one loop")
check(!migratedTask.isLoopInfinite, "Legacy tasks should default to finite loops")
check(migratedTask.shortcuts == .empty, "Legacy tasks should have no shortcuts")
check(
    KeyCodeLabel.normalized(storedLabel: "\u{F70F}", keyCode: 111) == "F12",
    "Function keys should ignore private-use characters"
)
check(
    GlobalShortcut(
        keyCode: 111,
        modifiers: UInt64(NSEvent.ModifierFlags.command.rawValue),
        keyLabel: "\u{F70F}"
    ).displayName == "⌘F12",
    "Command-F12 should be displayed clearly"
)
let shiftCommand = NSEvent.ModifierFlags.command.union(.shift)
check(
    GlobalShortcut(
        keyCode: 111,
        modifiers: UInt64(shiftCommand.rawValue),
        keyLabel: "F12"
    ).displayName == "⇧⌘F12",
    "Shift and Command should both be preserved"
)
check(
    KeyboardAction(
        keyCode: 0,
        modifiers: UInt64(shiftCommand.rawValue),
        keyLabel: "A"
    ).displayName == "⇧⌘A",
    "Keyboard actions should preserve Shift and Command"
)

check(
    TaskSchedule.manual.nextRun(after: Date()) == nil,
    "Manual schedules should not fire automatically"
)

let intervalStart = Date(timeIntervalSince1970: 1_700_000_000)
let intervalSchedule = TaskSchedule(
    mode: .interval,
    intervalSeconds: 45,
    hour: 0,
    minute: 0
)
check(
    intervalSchedule.nextRun(after: intervalStart)
        == intervalStart.addingTimeInterval(45),
    "Interval schedules should use the configured delay"
)

var calendar = Calendar(identifier: .gregorian)
calendar.timeZone = TimeZone(secondsFromGMT: 0)!
let dailyStart = calendar.date(
    from: DateComponents(
        year: 2026,
        month: 11,
        day: 17,
        hour: 10,
        minute: 30
    )
)!
let dailySchedule = TaskSchedule(
    mode: .daily,
    intervalSeconds: 60,
    hour: 9,
    minute: 0
)
let nextDailyRun = dailySchedule.nextRun(
    after: dailyStart,
    calendar: calendar
)!
let dailyComponents = calendar.dateComponents(
    [.day, .hour, .minute],
    from: nextDailyRun
)
check(
    dailyComponents.day == 18
        && dailyComponents.hour == 9
        && dailyComponents.minute == 0,
    "Daily schedules should find the next configured time"
)

let runStartedAt = Date(timeIntervalSince1970: 1_700_000_000)
let runningProgress = RunProgress(
    currentLoop: 2,
    totalLoops: 5,
    currentAction: 3,
    totalActions: 4,
    startedAt: runStartedAt
)
check(
    runningProgress.elapsedTimeDescription(
        at: runStartedAt.addingTimeInterval(65)
    ) == "01:05",
    "Execution time should format minutes and seconds"
)
check(
    runningProgress.elapsedTimeDescription(
        at: runStartedAt.addingTimeInterval(3_661)
    ) == "1:01:01",
    "Execution time should include hours when needed"
)

print("\(passed) core checks passed")
