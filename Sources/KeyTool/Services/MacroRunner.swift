import SwiftUI

struct RunProgress: Equatable {
    let currentLoop: Int
    let totalLoops: Int?
    let currentAction: Int
    let totalActions: Int

    var fractionComplete: Double? {
        guard
            let totalLoops,
            totalLoops > 0,
            totalActions > 0
        else {
            return nil
        }
        let completedActions = (currentLoop - 1) * totalActions
            + max(0, currentAction - 1)
        return min(
            1,
            Double(completedActions) / Double(totalLoops * totalActions)
        )
    }
}

@MainActor
final class MacroRunner: ObservableObject {
    @Published private(set) var runningTaskIDs: Set<UUID> = []
    @Published private(set) var progressByTask: [UUID: RunProgress] = [:]
    @Published var errorMessage: String?

    private var jobs: [UUID: Task<Void, Never>] = [:]

    func isRunning(_ taskID: UUID) -> Bool {
        runningTaskIDs.contains(taskID)
    }

    func progress(for taskID: UUID) -> RunProgress? {
        progressByTask[taskID]
    }

    func start(_ task: MacroTask) {
        guard !task.actions.isEmpty else {
            errorMessage = "任务中没有可执行动作。"
            return
        }
        guard AccessibilityPermission.isTrusted else {
            errorMessage = "请先授予 KeyTool 辅助功能权限。"
            return
        }
        guard let target = task.target else {
            errorMessage = "请先选择目标应用。"
            return
        }
        guard NSRunningApplication(processIdentifier: target.processID)?.isTerminated == false else {
            errorMessage = "目标应用已退出，请重新选择目标应用。"
            return
        }
        guard !isRunning(task.id) else {
            return
        }

        runningTaskIDs.insert(task.id)
        progressByTask[task.id] = RunProgress(
            currentLoop: 1,
            totalLoops: task.isLoopInfinite ? nil : task.loopCount,
            currentAction: 1,
            totalActions: task.actions.count
        )
        errorMessage = nil

        let job = Task { [weak self] in
            do {
                var loopIndex = 1
                while !Task.isCancelled
                    && (task.isLoopInfinite || loopIndex <= task.loopCount)
                {
                    for (actionIndex, action) in task.actions.enumerated() {
                        try Task.checkCancellation()
                        self?.progressByTask[task.id] = RunProgress(
                            currentLoop: loopIndex,
                            totalLoops: task.isLoopInfinite ? nil : task.loopCount,
                            currentAction: actionIndex + 1,
                            totalActions: task.actions.count
                        )
                        try await InputEngine.perform(action, to: target.processID)
                    }
                    loopIndex += 1
                }
            } catch is CancellationError {
                // A stopped task is an expected state.
            } catch {
                self?.errorMessage = error.localizedDescription
            }
            self?.finish(task.id)
        }
        jobs[task.id] = job
    }

    func stop(_ taskID: UUID) {
        jobs[taskID]?.cancel()
        finish(taskID)
    }

    func stopAll() {
        jobs.values.forEach { $0.cancel() }
        jobs.removeAll()
        runningTaskIDs.removeAll()
        progressByTask.removeAll()
    }

    private func finish(_ taskID: UUID) {
        jobs.removeValue(forKey: taskID)
        runningTaskIDs.remove(taskID)
        progressByTask.removeValue(forKey: taskID)
    }
}
