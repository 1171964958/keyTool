using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KeyTool.Windows.Models;

namespace KeyTool.Windows.Services;

public sealed class TaskRunner
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobs = new();

    public bool IsRunning(Guid taskId) => _jobs.ContainsKey(taskId);

    public void Start(MacroTask task)
    {
        if (_jobs.ContainsKey(task.Id))
        {
            return;
        }

        if (task.Target is null)
        {
            WindowInputService.TraceTask($"start-rejected task={task.Id} reason=no-target");
            SetError(task, "请先选择目标窗口。");
            return;
        }

        var resolvedTarget = WindowCatalogService.ResolveTarget(task.Target);
        if (resolvedTarget is null)
        {
            WindowInputService.TraceTask($"start-rejected task={task.Id} reason=target-not-found");
            SetError(task, "找不到目标窗口，请刷新窗口列表并重新选择。");
            return;
        }
        if (resolvedTarget.WindowHandle != task.Target.WindowHandle
            || resolvedTarget.Title != task.Target.Title
            || resolvedTarget.ProcessName != task.Target.ProcessName)
        {
            UpdateRuntime(task, () => task.Target = resolvedTarget);
        }
        if (task.KeyboardInputMode == KeyboardInputMode.Adaptive)
        {
            WindowInputService.WatchTarget(new IntPtr(resolvedTarget.WindowHandle));
        }

        if (task.Actions.Count == 0)
        {
            WindowInputService.TraceTask($"start-rejected task={task.Id} reason=no-actions");
            SetError(task, "任务中没有可执行动作。");
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        if (!_jobs.TryAdd(task.Id, cancellationSource))
        {
            WindowInputService.TraceTask($"start-rejected task={task.Id} reason=already-running");
            cancellationSource.Dispose();
            return;
        }

        WindowInputService.TraceTask(
            $"start task={task.Id} target=0x{resolvedTarget.WindowHandle:X} "
            + $"mode={task.KeyboardInputMode}");

        UpdateRuntime(task, () =>
        {
            task.Runtime.ErrorMessage = null;
            task.Runtime.IsRunning = true;
            task.Runtime.CurrentLoop = 1;
            task.Runtime.TotalLoops = task.IsLoopInfinite ? null : task.LoopCount;
            task.Runtime.CurrentAction = 1;
            task.Runtime.TotalActions = task.Actions.Count;
        });

        _ = Task.Run(
            () => RunAsync(task, cancellationSource.Token),
            CancellationToken.None
        );
    }

    public void Stop(Guid taskId)
    {
        if (_jobs.TryRemove(taskId, out var cancellationSource))
        {
            WindowInputService.TraceTask($"stop-requested task={taskId}");
            cancellationSource.Cancel();
            cancellationSource.Dispose();
        }
    }

    public void StopAll()
    {
        foreach (var taskId in _jobs.Keys)
        {
            Stop(taskId);
        }
    }

    private async System.Threading.Tasks.Task RunAsync(
        MacroTask task,
        CancellationToken cancellationToken)
    {
        try
        {
            var loopIndex = 1;
            while (!cancellationToken.IsCancellationRequested
                && (task.IsLoopInfinite || loopIndex <= task.LoopCount))
            {
                var actionIndex = 0;
                while (actionIndex < task.Actions.Count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryResolveTarget(task, out var windowHandle))
                    {
                        var missingTargetMessage = "目标窗口暂时不可用，任务正在等待窗口恢复。";
                        UpdateRuntime(task, () =>
                            task.Runtime.ErrorMessage = missingTargetMessage);
                        WindowInputService.TraceTask(
                            $"waiting-for-target task={task.Id}");
                        await Task.Delay(750, cancellationToken);
                        continue;
                    }

                    var selectedLoop = loopIndex;
                    var selectedAction = actionIndex + 1;
                    UpdateRuntime(task, () =>
                    {
                        task.Runtime.CurrentLoop = selectedLoop;
                        task.Runtime.CurrentAction = selectedAction;
                    });
                    try
                    {
                        await WindowInputService.ExecuteAsync(
                            task.Actions[actionIndex],
                            windowHandle,
                            task.KeyboardInputMode,
                            cancellationToken
                        );
                        UpdateRuntime(task, () => task.Runtime.ErrorMessage = null);
                        actionIndex++;
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        WindowInputService.TraceTask(
                            $"action-error task={task.Id} "
                            + $"loop={loopIndex} action={selectedAction} "
                            + $"{ex.GetType().Name}: {ex.Message}");
                        UpdateRuntime(task, () =>
                            task.Runtime.ErrorMessage = ex.Message);
                        await Task.Delay(750, cancellationToken);
                    }
                }
                loopIndex++;
            }
        }
        catch (OperationCanceledException)
        {
            WindowInputService.TraceTask($"cancelled task={task.Id}");
            // Manual stop is an expected completion path.
        }
        catch (Exception ex)
        {
            WindowInputService.TraceTask(
                $"error task={task.Id} {ex.GetType().Name}: {ex}");
            UpdateRuntime(task, () => task.Runtime.ErrorMessage = ex.Message);
        }
        finally
        {
            WindowInputService.TraceTask($"finish task={task.Id}");
            UpdateRuntime(task, task.Runtime.Reset);
            if (_jobs.TryRemove(task.Id, out var cancellationSource))
            {
                cancellationSource.Dispose();
            }
        }
    }

    private static bool TryResolveTarget(
        MacroTask task,
        out IntPtr windowHandle)
    {
        windowHandle = IntPtr.Zero;
        if (task.Target is null)
        {
            return false;
        }

        var resolvedTarget = WindowCatalogService.ResolveTarget(task.Target);
        if (resolvedTarget is null)
        {
            return false;
        }

        if (resolvedTarget.WindowHandle != task.Target.WindowHandle
            || resolvedTarget.Title != task.Target.Title
            || resolvedTarget.ProcessName != task.Target.ProcessName)
        {
            UpdateRuntime(task, () => task.Target = resolvedTarget);
        }
        if (task.KeyboardInputMode == KeyboardInputMode.Adaptive)
        {
            WindowInputService.WatchTarget(
                new IntPtr(resolvedTarget.WindowHandle));
        }

        windowHandle = new IntPtr(resolvedTarget.WindowHandle);
        return true;
    }

    private static void SetError(MacroTask task, string message)
    {
        UpdateRuntime(task, () => task.Runtime.ErrorMessage = message);
    }

    private static void UpdateRuntime(MacroTask task, Action update)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            update();
        }
        else
        {
            dispatcher.Invoke(update);
        }
    }
}
