import Foundation

struct RunProgress: Equatable {
    let currentLoop: Int
    let totalLoops: Int?
    let currentAction: Int
    let totalActions: Int
    let startedAt: Date

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

    var progressDescription: String {
        if let totalLoops {
            return "第 \(currentLoop)/\(totalLoops) 轮 · "
                + "动作 \(currentAction)/\(totalActions)"
        }
        return "第 \(currentLoop) 轮 · 无限循环 · "
            + "动作 \(currentAction)/\(totalActions)"
    }

    func elapsedTimeDescription(at date: Date) -> String {
        let totalSeconds = Int(max(0, date.timeIntervalSince(startedAt)))
        let hours = totalSeconds / 3_600
        let minutes = (totalSeconds % 3_600) / 60
        let seconds = totalSeconds % 60

        if hours > 0 {
            return String(
                format: "%d:%02d:%02d",
                hours,
                minutes,
                seconds
            )
        }
        return String(format: "%02d:%02d", minutes, seconds)
    }
}
