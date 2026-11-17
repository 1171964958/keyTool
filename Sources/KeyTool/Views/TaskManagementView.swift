import SwiftUI

struct TaskManagementView: View {
    @ObservedObject var store: TaskStore
    @ObservedObject var runner: MacroRunner
    @ObservedObject var hotKeyManager: GlobalHotKeyManager
    let onCreate: () -> Void
    let onEdit: (UUID) -> Void

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()

            if store.tasks.isEmpty {
                emptyState
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 18) {
                        summaryGrid

                        if let errorMessage = runner.errorMessage {
                            ManagementErrorBanner(message: errorMessage) {
                                runner.errorMessage = nil
                            }
                        }

                        HStack {
                            Text("全部任务")
                                .font(.title3.weight(.semibold))
                            Text("\(store.tasks.count)")
                                .font(.caption.monospacedDigit())
                                .foregroundStyle(.secondary)
                                .padding(.horizontal, 7)
                                .padding(.vertical, 2)
                                .background(
                                    Color.primary.opacity(0.06),
                                    in: Capsule()
                                )
                            Spacer()
                        }

                        LazyVStack(spacing: 10) {
                            ForEach(store.tasks) { task in
                                TaskManagementRow(
                                    task: task,
                                    progress: runner.progress(for: task.id),
                                    startError: hotKeyManager.error(
                                        for: task.id,
                                        kind: .start
                                    ),
                                    stopError: hotKeyManager.error(
                                        for: task.id,
                                        kind: .stop
                                    ),
                                    onRun: {
                                        if runner.isRunning(task.id) {
                                            runner.stop(task.id)
                                        } else {
                                            runner.start(task)
                                        }
                                    },
                                    onEdit: { onEdit(task.id) },
                                    onDuplicate: {
                                        store.duplicateTask(task)
                                    },
                                    onDelete: {
                                        store.deleteTask(task)
                                    }
                                )
                            }
                        }
                    }
                    .padding(24)
                    .frame(maxWidth: 980, alignment: .leading)
                }
            }
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .navigationTitle("任务管理")
    }

    private var header: some View {
        HStack(spacing: 16) {
            VStack(alignment: .leading, spacing: 4) {
                Text("任务管理")
                    .font(.system(size: 28, weight: .semibold))
                Text("集中查看执行状态、快捷控制与任务配置")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            Button(action: onCreate) {
                Label("新建任务", systemImage: "plus")
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
        }
        .padding(.horizontal, 24)
        .padding(.vertical, 16)
        .background(.regularMaterial)
    }

    private var emptyState: some View {
        ContentUnavailableView {
            Label("还没有任务", systemImage: "square.grid.2x2")
        } description: {
            Text("新建一个任务，设置动作序列和全局快捷键。")
        } actions: {
            Button("新建任务", action: onCreate)
                .buttonStyle(.borderedProminent)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var summaryGrid: some View {
        LazyVGrid(
            columns: [GridItem(.adaptive(minimum: 145), spacing: 12)],
            spacing: 12
        ) {
            SummaryMetric(
                title: "任务总数",
                value: "\(store.tasks.count)",
                icon: "square.stack.3d.up",
                tint: .indigo
            )
            SummaryMetric(
                title: "正在执行",
                value: "\(runner.progressByTask.count)",
                icon: "waveform.path.ecg",
                tint: .orange
            )
            SummaryMetric(
                title: "自动计划",
                value: "\(store.tasks.filter(\.isScheduleEnabled).count)",
                icon: "calendar.badge.clock",
                tint: .teal
            )
            SummaryMetric(
                title: "已设快捷键",
                value: "\(shortcutTaskCount)",
                icon: "command",
                tint: .purple
            )
        }
    }

    private var shortcutTaskCount: Int {
        store.tasks.filter {
            $0.shortcuts.start != nil || $0.shortcuts.stop != nil
        }.count
    }
}

private struct ManagementErrorBanner: View {
    let message: String
    let dismiss: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.red)
                .accessibilityHidden(true)
            Text(message)
                .font(.callout)
            Spacer()
            Button(action: dismiss) {
                Image(systemName: "xmark")
            }
            .buttonStyle(.borderless)
            .accessibilityLabel("关闭错误提示")
        }
        .padding(12)
        .background(Color.red.opacity(0.08), in: RoundedRectangle(cornerRadius: 10))
    }
}

private struct SummaryMetric: View {
    let title: String
    let value: String
    let icon: String
    let tint: Color

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: icon)
                .font(.title3)
                .foregroundStyle(tint)
                .frame(width: 34, height: 34)
                .background(tint.opacity(0.10), in: RoundedRectangle(cornerRadius: 9))
                .accessibilityHidden(true)

            VStack(alignment: .leading, spacing: 1) {
                Text(value)
                    .font(.title3.monospacedDigit().weight(.semibold))
                Text(title)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .background(
            Color(nsColor: .controlBackgroundColor),
            in: RoundedRectangle(cornerRadius: 12)
        )
        .overlay {
            RoundedRectangle(cornerRadius: 12)
                .stroke(Color.primary.opacity(0.07))
        }
        .accessibilityElement(children: .combine)
    }
}

private struct TaskManagementRow: View {
    let task: MacroTask
    let progress: RunProgress?
    let startError: String?
    let stopError: String?
    let onRun: () -> Void
    let onEdit: () -> Void
    let onDuplicate: () -> Void
    let onDelete: () -> Void

    private var isRunning: Bool { progress != nil }

    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .top, spacing: 14) {
                statusIcon

                VStack(alignment: .leading, spacing: 8) {
                    HStack(spacing: 8) {
                        Text(task.name.isEmpty ? "未命名任务" : task.name)
                            .font(.headline)
                            .lineLimit(1)

                        if isRunning {
                            Text("执行中")
                                .font(.caption2.weight(.semibold))
                                .foregroundStyle(.orange)
                                .padding(.horizontal, 7)
                                .padding(.vertical, 2)
                                .background(Color.orange.opacity(0.12), in: Capsule())
                        }
                    }

                    HStack(spacing: 8) {
                        TaskMetadataTag(
                            icon: "app",
                            text: task.target?.name ?? "未选择目标"
                        )
                        TaskMetadataTag(
                            icon: "list.number",
                            text: "\(task.actions.count) 个动作"
                        )
                        TaskMetadataTag(
                            icon: task.isLoopInfinite ? "infinity" : "repeat",
                            text: task.isLoopInfinite
                                ? "无限循环"
                                : "\(task.loopCount) 次"
                        )
                        TaskMetadataTag(icon: "clock", text: scheduleLabel)
                    }

                    shortcutRow
                }

                Spacer(minLength: 12)

                HStack(spacing: 8) {
                    Button(action: onRun) {
                        Label(
                            isRunning ? "停止" : "运行",
                            systemImage: isRunning ? "stop.fill" : "play.fill"
                        )
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(isRunning ? .red : .indigo)
                    .accessibilityLabel(isRunning ? "停止任务" : "运行任务")

                    Button(action: onEdit) {
                        Label("编辑", systemImage: "slider.horizontal.3")
                    }
                    .buttonStyle(.bordered)

                    Menu {
                        Button("编辑任务", action: onEdit)
                        Button("复制任务", action: onDuplicate)
                        Divider()
                        Button("删除任务", role: .destructive, action: onDelete)
                    } label: {
                        Image(systemName: "ellipsis")
                            .frame(width: 20, height: 20)
                    }
                    .menuStyle(.borderlessButton)
                    .fixedSize()
                    .accessibilityLabel("更多任务操作")
                }
            }
            .padding(16)

            if let progress {
                VStack(spacing: 6) {
                    HStack {
                        Text(progressText(progress))
                            .font(.caption.monospacedDigit())
                            .foregroundStyle(.secondary)
                        Spacer()
                        Button("停止任务", action: onRun)
                            .buttonStyle(.borderless)
                            .foregroundStyle(.red)
                    }

                    if let fraction = progress.fractionComplete {
                        ProgressView(value: fraction)
                            .tint(.orange)
                    } else {
                        ProgressView()
                            .controlSize(.small)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.bottom, 14)
            }
        }
        .background(
            isRunning ? Color.orange.opacity(0.07) : Color(nsColor: .controlBackgroundColor),
            in: RoundedRectangle(cornerRadius: 14)
        )
        .overlay {
            RoundedRectangle(cornerRadius: 14)
                .stroke(isRunning ? Color.orange.opacity(0.32) : Color.primary.opacity(0.07))
        }
    }

    private var statusIcon: some View {
        Image(systemName: isRunning ? "play.fill" : "command")
            .font(.system(size: 15, weight: .semibold))
            .foregroundStyle(isRunning ? .white : .indigo)
            .frame(width: 36, height: 36)
            .background(
                isRunning ? Color.orange : Color.indigo.opacity(0.10),
                in: RoundedRectangle(cornerRadius: 10)
            )
            .accessibilityHidden(true)
    }

    @ViewBuilder
    private var shortcutRow: some View {
        HStack(spacing: 7) {
            if let shortcut = task.shortcuts.start {
                ShortcutBadge(
                    title: "启动",
                    shortcut: shortcut,
                    error: startError,
                    tint: .indigo
                )
            }

            if let shortcut = task.shortcuts.stop {
                ShortcutBadge(
                    title: "停止",
                    shortcut: shortcut,
                    error: stopError,
                    tint: .red
                )
            }

            if task.shortcuts.start == nil && task.shortcuts.stop == nil {
                Label("未设置快捷键", systemImage: "keyboard")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            }
        }
    }

    private var scheduleLabel: String {
        guard task.isScheduleEnabled else {
            return "手动触发"
        }
        switch task.schedule.mode {
        case .manual:
            return "手动触发"
        case .interval:
            return "每 \(task.schedule.intervalSeconds) 秒"
        case .daily:
            return String(
                format: "每天 %02d:%02d",
                task.schedule.hour,
                task.schedule.minute
            )
        }
    }

    private func progressText(_ progress: RunProgress) -> String {
        let loop = progress.totalLoops.map {
            "第 \(progress.currentLoop)/\($0) 轮"
        } ?? "第 \(progress.currentLoop) 轮 · 无限循环"
        return "\(loop) · 动作 \(progress.currentAction)/\(progress.totalActions)"
    }
}

private struct TaskMetadataTag: View {
    let icon: String
    let text: String

    var body: some View {
        Label(text, systemImage: icon)
            .font(.caption)
            .foregroundStyle(.secondary)
            .lineLimit(1)
    }
}

private struct ShortcutBadge: View {
    let title: String
    let shortcut: GlobalShortcut
    let error: String?
    let tint: Color

    var body: some View {
        HStack(spacing: 5) {
            Text(title)
                .foregroundStyle(.secondary)
            Text(shortcut.displayName)
                .font(.caption.monospaced().weight(.semibold))
                .foregroundStyle(error == nil ? tint : .red)
            if error != nil {
                Image(systemName: "exclamationmark.triangle.fill")
                    .font(.caption2)
                    .foregroundStyle(.red)
            }
        }
        .font(.caption)
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(
            (error == nil ? tint : Color.red).opacity(0.08),
            in: Capsule()
        )
        .help(error ?? "\(title)：\(shortcut.displayName)")
    }
}
