using System;
using System.Collections.Generic;
using System.Linq;
using KeyTool.Windows.Models;

namespace KeyTool.Windows.Services;

public sealed class TaskSchedulerService
{
    private sealed record ScheduleSignature(
        ScheduleMode Mode,
        int IntervalSeconds,
        int Hour,
        int Minute
    );

    private sealed record PlannedRun(ScheduleSignature Schedule, DateTime RunAt);

    private readonly Dictionary<Guid, PlannedRun> _plannedRuns = new();
    private readonly TaskRunner _runner;

    public TaskSchedulerService(TaskRunner runner)
    {
        _runner = runner;
    }

    public void Tick(IReadOnlyList<MacroTask> tasks, DateTime now)
    {
        var active = tasks.ToDictionary(task => task.Id);
        var schedulableIds = tasks
            .Where(task => task.IsScheduleEnabled && task.Schedule.Mode != ScheduleMode.Manual)
            .Select(task => task.Id)
            .ToHashSet();

        foreach (var taskId in _plannedRuns.Keys.ToList())
        {
            if (!active.ContainsKey(taskId) || !schedulableIds.Contains(taskId))
            {
                _plannedRuns.Remove(taskId);
            }
        }

        foreach (var task in tasks)
        {
            if (!task.IsScheduleEnabled
                || task.Schedule.Mode == ScheduleMode.Manual
                || task.Runtime.IsRunning)
            {
                continue;
            }

            if (_plannedRuns.TryGetValue(task.Id, out var planned)
                && planned.Schedule == Snapshot(task.Schedule))
            {
                if (now >= planned.RunAt)
                {
                    _runner.Start(task);
                    UpdateNextRun(task, now);
                }
            }
            else
            {
                UpdateNextRun(task, now);
            }
        }
    }

    private void UpdateNextRun(MacroTask task, DateTime now)
    {
        var next = task.Schedule.NextRun(now);
        if (next.HasValue)
        {
            _plannedRuns[task.Id] = new PlannedRun(
                Snapshot(task.Schedule),
                next.Value
            );
        }
        else
        {
            _plannedRuns.Remove(task.Id);
        }
    }

    private static ScheduleSignature Snapshot(TaskSchedule schedule)
    {
        return new ScheduleSignature(
            schedule.Mode,
            schedule.IntervalSeconds,
            schedule.Hour,
            schedule.Minute
        );
    }
}
