import AppKit
import CoreGraphics
import SwiftUI

private let actionRecorderTapCallback: CGEventTapCallBack = {
    _, type, event, userInfo in
    if let userInfo {
        let recorder = Unmanaged<ActionRecorder>
            .fromOpaque(userInfo)
            .takeUnretainedValue()
        DispatchQueue.main.async {
            recorder.capture(type: type, event: event)
        }
    }
    return Unmanaged.passUnretained(event)
}

@MainActor
final class ActionRecorder: ObservableObject {
    @Published private(set) var isRecording = false
    @Published private(set) var recordedCount = 0
    @Published private(set) var recordingTaskID: UUID?
    @Published private(set) var errorMessage: String?

    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var recordedActions: [MacroAction] = []
    private var lastEventTimestamp: TimeInterval?
    private var lastMoveTimestamp: TimeInterval = 0
    private var lastMovePoint: CGPoint?
    private let ownProcessID = ProcessInfo.processInfo.processIdentifier

    func start(taskID: UUID) {
        guard !isRecording else { return }
        errorMessage = nil

        let eventTypes: [CGEventType] = [
            .keyDown,
            .leftMouseDown,
            .rightMouseDown,
            .otherMouseDown,
            .scrollWheel,
            .mouseMoved
        ]
        let mask = eventTypes.reduce(CGEventMask(0)) {
            $0 | (CGEventMask(1) << $1.rawValue)
        }

        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .listenOnly,
            eventsOfInterest: mask,
            callback: actionRecorderTapCallback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()
        ) else {
            errorMessage = "无法开始录制，请重新检查辅助功能权限。"
            return
        }

        recordingTaskID = taskID
        recordedActions.removeAll()
        recordedCount = 0
        lastEventTimestamp = nil
        lastMoveTimestamp = 0
        lastMovePoint = nil
        isRecording = true
        eventTap = tap

        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        runLoopSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
    }

    func stop() -> [MacroAction] {
        removeEventTap()
        let result = recordedActions
        recordedActions.removeAll()
        recordedCount = 0
        recordingTaskID = nil
        isRecording = false
        return result
    }

    func cancel() {
        _ = stop()
    }

    fileprivate func capture(type: CGEventType, event: CGEvent) {
        guard isRecording else { return }
        guard
            NSWorkspace.shared.frontmostApplication?.processIdentifier
                != ownProcessID
        else {
            return
        }
        if isPointerOverKeyToolWindow() {
            return
        }

        let action: MacroAction?
        switch type {
        case .keyDown:
            guard
                event.getIntegerValueField(.keyboardEventAutorepeat) == 0,
                let keyboardEvent = NSEvent(cgEvent: event)
            else {
                return
            }
            action = keyboardAction(from: keyboardEvent)
        case .leftMouseDown:
            action = mouseClickAction(button: .left, event: event)
        case .rightMouseDown:
            action = mouseClickAction(button: .right, event: event)
        case .otherMouseDown:
            action = mouseClickAction(button: .center, event: event)
        case .scrollWheel:
            action = scrollAction(from: event)
        case .mouseMoved:
            action = mouseMoveAction(from: event)
        default:
            action = nil
        }

        guard let action else { return }
        appendWithDelay(action)
    }

    private func keyboardAction(from event: NSEvent) -> MacroAction? {
        let modifierOnlyKeys: Set<UInt16> = [
            54, 55, 56, 57, 58, 59, 60, 61, 62, 63
        ]
        guard !modifierOnlyKeys.contains(event.keyCode) else {
            return nil
        }

        return .keyboard(
            KeyboardAction(
                keyCode: event.keyCode,
                modifiers: UInt64(
                    event.modifierFlags
                        .intersection([.control, .option, .shift, .command])
                        .rawValue
                ),
                keyLabel: KeyCodeLabel.label(
                    for: event.keyCode,
                    characters: event.charactersIgnoringModifiers
                )
            )
        )
    }

    private func mouseClickAction(
        button: MouseButtonKind,
        event: CGEvent
    ) -> MacroAction {
        let point = event.location
        return .mouseClick(
            MouseClickAction(x: point.x, y: point.y, button: button)
        )
    }

    private func scrollAction(from event: CGEvent) -> MacroAction? {
        guard let nativeEvent = NSEvent(cgEvent: event) else { return nil }
        let deltaX = Int32(nativeEvent.scrollingDeltaX.rounded())
        let deltaY = Int32(nativeEvent.scrollingDeltaY.rounded())
        guard deltaX != 0 || deltaY != 0 else { return nil }
        return .scroll(ScrollAction(deltaX: deltaX, deltaY: deltaY))
    }

    private func mouseMoveAction(from event: CGEvent) -> MacroAction? {
        let timestamp = ProcessInfo.processInfo.systemUptime
        guard timestamp - lastMoveTimestamp >= 0.20 else {
            return nil
        }

        let point = event.location
        if let lastMovePoint {
            let distance = hypot(point.x - lastMovePoint.x, point.y - lastMovePoint.y)
            guard distance >= 10 else { return nil }
        }

        lastMoveTimestamp = timestamp
        lastMovePoint = point
        return .mouseMove(MouseMoveAction(x: point.x, y: point.y))
    }

    private func appendWithDelay(_ action: MacroAction) {
        let timestamp = ProcessInfo.processInfo.systemUptime
        if let lastEventTimestamp {
            let elapsed = timestamp - lastEventTimestamp
            if elapsed >= 0.05 {
                recordedActions.append(
                    .delay(DelayAction(seconds: min(max(elapsed, 0.05), 3_600)))
                )
            }
        }

        recordedActions.append(action)
        lastEventTimestamp = timestamp
        recordedCount = recordedActions.count
    }

    private func isPointerOverKeyToolWindow() -> Bool {
        let point = NSEvent.mouseLocation
        return NSApp.windows.contains {
            $0.isVisible && $0.frame.contains(point)
        }
    }

    private func removeEventTap() {
        if let runLoopSource {
            CFRunLoopRemoveSource(
                CFRunLoopGetMain(),
                runLoopSource,
                .commonModes
            )
        }
        if let eventTap {
            CGEvent.tapEnable(tap: eventTap, enable: false)
            CFMachPortInvalidate(eventTap)
        }
        runLoopSource = nil
        eventTap = nil
    }
}
