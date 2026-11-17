import SwiftUI

@MainActor
final class TaskScheduler: ObservableObject {
    private struct PlannedRun {
        let schedule: TaskSchedule
        let date: Date
    }

    private let store: TaskStore
    private let runner: MacroRunner
    private var plannedRuns: [UUID: PlannedRun] = [:]
    private var loop: Task<Void, Never>?

    init(store: TaskStore, runner: MacroRunner) {
        self.store = store
        self.runner = runner
    }

    deinit {
        loop?.cancel()
    }

    func start() {
        guard loop == nil else { return }
        loop = Task { [weak self] in
            while !Task.isCancelled {
                self?.tick()
                try? await Task.sleep(nanoseconds: 1_000_000_000)
            }
        }
    }

    func tick(now: Date = Date()) {
        let activeIDs = Set(store.tasks.map(\.id))
        let schedulableIDs = Set(
            store.tasks
                .filter { $0.isScheduleEnabled && $0.schedule.mode != .manual }
                .map(\.id)
        )
        plannedRuns = plannedRuns.filter {
            activeIDs.contains($0.key) && schedulableIDs.contains($0.key)
        }

        for task in store.tasks where task.isScheduleEnabled {
            guard task.schedule.mode != .manual, !runner.isRunning(task.id) else {
                plannedRuns.removeValue(forKey: task.id)
                continue
            }

            if
                let planned = plannedRuns[task.id],
                planned.schedule == task.schedule
            {
                if now >= planned.date {
                    runner.start(task)
                    if let nextDate = task.schedule.nextRun(after: now) {
                        plannedRuns[task.id] = PlannedRun(
                            schedule: task.schedule,
                            date: nextDate
                        )
                    } else {
                        plannedRuns.removeValue(forKey: task.id)
                    }
                }
            } else if let nextDate = task.schedule.nextRun(after: now) {
                plannedRuns[task.id] = PlannedRun(
                    schedule: task.schedule,
                    date: nextDate
                )
            }
        }
    }
}
