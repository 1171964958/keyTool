using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KeyTool.Windows.Infrastructure;
using KeyTool.Windows.Models;
using KeyTool.Windows.Services;

namespace KeyTool.Windows.ViewModels;

public enum MainPage
{
    Management,
    Editor
}

public sealed class MainViewModel : ObservableObject
{
    private readonly TaskRepository _repository = new();
    private readonly TaskRunner _runner = new();
    private readonly GlobalHotKeyService _hotKeys = new();
    private readonly TaskSchedulerService _scheduler;
    private readonly ActionRecorderService _recorder = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _scheduleTimer;
    private readonly HashSet<Guid> _attachedTasks = new();

    private MacroTask? _selectedTask;
    private MacroAction? _selectedAction;
    private MainPage _page = MainPage.Management;
    private bool _initialized;

    public MainViewModel()
    {
        Tasks = new ObservableCollection<MacroTask>(_repository.Load());
        AvailableWindows = new ObservableCollection<TargetWindow>();
        _scheduler = new TaskSchedulerService(_runner);

        foreach (var task in Tasks)
        {
            if (task.Target is not null)
            {
                task.Target = WindowCatalogService.ResolveTarget(task.Target)
                    ?? task.Target;
            }
            AttachTask(task);
        }

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveTasks();
        };

        _scheduleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _scheduleTimer.Tick += (_, _) => _scheduler.Tick(Tasks.ToList(), DateTime.Now);

        OpenManagementCommand = new RelayCommand(_ => Page = MainPage.Management);
        NewTaskCommand = new RelayCommand(_ => CreateTask());
        EditTaskCommand = new RelayCommand(parameter => EditTask(parameter as MacroTask));
        ToggleRunCommand = new RelayCommand(parameter => ToggleRun(parameter as MacroTask));
        DuplicateTaskCommand = new RelayCommand(parameter => DuplicateTask(parameter as MacroTask));
        DeleteTaskCommand = new RelayCommand(parameter => DeleteTask(parameter as MacroTask));
        RefreshWindowsCommand = new RelayCommand(_ => RefreshWindows());
        CaptureForegroundWindowCommand = new RelayCommand(_ => CaptureForegroundWindow());
        AddActionCommand = new RelayCommand(parameter => AddAction(parameter as string));
        RemoveActionCommand = new RelayCommand(parameter => RemoveAction(parameter as MacroAction));
        MoveActionUpCommand = new RelayCommand(parameter => MoveAction(parameter as MacroAction, -1));
        MoveActionDownCommand = new RelayCommand(parameter => MoveAction(parameter as MacroAction, 1));
        UseCursorCommand = new RelayCommand(parameter => UseCursor(parameter as MacroAction));
        ToggleRecordingCommand = new RelayCommand(_ => ToggleRecording());
        ClearStartShortcutCommand = new RelayCommand(_ => ClearShortcut(true));
        ClearStopShortcutCommand = new RelayCommand(_ => ClearShortcut(false));

        _hotKeys.HotKeyPressed += HandleHotKey;
        RefreshWindows();
    }

    public ObservableCollection<MacroTask> Tasks { get; }
    public ObservableCollection<TargetWindow> AvailableWindows { get; }
    public ActionRecorderService Recorder => _recorder;

    public MacroTask? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetProperty(ref _selectedTask, value))
            {
                SelectedAction = value?.Actions.FirstOrDefault();
                if (value is not null)
                {
                    Page = MainPage.Editor;
                }
                OnPropertyChanged(nameof(HasSelectedTask));
            }
        }
    }

    public MacroAction? SelectedAction
    {
        get => _selectedAction;
        set => SetProperty(ref _selectedAction, value);
    }

    public MainPage Page
    {
        get => _page;
        set
        {
            if (SetProperty(ref _page, value))
            {
                OnPropertyChanged(nameof(IsManagement));
                OnPropertyChanged(nameof(IsEditor));
            }
        }
    }

    public bool IsManagement => Page == MainPage.Management;
    public bool IsEditor => Page == MainPage.Editor;
    public bool HasTasks => Tasks.Count > 0;
    public bool HasSelectedTask => SelectedTask is not null;
    public int TaskCount => Tasks.Count;
    public int RunningCount => Tasks.Count(task => task.Runtime.IsRunning);
    public int ScheduledCount => Tasks.Count(task => task.IsScheduleEnabled);
    public int ShortcutTaskCount => Tasks.Count(task =>
        task.Shortcuts.Start is not null || task.Shortcuts.Stop is not null);

    public ICommand OpenManagementCommand { get; }
    public ICommand NewTaskCommand { get; }
    public ICommand EditTaskCommand { get; }
    public ICommand ToggleRunCommand { get; }
    public ICommand DuplicateTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand RefreshWindowsCommand { get; }
    public ICommand CaptureForegroundWindowCommand { get; }
    public ICommand AddActionCommand { get; }
    public ICommand RemoveActionCommand { get; }
    public ICommand MoveActionUpCommand { get; }
    public ICommand MoveActionDownCommand { get; }
    public ICommand UseCursorCommand { get; }
    public ICommand ToggleRecordingCommand { get; }
    public ICommand ClearStartShortcutCommand { get; }
    public ICommand ClearStopShortcutCommand { get; }

    public void Initialize(IntPtr windowHandle)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _hotKeys.Attach(windowHandle, Tasks.ToList());
        _scheduleTimer.Start();
    }

    public void Shutdown()
    {
        _runner.StopAll();
        _recorder.Stop();
        _scheduleTimer.Stop();
        _saveTimer.Stop();
        SaveTasks();
        _hotKeys.Dispose();
        WindowInputService.ShutdownForegroundWatch();
    }

    public void StopAllTasks() => _runner.StopAll();

    private void CreateTask()
    {
        var task = MacroTask.CreateNew();
        Tasks.Add(task);
        AttachTask(task);
        SelectedTask = task;
        NotifyTaskCounts();
        MarkDirty();
        RefreshHotKeys();
    }

    private void EditTask(MacroTask? task)
    {
        if (task is null)
        {
            return;
        }
        SelectedTask = task;
        Page = MainPage.Editor;
    }

    private void ToggleRun(MacroTask? task)
    {
        task ??= SelectedTask;
        if (task is null)
        {
            return;
        }

        if (task.Runtime.IsRunning)
        {
            _runner.Stop(task.Id);
        }
        else
        {
            _runner.Start(task);
        }
    }

    private void DuplicateTask(MacroTask? source)
    {
        if (source is null)
        {
            return;
        }

        var copy = CloneTask(source);
        Tasks.Add(copy);
        AttachTask(copy);
        SelectedTask = copy;
        NotifyTaskCounts();
        MarkDirty();
        RefreshHotKeys();
    }

    private void DeleteTask(MacroTask? task)
    {
        if (task is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"确定删除任务“{task.Name}”吗？此操作无法撤销。",
            "删除任务",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _runner.Stop(task.Id);
        Tasks.Remove(task);
        if (SelectedTask == task)
        {
            SelectedTask = null;
            Page = MainPage.Management;
        }
        NotifyTaskCounts();
        MarkDirty();
        RefreshHotKeys();
    }

    private void RefreshWindows()
    {
        var selectedTarget = SelectedTask?.Target;
        AvailableWindows.Clear();
        foreach (var window in WindowCatalogService.GetOpenWindows())
        {
            AvailableWindows.Add(window);
        }

        if (selectedTarget is not null)
        {
            SelectedTask!.Target = AvailableWindows.FirstOrDefault(window =>
                window.WindowHandle == selectedTarget.WindowHandle);
        }
    }

    private void CaptureForegroundWindow()
    {
        var window = WindowCatalogService.CaptureForegroundWindow();
        if (window is null || SelectedTask is null)
        {
            return;
        }
        SelectedTask.Target = window;
        if (!AvailableWindows.Any(item => item.WindowHandle == window.WindowHandle))
        {
            AvailableWindows.Add(window);
        }
    }

    private void AddAction(string? kind)
    {
        if (SelectedTask is null || !Enum.TryParse<ActionKind>(kind, out var actionKind))
        {
            return;
        }

        var action = actionKind switch
        {
            ActionKind.Keyboard => new MacroAction
            {
                Kind = ActionKind.Keyboard,
                Shortcut = new ShortcutGesture
                {
                    VirtualKey = 0x79,
                    Modifiers = ModifierKeys.None,
                    KeyLabel = "F10"
                }
            },
            ActionKind.MouseClick => new MacroAction
            {
                Kind = ActionKind.MouseClick,
                MouseButton = MouseButtonKind.Left
            },
            ActionKind.MouseMove => new MacroAction { Kind = ActionKind.MouseMove },
            ActionKind.Scroll => new MacroAction
            {
                Kind = ActionKind.Scroll,
                DeltaY = -120
            },
            _ => new MacroAction { Kind = ActionKind.Delay, DelaySeconds = 0.5 }
        };

        if (actionKind is ActionKind.MouseClick or ActionKind.MouseMove)
        {
            var cursor = WindowInputService.GetCursorPosition();
            action.X = cursor.X;
            action.Y = cursor.Y;
        }

        SelectedTask.Actions.Add(action);
        SelectedAction = action;
    }

    private void RemoveAction(MacroAction? action)
    {
        if (SelectedTask is null || action is null)
        {
            return;
        }
        SelectedTask.Actions.Remove(action);
        SelectedAction = SelectedTask.Actions.FirstOrDefault();
    }

    private void MoveAction(MacroAction? action, int offset)
    {
        if (SelectedTask is null || action is null)
        {
            return;
        }
        var index = SelectedTask.Actions.IndexOf(action);
        var targetIndex = index + offset;
        if (index < 0 || targetIndex < 0 || targetIndex >= SelectedTask.Actions.Count)
        {
            return;
        }
        SelectedTask.Actions.Move(index, targetIndex);
    }

    private void UseCursor(MacroAction? action)
    {
        if (action is null)
        {
            return;
        }
        var cursor = WindowInputService.GetCursorPosition();
        action.X = cursor.X;
        action.Y = cursor.Y;
    }

    private void ClearShortcut(bool isStart)
    {
        if (SelectedTask is null)
        {
            return;
        }
        if (isStart)
        {
            SelectedTask.Shortcuts.Start = null;
        }
        else
        {
            SelectedTask.Shortcuts.Stop = null;
        }
    }

    private void ToggleRecording()
    {
        if (_recorder.IsRecording)
        {
            var taskId = _recorder.RecordingTaskId;
            var actions = _recorder.Stop();
            var task = Tasks.FirstOrDefault(item => item.Id == taskId);
            if (task is not null)
            {
                foreach (var action in actions)
                {
                    task.Actions.Add(action);
                }
                SelectedAction = task.Actions.LastOrDefault();
                SelectedTask = task;
            }
            return;
        }

        if (SelectedTask is null || SelectedTask.Runtime.IsRunning)
        {
            return;
        }
        _recorder.Start(SelectedTask.Id);
    }

    private void HandleHotKey(Guid taskId, bool isStart)
    {
        var task = Tasks.FirstOrDefault(item => item.Id == taskId);
        if (task is null)
        {
            return;
        }

        if (isStart)
        {
            _runner.Start(task);
        }
        else
        {
            _runner.Stop(task.Id);
        }
    }

    private void AttachTask(MacroTask task)
    {
        if (!_attachedTasks.Add(task.Id))
        {
            return;
        }

        task.PropertyChanged += TaskPropertyChanged;
        task.Schedule.PropertyChanged += NestedPropertyChanged;
        task.Shortcuts.PropertyChanged += NestedPropertyChanged;
        task.Runtime.PropertyChanged += RuntimePropertyChanged;
        task.Actions.CollectionChanged += ActionsCollectionChanged;
        foreach (var action in task.Actions)
        {
            action.PropertyChanged += NestedPropertyChanged;
        }
    }

    private void ActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (MacroAction action in e.NewItems)
            {
                action.PropertyChanged += NestedPropertyChanged;
            }
        }
        if (e.OldItems is not null)
        {
            foreach (MacroAction action in e.OldItems)
            {
                action.PropertyChanged -= NestedPropertyChanged;
            }
        }

        if (sender is ObservableCollection<MacroAction> actions
            && SelectedTask?.Actions == actions)
        {
            SelectedTask.NotifyActionSummary();
        }
        MarkDirty();
    }

    private void NestedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SelectedTask?.NotifyActionSummary();
        OnPropertyChanged(nameof(ScheduledCount));
        OnPropertyChanged(nameof(ShortcutTaskCount));
        if (sender is TaskShortcuts)
        {
            SelectedTask?.NotifyShortcutSummary();
            OnPropertyChanged(nameof(ShortcutTaskCount));
            RefreshHotKeys();
        }
        MarkDirty();
    }

    private void TaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MacroTask task)
        {
            return;
        }

        MarkDirty();
    }

    private void RuntimePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RunningCount));
        (ToggleRunCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void MarkDirty()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTasks()
    {
        try
        {
            _repository.Save(Tasks);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"KeyTool task save failed: {ex}");
        }
    }

    private void RefreshHotKeys()
    {
        if (_initialized)
        {
            _hotKeys.Refresh(Tasks.ToList());
        }
    }

    private void NotifyTaskCounts()
    {
        OnPropertyChanged(nameof(TaskCount));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(ScheduledCount));
        OnPropertyChanged(nameof(ShortcutTaskCount));
        OnPropertyChanged(nameof(HasTasks));
    }

    private static MacroTask CloneTask(MacroTask source)
    {
        var copy = new MacroTask
        {
            Name = $"{source.Name} 副本",
            IsScheduleEnabled = source.IsScheduleEnabled,
            LoopCount = source.LoopCount,
            IsLoopInfinite = source.IsLoopInfinite,
            KeyboardInputMode = source.KeyboardInputMode,
            Target = source.Target is null
                ? null
                : new TargetWindow
                {
                    WindowHandle = source.Target.WindowHandle,
                    Title = source.Target.Title,
                    ProcessName = source.Target.ProcessName
                },
            Schedule = new TaskSchedule
            {
                Mode = source.Schedule.Mode,
                IntervalSeconds = source.Schedule.IntervalSeconds,
                Hour = source.Schedule.Hour,
                Minute = source.Schedule.Minute
            },
            Shortcuts = new TaskShortcuts
            {
                Start = source.Shortcuts.Start?.Clone(),
                Stop = source.Shortcuts.Stop?.Clone()
            }
        };

        foreach (var action in source.Actions)
        {
            copy.Actions.Add(new MacroAction
            {
                Kind = action.Kind,
                Shortcut = action.Shortcut?.Clone(),
                X = action.X,
                Y = action.Y,
                MouseButton = action.MouseButton,
                DeltaX = action.DeltaX,
                DeltaY = action.DeltaY,
                DelaySeconds = action.DelaySeconds
            });
        }
        return copy;
    }
}
