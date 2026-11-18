import AppKit
import SwiftUI

struct MenuBarContentView: View {
    @ObservedObject private var store: TaskStore
    @ObservedObject private var runner: MacroRunner
    @Environment(\.openWindow) private var openWindow

    init(appModel: AppModel) {
        _store = ObservedObject(wrappedValue: appModel.store)
        _runner = ObservedObject(wrappedValue: appModel.runner)
    }

    var body: some View {
        if let task = store.selectedTask {
            Button(runner.isRunning(task.id) ? "停止 \(task.name)" : "运行 \(task.name)") {
                if runner.isRunning(task.id) {
                    runner.stop(task.id)
                } else {
                    runner.start(task)
                }
            }
        } else {
            Text("未选择任务")
        }

        Divider()

        Button("打开 KeyTool") {
            openWindow(id: "main")
            NSApp.activate(ignoringOtherApps: true)
        }

        Button("停止所有任务") {
            runner.stopAll()
        }
        .disabled(runner.runningTaskIDs.isEmpty)

        Divider()

        if AccessibilityPermission.isTrusted {
            Text("辅助功能权限：已授权")
        } else {
            Button("授予辅助功能权限") {
                AccessibilityPermission.request()
            }
            Button("打开系统设置") {
                AccessibilityPermission.openSystemSettings()
            }
        }

        Divider()

        Button("退出 KeyTool") {
            NSApp.terminate(nil)
        }
    }
}
