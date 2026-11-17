import AppKit
import SwiftUI

struct TaskEditorView: View {
    @Binding var task: MacroTask
    @ObservedObject var runner: MacroRunner
    @ObservedObject var hotKeyManager: GlobalHotKeyManager
    @ObservedObject var actionRecorder: ActionRecorder
    @Binding var permissionTrusted: Bool

    @StateObject private var actionSelection = ActionSelectionState()

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()

            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    if !permissionTrusted {
                        PermissionBanner(permissionTrusted: $permissionTrusted)
                    }

                    if let errorMessage = runner.errorMessage {
                        ErrorBanner(message: errorMessage) {
                            runner.errorMessage = nil
                        }
                    }

                    TargetSection(task: $task)
                    ActionSection(
                        taskID: task.id,
                        actions: $task.actions,
                        selectedActionID: $actionSelection.actionID,
                        recorder: actionRecorder
                    )
                    ShortcutSection(task: $task, hotKeyManager: hotKeyManager)
                    ScheduleSection(task: $task, runner: runner)
                }
                .padding(24)
                .frame(maxWidth: 860, alignment: .leading)
            }
        }
        .background(Color(nsColor: .windowBackgroundColor))
    }

    private var header: some View {
        HStack(spacing: 14) {
            VStack(alignment: .leading, spacing: 2) {
                TextField("任务名称", text: $task.name)
                    .font(.system(size: 24, weight: .semibold))
                    .textFieldStyle(.plain)
                    .accessibilityLabel("任务名称")

                Text(task.statusDescription)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            if let progress = runner.progress(for: task.id) {
                VStack(alignment: .leading, spacing: 4) {
                    Label("执行中", systemImage: "waveform.path.ecg")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.orange)

                    Text(progressDescription(progress))
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(.secondary)

                    if let fraction = progress.fractionComplete {
                        ProgressView(value: fraction)
                            .progressViewStyle(.linear)
                            .tint(.orange)
                            .frame(width: 132)
                    } else {
                        ProgressView()
                            .controlSize(.small)
                            .tint(.orange)
                    }
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 7)
                .background(
                    Color.orange.opacity(0.10),
                    in: RoundedRectangle(cornerRadius: 10)
                )
                .overlay {
                    RoundedRectangle(cornerRadius: 10)
                        .stroke(Color.orange.opacity(0.28))
                }
                .accessibilityElement(children: .combine)
            }

            Button {
                if runner.isRunning(task.id) {
                    runner.stop(task.id)
                } else {
                    runner.start(task)
                }
            } label: {
                Label(
                    runner.isRunning(task.id) ? "立即停止" : "运行",
                    systemImage: runner.isRunning(task.id)
                        ? "stop.fill"
                        : "play.fill"
                )
                .frame(minWidth: 82)
            }
            .buttonStyle(.borderedProminent)
            .tint(runner.isRunning(task.id) ? .red : .indigo)
            .controlSize(.large)
            .keyboardShortcut(.return, modifiers: .command)
        }
        .padding(.horizontal, 24)
        .padding(.vertical, 16)
        .background(.regularMaterial)
    }

    private func progressDescription(_ progress: RunProgress) -> String {
        if let totalLoops = progress.totalLoops {
            return "第 \(progress.currentLoop)/\(totalLoops) 轮 · "
                + "动作 \(progress.currentAction)/\(progress.totalActions)"
        }
        return "第 \(progress.currentLoop) 轮 · 无限循环 · "
            + "动作 \(progress.currentAction)/\(progress.totalActions)"
    }
}

private final class ActionSelectionState: ObservableObject {
    @Published var actionID: UUID?
}

private extension MacroTask {
    var statusDescription: String {
        let target = target?.name ?? "未选择目标应用"
        let loops = isLoopInfinite ? "无限循环" : "循环 \(loopCount) 次"
        return "\(actions.count) 个动作 · \(loops) · \(target)"
    }
}

private struct PermissionBanner: View {
    @Binding var permissionTrusted: Bool

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "lock.shield")
                .font(.title2)
                .foregroundStyle(.orange)
                .accessibilityHidden(true)

            VStack(alignment: .leading, spacing: 5) {
                Text("需要辅助功能权限")
                    .font(.headline)
                Text("KeyTool 需要此权限才能向目标应用发送按键和鼠标事件。")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                Text("如果设置中已经勾选，请完全退出并重新打开 KeyTool。")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            Button("授权") {
                AccessibilityPermission.request()
            }
            .buttonStyle(.borderedProminent)

            Button("打开设置") {
                AccessibilityPermission.openSystemSettings()
            }
            .buttonStyle(.bordered)

            Button {
                permissionTrusted = AccessibilityPermission.isTrusted
            } label: {
                Label("重新检测", systemImage: "arrow.clockwise")
            }
            .buttonStyle(.bordered)
            .accessibilityLabel("刷新权限状态")

            Button("重新启动") {
                relaunchApp()
            }
            .buttonStyle(.bordered)
        }
        .padding(16)
        .background(Color.orange.opacity(0.10), in: RoundedRectangle(cornerRadius: 12))
        .overlay {
            RoundedRectangle(cornerRadius: 12)
                .stroke(Color.orange.opacity(0.28))
        }
    }

    private func relaunchApp() {
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.createsNewApplicationInstance = true
        NSWorkspace.shared.openApplication(
            at: Bundle.main.bundleURL,
            configuration: configuration
        ) { _, _ in
            DispatchQueue.main.async {
                NSApp.terminate(nil)
            }
        }
    }
}

private struct ErrorBanner: View {
    let message: String
    let dismiss: () -> Void

    var body: some View {
        HStack(spacing: 12) {
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
        .padding(14)
        .background(Color.red.opacity(0.08), in: RoundedRectangle(cornerRadius: 12))
    }
}

private struct TargetSection: View {
    @Binding var task: MacroTask
    @StateObject private var catalog = ApplicationCatalogState()

    var body: some View {
        InspectorCard(title: "目标应用", icon: "macwindow") {
            HStack(spacing: 12) {
                Menu {
                    Button("使用当前前台应用") {
                        task.target = RunningApplicationCatalog.frontmost(
                            excluding: ProcessInfo.processInfo.processIdentifier
                        )
                    }
                    Divider()
                    ForEach(catalog.applications) { application in
                        Button(application.name) {
                            task.target = application
                        }
                    }
                    Divider()
                    Button("刷新应用列表", action: refreshApplications)
                } label: {
                    HStack {
                        Image(systemName: "app")
                            .accessibilityHidden(true)
                        Text(task.target?.name ?? "选择正在运行的应用")
                            .lineLimit(1)
                        Spacer()
                        Image(systemName: "chevron.up.chevron.down")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .accessibilityHidden(true)
                    }
                    .padding(.horizontal, 12)
                    .frame(height: 36)
                    .background(
                        Color(nsColor: .controlBackgroundColor),
                        in: RoundedRectangle(cornerRadius: 8)
                    )
                }
                .menuStyle(.borderlessButton)
                .accessibilityLabel("选择目标应用")

                Button("当前前台") {
                    task.target = RunningApplicationCatalog.frontmost(
                        excluding: ProcessInfo.processInfo.processIdentifier
                    )
                }
                .buttonStyle(.bordered)
            }

            if let target = task.target {
                HStack(spacing: 7) {
                    Circle()
                        .fill(isRunning(target) ? Color.green : Color.secondary)
                        .frame(width: 7, height: 7)
                        .accessibilityHidden(true)
                    Text(isRunning(target) ? "应用正在运行" : "应用已退出")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Text("PID \(target.processID)")
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.tertiary)
                }
            }

            Text("输入事件会发送到此进程，不切换当前前台应用。应用内部的窗口焦点仍由目标应用决定。")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .onAppear(perform: refreshApplications)
    }

    private func refreshApplications() {
        catalog.applications = RunningApplicationCatalog.current()
    }

    private func isRunning(_ target: TargetApplication) -> Bool {
        NSRunningApplication(processIdentifier: target.processID)?.isTerminated == false
    }
}

private final class ApplicationCatalogState: ObservableObject {
    @Published var applications: [TargetApplication] = []
}

struct InspectorCard<Content: View>: View {
    let title: String
    let icon: String
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Label(title, systemImage: icon)
                .font(.headline)
            content
        }
        .padding(18)
        .background(
            Color(nsColor: .controlBackgroundColor),
            in: RoundedRectangle(cornerRadius: 14)
        )
        .overlay {
            RoundedRectangle(cornerRadius: 14)
                .stroke(Color.primary.opacity(0.08))
        }
    }
}
