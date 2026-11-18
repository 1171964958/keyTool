import AppKit
import CoreGraphics
import SwiftUI

struct ActionSection: View {
    let taskID: UUID
    @Binding var actions: [MacroAction]
    @Binding var selectedActionID: UUID?
    @ObservedObject var recorder: ActionRecorder

    var body: some View {
        InspectorCard(title: "动作序列", icon: "list.number") {
            recordingBar

            Divider()

            if actions.isEmpty {
                ContentUnavailableView {
                    Label("没有动作", systemImage: "rectangle.stack")
                } description: {
                    Text("添加按键、鼠标或等待动作。")
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 12)
            } else {
                VStack(spacing: 7) {
                    ForEach(Array(actions.enumerated()), id: \.element.id) { index, action in
                        ActionRow(
                            index: index,
                            action: action,
                            isSelected: selectedActionID == action.id,
                            select: { selectedActionID = action.id },
                            moveUp: index == 0 ? nil : { move(from: index, to: index - 1) },
                            moveDown: index == actions.count - 1
                                ? nil
                                : { move(from: index, to: index + 1) },
                            delete: { remove(action.id) }
                        )
                    }
                }
            }

            actionEditor
        }
    }

    private var recordingBar: some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack(spacing: 10) {
                if recorder.isRecording {
                    Label(
                        "正在录制 · \(recorder.recordedCount) 个操作",
                        systemImage: "record.circle"
                    )
                    .foregroundStyle(.red)
                    .font(.callout.weight(.semibold))
                } else {
                    Label("将键盘、鼠标和等待时间追加到动作序列", systemImage: "record.circle")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }

                Spacer()

                HStack(spacing: 8) {
                    addActionMenu

                    if recorder.isRecording {
                        if recorder.recordingTaskID == taskID {
                            Button("停止并保存") {
                                let recordedActions = recorder.stop()
                                actions.append(contentsOf: recordedActions)
                                selectedActionID = recordedActions.last?.id
                            }
                            .buttonStyle(.borderedProminent)
                            .tint(.red)
                        } else {
                            Text("正在录制其他任务")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    } else {
                        Button("录制操作") {
                            recorder.start(taskID: taskID)
                        }
                        .buttonStyle(.borderedProminent)
                    }
                }
            }

            if let errorMessage = recorder.errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle.fill")
                    .font(.caption)
                    .foregroundStyle(.red)
            }
        }
    }

    private var addActionMenu: some View {
        Menu {
            Button("按键组合") { append(.keyboard(.defaultKeyboardAction)) }
            Button("鼠标点击") { append(.mouseClick(.atCurrentPosition(button: .left))) }
            Button("鼠标移动") { append(.mouseMove(.atCurrentPosition)) }
            Button("鼠标滚动") {
                append(.scroll(ScrollAction(deltaX: 0, deltaY: -120)))
            }
            Divider()
            Button("等待") {
                append(.delay(DelayAction(seconds: 0.5)))
            }
        } label: {
            Label("添加动作", systemImage: "plus")
                .font(.callout.weight(.semibold))
                .foregroundStyle(.indigo)
                .padding(.horizontal, 11)
                .frame(height: 30)
                .background(
                    Color.indigo.opacity(0.09),
                    in: RoundedRectangle(cornerRadius: 8)
                )
                .overlay {
                    RoundedRectangle(cornerRadius: 8)
                        .stroke(Color.indigo.opacity(0.16))
                }
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .accessibilityLabel("添加动作")
    }

    @ViewBuilder
    private var actionEditor: some View {
        if
            let selectedActionID,
            let index = actions.firstIndex(where: { $0.id == selectedActionID })
        {
            Divider()

            VStack(alignment: .leading, spacing: 12) {
                HStack(spacing: 8) {
                    Label("动作设置", systemImage: "slider.horizontal.3")
                        .font(.subheadline.weight(.semibold))

                    Text(actions[index].title)
                        .font(.caption.weight(.medium))
                        .foregroundStyle(.indigo)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3)
                        .background(Color.indigo.opacity(0.09), in: Capsule())

                    Spacer()
                }

                ActionInspector(action: $actions[index])
            }
            .keyToolInsetPanel()
        }
    }

    private func append(_ action: MacroAction) {
        actions.append(action)
        selectedActionID = action.id
    }

    private func remove(_ id: UUID) {
        actions.removeAll { $0.id == id }
        if selectedActionID == id {
            selectedActionID = actions.first?.id
        }
    }

    private func move(from source: Int, to destination: Int) {
        guard actions.indices.contains(source), actions.indices.contains(destination) else {
            return
        }
        let action = actions.remove(at: source)
        actions.insert(action, at: destination)
    }
}

private struct ActionRow: View {
    let index: Int
    let action: MacroAction
    let isSelected: Bool
    let select: () -> Void
    let moveUp: (() -> Void)?
    let moveDown: (() -> Void)?
    let delete: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Text("\(index + 1)")
                .font(.caption2.monospacedDigit().weight(.semibold))
                .foregroundStyle(.secondary)
                .frame(width: 22, height: 22)
                .background(Color.primary.opacity(0.04), in: Capsule())

            Image(systemName: icon)
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(isSelected ? Color.white : Color.indigo)
                .frame(width: 30, height: 30)
                .background(
                    isSelected ? Color.indigo : Color.indigo.opacity(0.09),
                    in: RoundedRectangle(cornerRadius: 8)
                )
                .accessibilityHidden(true)

            VStack(alignment: .leading, spacing: 2) {
                Text(action.title)
                    .font(.callout.weight(.medium))
                Text(action.summary)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            Button(action: select) {
                Label(
                    isSelected ? "编辑中" : "编辑",
                    systemImage: "slider.horizontal.3"
                )
                .font(.caption.weight(.medium))
            }
            .buttonStyle(.bordered)
            .tint(.indigo)
            .controlSize(.small)
            .accessibilityLabel(isSelected ? "当前正在编辑的动作" : "编辑动作")

            Button {
                moveUp?()
            } label: {
                Image(systemName: "chevron.up")
            }
            .buttonStyle(KeyToolIconButtonStyle(tint: .secondary))
            .disabled(moveUp == nil)
            .accessibilityLabel("上移动作")

            Button {
                moveDown?()
            } label: {
                Image(systemName: "chevron.down")
            }
            .buttonStyle(KeyToolIconButtonStyle(tint: .secondary))
            .disabled(moveDown == nil)
            .accessibilityLabel("下移动作")

            Button(action: delete) {
                Image(systemName: "trash")
            }
            .buttonStyle(KeyToolIconButtonStyle(tint: .red))
            .accessibilityLabel("删除动作")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 7)
        .frame(minHeight: 54)
        .background(
            rowBackground,
            in: RoundedRectangle(cornerRadius: 11)
        )
        .overlay {
            RoundedRectangle(cornerRadius: 11)
                .stroke(
                    isSelected
                        ? Color.indigo.opacity(0.28)
                        : Color.primary.opacity(0.055)
                )
        }
        .contentShape(Rectangle())
        .onTapGesture(perform: select)
    }

    private var rowBackground: Color {
        if isSelected {
            return Color.indigo.opacity(0.095)
        }
        return Color.primary.opacity(0.025)
    }

    private var icon: String {
        switch action {
        case .keyboard: "keyboard"
        case .mouseClick: "cursorarrow.click"
        case .mouseMove: "cursorarrow.motionlines"
        case .scroll: "scroll"
        case .delay: "timer"
        }
    }
}
