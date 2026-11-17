import AppKit
import CoreGraphics
import Foundation

enum InputEngine {
    enum InputError: LocalizedError {
        case targetUnavailable
        case eventCreationFailed

        var errorDescription: String? {
            switch self {
            case .targetUnavailable:
                "目标应用已退出，请重新选择目标应用。"
            case .eventCreationFailed:
                "无法创建输入事件，请检查辅助功能权限。"
            }
        }
    }

    static func perform(_ action: MacroAction, to processID: pid_t) async throws {
        try Task.checkCancellation()
        guard NSRunningApplication(processIdentifier: processID)?.isTerminated == false else {
            throw InputError.targetUnavailable
        }

        switch action {
        case .keyboard(let action):
            try await performKeyboard(action, processID: processID)
        case .mouseClick(let action):
            try await performMouseClick(action, processID: processID)
        case .mouseMove(let action):
            try performMouseMove(action, processID: processID)
        case .scroll(let action):
            try performScroll(action, processID: processID)
        case .delay(let action):
            let nanoseconds = UInt64(max(0, action.seconds) * 1_000_000_000)
            try await Task.sleep(nanoseconds: nanoseconds)
        }
    }

    private static func performKeyboard(
        _ action: KeyboardAction,
        processID: pid_t
    ) async throws {
        let source = CGEventSource(stateID: .combinedSessionState)
        let flags = CGEventFlags(rawValue: action.modifiers)

        guard
            let keyDown = CGEvent(
                keyboardEventSource: source,
                virtualKey: action.keyCode,
                keyDown: true
            ),
            let keyUp = CGEvent(
                keyboardEventSource: source,
                virtualKey: action.keyCode,
                keyDown: false
            )
        else {
            throw InputError.eventCreationFailed
        }

        keyDown.flags = flags
        keyUp.flags = flags
        keyDown.postToPid(processID)
        try await Task.sleep(nanoseconds: 12_000_000)
        keyUp.postToPid(processID)
    }

    private static func performMouseClick(
        _ action: MouseClickAction,
        processID: pid_t
    ) async throws {
        let point = CGPoint(x: action.x, y: action.y)
        let source = CGEventSource(stateID: .combinedSessionState)
        let (button, downType, upType) = mouseTypes(for: action.button)

        guard
            let down = CGEvent(
                mouseEventSource: source,
                mouseType: downType,
                mouseCursorPosition: point,
                mouseButton: button
            ),
            let up = CGEvent(
                mouseEventSource: source,
                mouseType: upType,
                mouseCursorPosition: point,
                mouseButton: button
            )
        else {
            throw InputError.eventCreationFailed
        }

        down.setIntegerValueField(.mouseEventClickState, value: 1)
        up.setIntegerValueField(.mouseEventClickState, value: 1)
        down.postToPid(processID)
        try await Task.sleep(nanoseconds: 20_000_000)
        up.postToPid(processID)
    }

    private static func performMouseMove(
        _ action: MouseMoveAction,
        processID: pid_t
    ) throws {
        let source = CGEventSource(stateID: .combinedSessionState)
        guard let event = CGEvent(
            mouseEventSource: source,
            mouseType: .mouseMoved,
            mouseCursorPosition: CGPoint(x: action.x, y: action.y),
            mouseButton: .left
        ) else {
            throw InputError.eventCreationFailed
        }
        event.postToPid(processID)
    }

    private static func performScroll(
        _ action: ScrollAction,
        processID: pid_t
    ) throws {
        guard let event = CGEvent(
            scrollWheelEvent2Source: CGEventSource(stateID: .combinedSessionState),
            units: .pixel,
            wheelCount: 2,
            wheel1: action.deltaY,
            wheel2: action.deltaX,
            wheel3: 0
        ) else {
            throw InputError.eventCreationFailed
        }
        event.postToPid(processID)
    }

    private static func mouseTypes(
        for button: MouseButtonKind
    ) -> (CGMouseButton, CGEventType, CGEventType) {
        switch button {
        case .left:
            return (.left, .leftMouseDown, .leftMouseUp)
        case .right:
            return (.right, .rightMouseDown, .rightMouseUp)
        case .center:
            return (.center, .otherMouseDown, .otherMouseUp)
        }
    }
}
