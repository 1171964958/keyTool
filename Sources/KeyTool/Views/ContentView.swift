import AppKit
import SwiftUI

enum AppDestination: Hashable {
    case management
    case editor(UUID)
}

@MainActor
private final class AppNavigation: ObservableObject {
    @Published var destination: AppDestination? = .management
}

struct ContentView: View {
    @ObservedObject private var store: TaskStore
    @ObservedObject private var runner: MacroRunner
    @ObservedObject private var hotKeyManager: GlobalHotKeyManager
    @ObservedObject private var actionRecorder: ActionRecorder
    @StateObject private var permissionState = PermissionState()
    @StateObject private var navigation = AppNavigation()

    init(appModel: AppModel) {
        _store = ObservedObject(wrappedValue: appModel.store)
        _runner = ObservedObject(wrappedValue: appModel.runner)
        _hotKeyManager = ObservedObject(wrappedValue: appModel.hotKeyManager)
        _actionRecorder = ObservedObject(wrappedValue: appModel.actionRecorder)
    }

    var body: some View {
        NavigationSplitView {
            AppSidebar(
                store: store,
                runner: runner,
                selection: $navigation.destination,
                createTask: createTask,
                editTask: editTask,
                deleteTask: deleteTask
            )
            .navigationSplitViewColumnWidth(min: 220, ideal: 260, max: 330)
        } detail: {
            detail
        }
        .frame(minWidth: 920, minHeight: 620)
        .tint(.indigo)
        .onAppear(perform: refreshPermission)
        .onChange(of: navigation.destination) { _, destination in
            if case .editor(let taskID) = destination {
                store.selectedTaskID = taskID
            }
        }
        .onReceive(NotificationCenter.default.publisher(
            for: NSApplication.didBecomeActiveNotification
        )) { _ in
            refreshPermission()
        }
    }

    @ViewBuilder
    private var detail: some View {
        switch navigation.destination {
        case .management, nil:
            managementView
        case .editor(let taskID):
            if let taskBinding = store.binding(for: taskID) {
                TaskEditorView(
                    task: taskBinding,
                    runner: runner,
                    hotKeyManager: hotKeyManager,
                    actionRecorder: actionRecorder,
                    permissionTrusted: $permissionState.isTrusted
                )
                .id(taskID)
            } else {
                managementView
            }
        }
    }

    private var managementView: some View {
        TaskManagementView(
            store: store,
            runner: runner,
            hotKeyManager: hotKeyManager,
            onCreate: createTask,
            onEdit: editTask
        )
    }

    private func createTask() {
        store.createTask()
        if let taskID = store.selectedTaskID {
            navigation.destination = .editor(taskID)
        }
    }

    private func editTask(_ taskID: UUID) {
        store.selectedTaskID = taskID
        navigation.destination = .editor(taskID)
    }

    private func deleteTask(_ task: MacroTask) {
        store.deleteTask(task)
        if navigation.destination == .editor(task.id) {
            navigation.destination = .management
        }
    }

    private func refreshPermission() {
        permissionState.isTrusted = AccessibilityPermission.isTrusted
    }
}

private final class PermissionState: ObservableObject {
    @Published var isTrusted = AccessibilityPermission.isTrusted
}

private struct AppSidebar: View {
    @ObservedObject var store: TaskStore
    @ObservedObject var runner: MacroRunner
    @Binding var selection: AppDestination?
    let createTask: () -> Void
    let editTask: (UUID) -> Void
    let deleteTask: (MacroTask) -> Void

    var body: some View {
        VStack(spacing: 0) {
            List(selection: $selection) {
                Section {
                    Label("任务管理", systemImage: "square.grid.2x2")
                        .tag(AppDestination.management)
                }

                Section("任务") {
                    ForEach(store.tasks) { task in
                        TaskSidebarRow(
                            task: task,
                            progress: runner.progress(for: task.id)
                        )
                        .tag(AppDestination.editor(task.id))
                        .contextMenu {
                            Button("编辑任务") {
                                editTask(task.id)
                            }
                            Button("复制任务") {
                                store.duplicateTask(task)
                            }
                            Divider()
                            Button("删除任务", role: .destructive) {
                                deleteTask(task)
                            }
                        }
                    }
                }
            }
            .listStyle(.sidebar)

            Divider()

            HStack(spacing: 8) {
                Button(action: createTask) {
                    Label("新建任务", systemImage: "plus")
                }
                .buttonStyle(.borderless)

                Spacer()

                Button {
                    guard let task = store.selectedTask else { return }
                    store.duplicateTask(task)
                } label: {
                    Image(systemName: "plus.square.on.square")
                }
                .buttonStyle(.borderless)
                .disabled(store.selectedTask == nil)
                .help("复制当前任务")
                .accessibilityLabel("复制当前任务")

                Button {
                    guard let task = store.selectedTask else { return }
                    deleteTask(task)
                } label: {
                    Image(systemName: "trash")
                }
                .buttonStyle(.borderless)
                .disabled(store.selectedTask == nil)
                .help("删除当前任务")
                .accessibilityLabel("删除当前任务")
            }
            .padding(12)
        }
        .navigationTitle("KeyTool")
    }
}

private struct TaskSidebarRow: View {
    let task: MacroTask
    let progress: RunProgress?

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack(spacing: 7) {
                Text(task.name.isEmpty ? "未命名任务" : task.name)
                    .font(.headline)
                    .lineLimit(1)

                if progress != nil {
                    Circle()
                        .fill(Color.orange)
                        .frame(width: 7, height: 7)
                        .accessibilityHidden(true)
                }
            }

            HStack(spacing: 6) {
                Image(systemName: task.isLoopInfinite ? "infinity" : "repeat")
                    .font(.caption)

                Text(scheduleSummary)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)

                Spacer()

                if let progress {
                    Text(progressLabel(progress))
                        .font(.caption.monospacedDigit().weight(.semibold))
                        .foregroundStyle(.orange)
                        .accessibilityLabel(progressAccessibilityLabel(progress))
                }
            }
        }
        .padding(.vertical, 5)
        .accessibilityElement(children: .combine)
    }

    private var scheduleSummary: String {
        guard task.isScheduleEnabled else {
            return "手动运行"
        }
        switch task.schedule.mode {
        case .manual:
            return "手动运行"
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

    private func progressLabel(_ progress: RunProgress) -> String {
        progress.totalLoops.map { "\(progress.currentLoop)/\($0)" } ?? "∞"
    }

    private func progressAccessibilityLabel(_ progress: RunProgress) -> String {
        if let totalLoops = progress.totalLoops {
            return "正在执行，第 \(progress.currentLoop) 轮，共 \(totalLoops) 轮"
        }
        return "正在执行，第 \(progress.currentLoop) 轮，无限循环"
    }
}
