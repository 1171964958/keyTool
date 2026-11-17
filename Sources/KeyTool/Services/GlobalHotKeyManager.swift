import AppKit
import Carbon
import Combine
import SwiftUI

enum ShortcutKind: String, CaseIterable {
    case start
    case stop

    var label: String {
        switch self {
        case .start: "启动"
        case .stop: "停止"
        }
    }
}

struct HotKeySlot: Hashable {
    let taskID: UUID
    let kind: ShortcutKind
}

@MainActor
final class GlobalHotKeyManager: ObservableObject {
    @Published private(set) var registrationErrors: [HotKeySlot: String] = [:]

    private struct BindingRequest: Equatable {
        let slot: HotKeySlot
        let shortcut: GlobalShortcut
    }

    private let store: TaskStore
    private let runner: MacroRunner
    private var taskObservation: AnyCancellable?
    private var eventHandler: EventHandlerRef?
    private var registrations: [HotKeySlot: EventHotKeyRef] = [:]
    private var slotsByID: [UInt32: HotKeySlot] = [:]
    private var nextID: UInt32 = 1
    private var lastRequests: [BindingRequest] = []

    private let signature: OSType = 0x4B_59_54_4C // KYTL

    init(store: TaskStore, runner: MacroRunner) {
        self.store = store
        self.runner = runner
    }

    func start() {
        guard taskObservation == nil else { return }
        installEventHandler()
        taskObservation = store.$tasks.sink { [weak self] tasks in
            self?.synchronize(tasks)
        }
    }

    func error(for taskID: UUID, kind: ShortcutKind) -> String? {
        registrationErrors[HotKeySlot(taskID: taskID, kind: kind)]
    }

    private func synchronize(_ tasks: [MacroTask]) {
        let requests = tasks.flatMap { task -> [BindingRequest] in
            var requests: [BindingRequest] = []
            if let shortcut = task.shortcuts.start {
                requests.append(
                    BindingRequest(
                        slot: HotKeySlot(taskID: task.id, kind: .start),
                        shortcut: shortcut
                    )
                )
            }
            if let shortcut = task.shortcuts.stop {
                requests.append(
                    BindingRequest(
                        slot: HotKeySlot(taskID: task.id, kind: .stop),
                        shortcut: shortcut
                    )
                )
            }
            return requests
        }

        guard requests != lastRequests else { return }
        lastRequests = requests
        unregisterAll()
        registrationErrors.removeAll()

        var shortcutOwners: [GlobalShortcut: HotKeySlot] = [:]
        for request in requests {
            guard request.shortcut.hasModifier else {
                registrationErrors[request.slot] = "快捷键至少需要一个修饰键。"
                continue
            }
            if shortcutOwners[request.shortcut] != nil {
                registrationErrors[request.slot] = "该快捷键已被其他任务使用。"
                continue
            }

            shortcutOwners[request.shortcut] = request.slot
            register(request)
        }
    }

    private func register(_ request: BindingRequest) {
        let id = nextID
        nextID += 1
        var hotKeyRef: EventHotKeyRef?
        let hotKeyID = EventHotKeyID(signature: signature, id: id)
        let status = RegisterEventHotKey(
            request.shortcut.keyCode,
            carbonModifiers(from: request.shortcut.modifiers),
            hotKeyID,
            GetApplicationEventTarget(),
            0,
            &hotKeyRef
        )

        guard status == noErr, let hotKeyRef else {
            registrationErrors[request.slot] = status == eventHotKeyExistsErr
                ? "该快捷键已被系统或其他应用占用。"
                : "快捷键注册失败（错误码 \(status)）。"
            return
        }

        registrations[request.slot] = hotKeyRef
        slotsByID[id] = request.slot
        GlobalHotKeyDispatcher.shared.setHandler(for: id) { [weak self] in
            self?.handle(request.slot)
        }
    }

    private func handle(_ slot: HotKeySlot) {
        guard let task = store.tasks.first(where: { $0.id == slot.taskID }) else {
            return
        }

        switch slot.kind {
        case .start:
            runner.start(task)
        case .stop:
            runner.stop(task.id)
        }
    }

    private func installEventHandler() {
        guard eventHandler == nil else { return }
        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed)
        )
        InstallEventHandler(
            GetApplicationEventTarget(),
            { _, event, _ -> OSStatus in
                var hotKeyID = EventHotKeyID()
                let status = GetEventParameter(
                    event,
                    EventParamName(kEventParamDirectObject),
                    EventParamType(typeEventHotKeyID),
                    nil,
                    MemoryLayout<EventHotKeyID>.size,
                    nil,
                    &hotKeyID
                )
                if status == noErr {
                    GlobalHotKeyDispatcher.shared.dispatch(hotKeyID.id)
                }
                return noErr
            },
            1,
            &eventType,
            nil,
            &eventHandler
        )
    }

    private func unregisterAll() {
        for hotKeyRef in registrations.values {
            UnregisterEventHotKey(hotKeyRef)
        }
        registrations.removeAll()
        slotsByID.removeAll()
        GlobalHotKeyDispatcher.shared.removeAllHandlers()
    }

    private func carbonModifiers(from rawValue: UInt64) -> UInt32 {
        let flags = NSEvent.ModifierFlags(rawValue: UInt(rawValue))
        var modifiers: UInt32 = 0
        if flags.contains(.command) { modifiers |= UInt32(cmdKey) }
        if flags.contains(.option) { modifiers |= UInt32(optionKey) }
        if flags.contains(.control) { modifiers |= UInt32(controlKey) }
        if flags.contains(.shift) { modifiers |= UInt32(shiftKey) }
        return modifiers
    }
}

private final class GlobalHotKeyDispatcher {
    static let shared = GlobalHotKeyDispatcher()

    private var handlers: [UInt32: () -> Void] = [:]

    func setHandler(for id: UInt32, handler: @escaping () -> Void) {
        handlers[id] = handler
    }

    func dispatch(_ id: UInt32) {
        DispatchQueue.main.async { [weak self] in
            self?.handlers[id]?()
        }
    }

    func removeAllHandlers() {
        handlers.removeAll()
    }
}
