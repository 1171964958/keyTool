import AppKit
import SwiftUI

struct ShortcutSection: View {
    @Binding var task: MacroTask
    @ObservedObject var hotKeyManager: GlobalHotKeyManager

    var body: some View {
        InspectorCard(title: "快捷控制", icon: "keyboard.badge.ellipsis") {
            ShortcutRecorderRow(
                title: "启动任务",
                icon: "play.circle",
                tint: .indigo,
                shortcut: task.shortcuts.start,
                error: hotKeyManager.error(for: task.id, kind: .start),
                onChange: { task.shortcuts.start = $0 }
            )

            Divider()

            ShortcutRecorderRow(
                title: "停止任务",
                icon: "stop.circle",
                tint: .red,
                shortcut: task.shortcuts.stop,
                error: hotKeyManager.error(for: task.id, kind: .stop),
                onChange: { task.shortcuts.stop = $0 }
            )

            Text(
                "支持 ⌃ Control、⌥ Option、⇧ Shift、⌘ Command。"
                    + "快捷键在 KeyTool 位于后台时仍然生效，且不能与其他任务重复。"
            )
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }
}

private struct ShortcutRecorderRow: View {
    let title: String
    let icon: String
    let tint: Color
    let shortcut: GlobalShortcut?
    let error: String?
    let onChange: (GlobalShortcut?) -> Void

    @StateObject private var state = ShortcutRecorderState()

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 12) {
                Label(title, systemImage: icon)
                    .foregroundStyle(tint)
                    .frame(minWidth: 110, alignment: .leading)

                Text(shortcut?.displayName ?? "未设置")
                    .font(.system(.body, design: .monospaced).weight(.semibold))
                    .foregroundStyle(shortcut == nil ? .secondary : .primary)
                    .frame(minWidth: 100, minHeight: 32)
                    .padding(.horizontal, 12)
                    .background(
                        Color.primary.opacity(0.055),
                        in: RoundedRectangle(cornerRadius: 8)
                    )

                Button(state.isRecording ? "请按快捷键…" : "录制") {
                    state.isRecording ? cancelRecording() : startRecording()
                }
                .buttonStyle(.bordered)
                .tint(state.isRecording ? .orange : tint)

                if state.isRecording {
                    Button("取消", action: cancelRecording)
                        .buttonStyle(.bordered)
                } else if shortcut != nil {
                    Button {
                        onChange(nil)
                    } label: {
                        Label("清除", systemImage: "xmark")
                    }
                    .buttonStyle(.bordered)
                }

                Spacer()
            }

            if let message = state.validationMessage ?? error {
                Label(message, systemImage: "exclamationmark.triangle.fill")
                    .font(.caption)
                    .foregroundStyle(.red)
            }
        }
        .onDisappear(perform: cancelRecording)
    }

    private func startRecording() {
        guard state.monitor == nil else { return }
        state.validationMessage = nil
        state.isRecording = true
        state.monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
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
            guard !flags.isEmpty else {
                state.validationMessage = "请至少按下一个修饰键（⌃ ⌥ ⇧ ⌘）。"
                cancelRecording()
                return nil
            }

            onChange(
                GlobalShortcut(
                    keyCode: UInt32(event.keyCode),
                    modifiers: UInt64(flags.rawValue),
                    keyLabel: keyLabel(for: event)
                )
            )
            cancelRecording()
            return nil
        }
    }

    private func cancelRecording() {
        if let monitor = state.monitor {
            NSEvent.removeMonitor(monitor)
        }
        state.monitor = nil
        state.isRecording = false
    }

    private func keyLabel(for event: NSEvent) -> String {
        KeyCodeLabel.label(
            for: event.keyCode,
            characters: event.charactersIgnoringModifiers
        )
    }
}

private final class ShortcutRecorderState: ObservableObject {
    @Published var isRecording = false
    @Published var validationMessage: String?
    var monitor: Any?

    deinit {
        if let monitor {
            NSEvent.removeMonitor(monitor)
        }
    }
}
