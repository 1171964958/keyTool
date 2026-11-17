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
            SetError(task, "请先选择目标窗口。");
            return;
        }

        var resolvedTarget = WindowCatalogService.ResolveTarget(task.Target);
        if (resolvedTarget is null)
        {
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
            SetError(task, "任务中没有可执行动作。");
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        if (!_jobs.TryAdd(task.Id, cancellationSource))
        {
            cancellationSource.Dispose();
            return;
        }

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
            var windowHandle = new IntPtr(task.Target!.WindowHandle);
            var loopIndex = 1;
            while (!cancellationToken.IsCancellationRequested
                && (task.IsLoopInfinite || loopIndex <= task.LoopCount))
            {
                for (var actionIndex = 0; actionIndex < task.Actions.Count; actionIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var selectedLoop = loopIndex;
                    var selectedAction = actionIndex + 1;
                    UpdateRuntime(task, () =>
                    {
                        task.Runtime.CurrentLoop = selectedLoop;
                        task.Runtime.CurrentAction = selectedAction;
                    });
                    await WindowInputService.ExecuteAsync(
                        task.Actions[actionIndex],
                        windowHandle,
                        task.KeyboardInputMode,
                        cancellationToken
                    );
                }
                loopIndex++;
            }
        }
        catch (OperationCanceledException)
        {
            // Manual stop is an expected completion path.
        }
        catch (Exception ex)
        {
            UpdateRuntime(task, () => task.Runtime.ErrorMessage = ex.Message);
        }
        finally
        {
            UpdateRuntime(task, task.Runtime.Reset);
            if (_jobs.TryRemove(task.Id, out var cancellationSource))
            {
                cancellationSource.Dispose();
            }
        }
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
