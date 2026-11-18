import SwiftUI

@main
struct KeyToolApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var appModel = AppModel()

    var body: some Scene {
        WindowGroup("KeyTool", id: "main") {
            ContentView(appModel: appModel)
        }
        .defaultSize(width: 1_060, height: 740)
        .windowResizability(.contentMinSize)
        .commands {
            CommandGroup(after: .newItem) {
                Button("新建任务") {
                    appModel.store.createTask()
                }
                .keyboardShortcut("n", modifiers: .command)
            }
            CommandMenu("任务") {
                Button("运行当前任务") {
                    guard let task = appModel.store.selectedTask else { return }
                    appModel.runner.start(task)
                }
                .keyboardShortcut(.return, modifiers: .command)

                Button("停止所有任务") {
                    appModel.runner.stopAll()
                }
                .keyboardShortcut(".", modifiers: [.command, .shift])
            }
        }

        MenuBarExtra("KeyTool", systemImage: "command.circle.fill") {
            MenuBarContentView(appModel: appModel)
        }
        .menuBarExtraStyle(.menu)
    }
}

@MainActor
final class AppModel: ObservableObject {
    let store: TaskStore
    let runner: MacroRunner
    let scheduler: TaskScheduler
    let hotKeyManager: GlobalHotKeyManager
    let actionRecorder: ActionRecorder

    init() {
        let store = TaskStore()
        let runner = MacroRunner()
        self.store = store
        self.runner = runner
        self.scheduler = TaskScheduler(store: store, runner: runner)
        self.hotKeyManager = GlobalHotKeyManager(store: store, runner: runner)
        self.actionRecorder = ActionRecorder()
        scheduler.start()
        hotKeyManager.start()
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }
}
