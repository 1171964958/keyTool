import AppKit
import CoreGraphics
import SwiftUI

struct ActionInspector: View {
    @Binding var action: MacroAction

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("动作设置")
                .font(.subheadline.weight(.semibold))

            switch action {
            case .keyboard(let keyboard):
                KeyComboEditor(keyboardAction: keyboard) { updated in
                    action = .keyboard(updated)
                }
            case .mouseClick(let click):
                mouseClickEditor(click)
            case .mouseMove(let move):
                mouseMoveEditor(move)
            case .scroll(let scroll):
                scrollEditor(scroll)
            case .delay(let delay):
                delayEditor(delay)
            }
        }
    }

    private func mouseClickEditor(_ click: MouseClickAction) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 12) {
                coordinateFields(
                    x: Binding(
                        get: { click.x },
                        set: {
                            var updated = click
                            updated.x = $0
                            action = .mouseClick(updated)
                        }
                    ),
                    y: Binding(
                        get: { click.y },
                        set: {
                            var updated = click
                            updated.y = $0
                            action = .mouseClick(updated)
                        }
                    )
                )

                Picker(
                    "按键",
                    selection: Binding(
                        get: { click.button },
                        set: {
                            var updated = click
                            updated.button = $0
                            action = .mouseClick(updated)
                        }
                    )
                ) {
                    ForEach(MouseButtonKind.allCases) { button in
                        Text("\(button.label)键").tag(button)
                    }
                }
                .frame(width: 110)

                Button("使用当前鼠标位置") {
                    var updated = click
                    let point = CGEvent(source: nil)?.location ?? .zero
                    updated.x = point.x
                    updated.y = point.y
                    action = .mouseClick(updated)
                }
                .buttonStyle(.bordered)
            }
            Text("坐标为屏幕全局坐标，原点位于主屏幕左上角。")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private func mouseMoveEditor(_ move: MouseMoveAction) -> some View {
        HStack(spacing: 12) {
            coordinateFields(
                x: Binding(
                    get: { move.x },
                    set: {
                        var updated = move
                        updated.x = $0
                        action = .mouseMove(updated)
                    }
                ),
                y: Binding(
                    get: { move.y },
                    set: {
                        var updated = move
                        updated.y = $0
                        action = .mouseMove(updated)
                    }
                )
            )

            Button("使用当前鼠标位置") {
                var updated = move
                let point = CGEvent(source: nil)?.location ?? .zero
                updated.x = point.x
                updated.y = point.y
                action = .mouseMove(updated)
            }
            .buttonStyle(.bordered)
        }
    }

    private func scrollEditor(_ scroll: ScrollAction) -> some View {
        HStack(spacing: 18) {
            Stepper(
                "水平增量：\(scroll.deltaX)",
                value: Binding(
                    get: { scroll.deltaX },
                    set: {
                        var updated = scroll
                        updated.deltaX = $0
                        action = .scroll(updated)
                    }
                ),
                in: -5_000...5_000,
                step: 20
            )

            Stepper(
                "垂直增量：\(scroll.deltaY)",
                value: Binding(
                    get: { scroll.deltaY },
                    set: {
                        var updated = scroll
                        updated.deltaY = $0
                        action = .scroll(updated)
                    }
                ),
                in: -5_000...5_000,
                step: 20
            )
        }
    }

    private func delayEditor(_ delay: DelayAction) -> some View {
        Stepper(
            value: Binding(
                get: { delay.seconds },
                set: {
                    var updated = delay
                    updated.seconds = $0
                    action = .delay(updated)
                }
            ),
            in: 0.05...3_600,
            step: 0.05
        ) {
            Text(String(format: "等待 %.2f 秒", delay.seconds))
                .monospacedDigit()
        }
        .frame(maxWidth: 260, alignment: .leading)
    }

    private func coordinateFields(
        x: Binding<Double>,
        y: Binding<Double>
    ) -> some View {
        HStack(spacing: 8) {
            LabeledContent("X") {
                TextField("X", value: x, format: .number.precision(.fractionLength(0)))
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 72)
            }
            LabeledContent("Y") {
                TextField("Y", value: y, format: .number.precision(.fractionLength(0)))
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 72)
            }
        }
        .labelStyle(.titleAndIcon)
    }
}

private struct KeyComboEditor: View {
    let keyboardAction: KeyboardAction
    let onChange: (KeyboardAction) -> Void

    @StateObject private var recorder = KeyRecorderState()

    var body: some View {
        HStack(spacing: 12) {
            Text(keyboardAction.displayName)
                .font(.system(size: 16, weight: .semibold, design: .rounded))
                .frame(minWidth: 110, minHeight: 34)
                .padding(.horizontal, 12)
                .background(
                    Color.primary.opacity(0.055),
                    in: RoundedRectangle(cornerRadius: 8)
                )
                .overlay {
                    RoundedRectangle(cornerRadius: 8)
                        .stroke(recorder.isRecording ? Color.indigo : Color.primary.opacity(0.10))
                }
                .accessibilityLabel("当前按键组合 \(keyboardAction.displayName)")

            Button(recorder.isRecording ? "请按快捷键…" : "录入快捷键") {
                recorder.isRecording ? cancelRecording() : startRecording()
            }
            .buttonStyle(.borderedProminent)
            .tint(recorder.isRecording ? .orange : .indigo)

            if recorder.isRecording {
                Button("取消", action: cancelRecording)
                    .buttonStyle(.borderless)
            }

            Spacer()

            Text("可同时按下 ⌃ ⌥ ⇧ ⌘")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .onDisappear(perform: cancelRecording)
    }

    private func startRecording() {
        guard recorder.monitor == nil else { return }
        recorder.isRecording = true
        recorder.monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.keyCode == 53 {
                cancelRecording()
                return nil
            }

            let relevantFlags: NSEvent.ModifierFlags = [
                .control,
                .option,
                .shift,
                .command
            ]
            let flags = event.modifierFlags.intersection(relevantFlags)
            var updated = keyboardAction
            updated.keyCode = event.keyCode
            updated.modifiers = UInt64(flags.rawValue)
            updated.keyLabel = keyLabel(for: event)
            onChange(updated)
            cancelRecording()
            return nil
        }
    }

    private func cancelRecording() {
        if let monitor = recorder.monitor {
            NSEvent.removeMonitor(monitor)
        }
        recorder.monitor = nil
        recorder.isRecording = false
    }

    private func keyLabel(for event: NSEvent) -> String {
        KeyCodeLabel.label(
            for: event.keyCode,
            characters: event.charactersIgnoringModifiers
        )
    }
}

private final class KeyRecorderState: ObservableObject {
    @Published var isRecording = false
    var monitor: Any?

    deinit {
        if let monitor {
            NSEvent.removeMonitor(monitor)
        }
    }
}

extension KeyboardAction {
    static let defaultKeyboardAction = KeyboardAction(
        keyCode: 0,
        modifiers: UInt64(NSEvent.ModifierFlags.command.rawValue),
        keyLabel: "A"
    )
}

extension MouseClickAction {
    static func atCurrentPosition(button: MouseButtonKind) -> MouseClickAction {
        let point = CGEvent(source: nil)?.location ?? .zero
        return MouseClickAction(x: point.x, y: point.y, button: button)
    }
}

extension MouseMoveAction {
    static var atCurrentPosition: MouseMoveAction {
        let point = CGEvent(source: nil)?.location ?? .zero
        return MouseMoveAction(x: point.x, y: point.y)
    }
}
