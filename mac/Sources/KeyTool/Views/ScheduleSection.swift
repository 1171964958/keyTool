import SwiftUI

struct ScheduleSection: View {
    @Binding var task: MacroTask
    @ObservedObject var runner: MacroRunner

    var body: some View {
        InspectorCard(title: "执行计划", icon: "calendar.badge.clock") {
            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 3) {
                    Text("循环次数")
                    Text("每次触发时重复整套动作")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                Spacer()

                TextField(
                    "次数",
                    value: Binding(
                        get: { task.loopCount },
                        set: {
                            task.loopCount = min(
                                max($0, MacroTask.loopCountRange.lowerBound),
                                MacroTask.loopCountRange.upperBound
                            )
                        }
                    ),
                    format: .number
                )
                .textFieldStyle(.roundedBorder)
                .multilineTextAlignment(.trailing)
                .monospacedDigit()
                .frame(width: 76)
                .accessibilityLabel("循环次数")
                .disabled(isRunning || task.isLoopInfinite)

                Stepper(
                    "循环次数",
                    value: $task.loopCount,
                    in: MacroTask.loopCountRange
                )
                .labelsHidden()
                .accessibilityLabel("调整循环次数")
                .disabled(isRunning || task.isLoopInfinite)

                Toggle("无限循环", isOn: $task.isLoopInfinite)
                    .toggleStyle(.switch)
                    .controlSize(.small)
                    .disabled(isRunning)

                if isRunning {
                    Button {
                        runner.stop(task.id)
                    } label: {
                        Label("停止任务", systemImage: "stop.fill")
                    }
                    .buttonStyle(.bordered)
                    .tint(.red)
                }
            }

            Divider()

            Toggle("启用自动执行", isOn: $task.isScheduleEnabled)
                .disabled(isRunning)

            Picker("计划", selection: $task.schedule.mode) {
                ForEach(TaskSchedule.Mode.allCases) { mode in
                    Text(mode.label).tag(mode)
                }
            }
            .pickerStyle(.segmented)
            .disabled(!task.isScheduleEnabled || isRunning)

            switch task.schedule.mode {
            case .manual:
                Text("点击“运行”时执行一次。")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            case .interval:
                HStack(spacing: 10) {
                    Text("时间间隔")
                    TextField(
                        "秒",
                        value: Binding(
                            get: { task.schedule.intervalSeconds },
                            set: {
                                task.schedule.intervalSeconds = min(
                                    max($0, TaskSchedule.intervalRange.lowerBound),
                                    TaskSchedule.intervalRange.upperBound
                                )
                            }
                        ),
                        format: .number
                    )
                    .textFieldStyle(.roundedBorder)
                    .multilineTextAlignment(.trailing)
                    .monospacedDigit()
                    .frame(width: 90)
                    .accessibilityLabel("定时间隔秒数")
                    Text("秒")

                    Stepper(
                        "定时间隔",
                        value: $task.schedule.intervalSeconds,
                        in: TaskSchedule.intervalRange,
                        step: 5
                    )
                    .labelsHidden()
                    .accessibilityLabel("调整定时间隔")
                }
                .disabled(!task.isScheduleEnabled || isRunning)

                Text("可输入 1–86,400 秒。")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            case .daily:
                HStack {
                    Stepper(
                        String(format: "时：%02d", task.schedule.hour),
                        value: $task.schedule.hour,
                        in: 0...23
                    )
                    Stepper(
                        String(format: "分：%02d", task.schedule.minute),
                        value: $task.schedule.minute,
                        in: 0...59,
                        step: 5
                    )
                }
                .disabled(!task.isScheduleEnabled || isRunning)
            }

            Text("无限循环会持续执行，直到手动停止。自动执行在 KeyTool 菜单栏进程存活时生效。")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private var isRunning: Bool {
        runner.isRunning(task.id)
    }
}
