using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Input;
using KeyTool.Windows.Infrastructure;

namespace KeyTool.Windows.Models;

public enum ActionKind
{
    Keyboard,
    MouseClick,
    MouseMove,
    Scroll,
    Delay
}

public enum MouseButtonKind
{
    Left,
    Right,
    Middle
}

public enum ScheduleMode
{
    Manual,
    Interval,
    Daily
}

public enum KeyboardInputMode
{
    Adaptive,
    BackgroundActivation,
    ForegroundCompatibility,
    BackgroundMessages
}

public sealed class ShortcutGesture : ObservableObject, IEquatable<ShortcutGesture>
{
    private int _virtualKey;
    private ModifierKeys _modifiers;
    private string _keyLabel = string.Empty;

    public int VirtualKey
    {
        get => _virtualKey;
        set
        {
            if (SetProperty(ref _virtualKey, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public ModifierKeys Modifiers
    {
        get => _modifiers;
        set
        {
            if (SetProperty(ref _modifiers, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string KeyLabel
    {
        get => _keyLabel;
        set
        {
            if (SetProperty(ref _keyLabel, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(string.IsNullOrWhiteSpace(KeyLabel) ? $"VK{VirtualKey}" : KeyLabel);
            return string.Join("+", parts);
        }
    }

    public ShortcutGesture Clone()
    {
        return new ShortcutGesture
        {
            VirtualKey = VirtualKey,
            Modifiers = Modifiers,
            KeyLabel = KeyLabel
        };
    }

    public bool Equals(ShortcutGesture? other)
    {
        return other is not null
            && VirtualKey == other.VirtualKey
            && Modifiers == other.Modifiers;
    }

    public override bool Equals(object? obj) => Equals(obj as ShortcutGesture);

    public override int GetHashCode() => HashCode.Combine(VirtualKey, Modifiers);
}

public sealed class TaskShortcuts : ObservableObject
{
    private ShortcutGesture? _start;
    private ShortcutGesture? _stop;

    public ShortcutGesture? Start
    {
        get => _start;
        set => SetProperty(ref _start, value);
    }

    public ShortcutGesture? Stop
    {
        get => _stop;
        set => SetProperty(ref _stop, value);
    }
}

public sealed class TaskSchedule : ObservableObject
{
    private ScheduleMode _mode = ScheduleMode.Manual;
    private double _intervalSeconds = 20;
    private int _hour = 9;
    private int _minute;

    public ScheduleMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsInterval));
                OnPropertyChanged(nameof(IsDaily));
            }
        }
    }

    public double IntervalSeconds
    {
        get => _intervalSeconds;
        set
        {
            if (double.IsFinite(value))
            {
                SetProperty(ref _intervalSeconds, Math.Clamp(value, 0.05, 86_400));
            }
        }
    }

    public int Hour
    {
        get => _hour;
        set => SetProperty(ref _hour, Math.Clamp(value, 0, 23));
    }

    public int Minute
    {
        get => _minute;
        set => SetProperty(ref _minute, Math.Clamp(value, 0, 59));
    }

    [JsonIgnore]
    public bool IsInterval => Mode == ScheduleMode.Interval;

    [JsonIgnore]
    public bool IsDaily => Mode == ScheduleMode.Daily;

    [JsonIgnore]
    public Array Modes => Enum.GetValues(typeof(ScheduleMode));

    public DateTime? NextRun(DateTime after)
    {
        return Mode switch
        {
            ScheduleMode.Manual => null,
            ScheduleMode.Interval => after.AddSeconds(Math.Max(0.05, IntervalSeconds)),
            ScheduleMode.Daily => NextDaily(after),
            _ => null
        };
    }

    private DateTime NextDaily(DateTime after)
    {
        var next = after.Date.AddHours(Hour).AddMinutes(Minute);
        return next <= after ? next.AddDays(1) : next;
    }
}

public sealed class TargetWindow : ObservableObject
{
    private long _windowHandle;
    private string _title = string.Empty;
    private string _processName = string.Empty;

    public long WindowHandle
    {
        get => _windowHandle;
        set => SetProperty(ref _windowHandle, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string ProcessName
    {
        get => _processName;
        set => SetProperty(ref _processName, value);
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(ProcessName)
        ? Title
        : $"{ProcessName} — {Title}";
}

public sealed class MacroAction : ObservableObject
{
    private ActionKind _kind;
    private ShortcutGesture? _shortcut;
    private double _x;
    private double _y;
    private MouseButtonKind _mouseButton;
    private int _deltaX;
    private int _deltaY;
    private double _delaySeconds = 0.5;

    public Guid Id { get; set; } = Guid.NewGuid();

    public ActionKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                NotifyPresentation();
            }
        }
    }

    public ShortcutGesture? Shortcut
    {
        get => _shortcut;
        set
        {
            if (SetProperty(ref _shortcut, value))
            {
                NotifyPresentation();
            }
        }
    }

    public double X
    {
        get => _x;
        set
        {
            if (SetProperty(ref _x, value)) NotifyPresentation();
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (SetProperty(ref _y, value)) NotifyPresentation();
        }
    }

    public MouseButtonKind MouseButton
    {
        get => _mouseButton;
        set
        {
            if (SetProperty(ref _mouseButton, value)) NotifyPresentation();
        }
    }

    public int DeltaX
    {
        get => _deltaX;
        set
        {
            if (SetProperty(ref _deltaX, value)) NotifyPresentation();
        }
    }

    public int DeltaY
    {
        get => _deltaY;
        set
        {
            if (SetProperty(ref _deltaY, value)) NotifyPresentation();
        }
    }

    public double DelaySeconds
    {
        get => _delaySeconds;
        set
        {
            if (double.IsFinite(value)
                && SetProperty(ref _delaySeconds, Math.Clamp(value, 0.01, 86_400)))
            {
                NotifyPresentation();
            }
        }
    }

    [JsonIgnore] public bool IsKeyboard => Kind == ActionKind.Keyboard;
    [JsonIgnore] public bool IsMouseClick => Kind == ActionKind.MouseClick;
    [JsonIgnore] public bool IsMouseMove => Kind == ActionKind.MouseMove;
    [JsonIgnore] public bool IsScroll => Kind == ActionKind.Scroll;
    [JsonIgnore] public bool IsDelay => Kind == ActionKind.Delay;

    [JsonIgnore]
    public Array MouseButtons => Enum.GetValues(typeof(MouseButtonKind));

    [JsonIgnore]
    public string Title => Kind switch
    {
        ActionKind.Keyboard => "按键",
        ActionKind.MouseClick => "鼠标点击",
        ActionKind.MouseMove => "鼠标移动",
        ActionKind.Scroll => "鼠标滚动",
        ActionKind.Delay => "等待",
        _ => "动作"
    };

    [JsonIgnore]
    public string Summary => Kind switch
    {
        ActionKind.Keyboard => Shortcut?.DisplayName ?? "未设置",
        ActionKind.MouseClick => $"{MouseButtonName(MouseButton)} @ {X:0}, {Y:0}",
        ActionKind.MouseMove => $"移动到 {X:0}, {Y:0}",
        ActionKind.Scroll => $"水平 {DeltaX}，垂直 {DeltaY}",
        ActionKind.Delay => $"{DelaySeconds:0.00} 秒",
        _ => string.Empty
    };

    private static string MouseButtonName(MouseButtonKind button)
    {
        return button switch
        {
            MouseButtonKind.Left => "左键",
            MouseButtonKind.Right => "右键",
            MouseButtonKind.Middle => "中键",
            _ => "左键"
        };
    }

    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsKeyboard));
        OnPropertyChanged(nameof(IsMouseClick));
        OnPropertyChanged(nameof(IsMouseMove));
        OnPropertyChanged(nameof(IsScroll));
        OnPropertyChanged(nameof(IsDelay));
    }
}

public sealed class TaskRuntimeState : ObservableObject
{
    private bool _isRunning;
    private int _currentLoop;
    private int? _totalLoops;
    private int _currentAction;
    private int _totalActions;
    private string? _errorMessage;

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public int CurrentLoop
    {
        get => _currentLoop;
        set
        {
            if (SetProperty(ref _currentLoop, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public int? TotalLoops
    {
        get => _totalLoops;
        set
        {
            if (SetProperty(ref _totalLoops, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public int CurrentAction
    {
        get => _currentAction;
        set
        {
            if (SetProperty(ref _currentAction, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public int TotalActions
    {
        get => _totalActions;
        set
        {
            if (SetProperty(ref _totalActions, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    [JsonIgnore]
    public string StatusText => IsRunning ? "执行中" : "空闲";

    [JsonIgnore]
    public string ProgressText
    {
        get
        {
            var loop = TotalLoops.HasValue
                ? $"第 {CurrentLoop}/{TotalLoops} 轮"
                : $"第 {CurrentLoop} 轮 · 无限循环";
            return $"{loop} · 动作 {CurrentAction}/{TotalActions}";
        }
    }

    public void Reset()
    {
        IsRunning = false;
        CurrentLoop = 0;
        TotalLoops = null;
        CurrentAction = 0;
        TotalActions = 0;
    }
}

public sealed class MacroTask : ObservableObject
{
    private string _name = "新任务";
    private TargetWindow? _target;
    private KeyboardInputMode _keyboardInputMode = KeyboardInputMode.ForegroundCompatibility;
    private TaskSchedule _schedule = new();
    private bool _isScheduleEnabled;
    private int _loopCount = 1;
    private bool _isLoopInfinite;
    private TaskShortcuts _shortcuts = new();
    private DateTime _updatedAt = DateTime.Now;
    private string? _hotKeyError;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                Touch();
            }
        }
    }

    public TargetWindow? Target
    {
        get => _target;
        set
        {
            if (SetProperty(ref _target, value))
            {
                NotifySummary();
                Touch();
            }
        }
    }

    public bool UseForegroundKeyboardInput
    {
        get => KeyboardInputMode == KeyboardInputMode.ForegroundCompatibility;
        set
        {
            if (value)
            {
                KeyboardInputMode = KeyboardInputMode.ForegroundCompatibility;
            }
            else if (KeyboardInputMode == KeyboardInputMode.ForegroundCompatibility)
            {
                KeyboardInputMode = KeyboardInputMode.BackgroundActivation;
            }
        }
    }

    public KeyboardInputMode KeyboardInputMode
    {
        get => _keyboardInputMode;
        set
        {
            var normalized = value == KeyboardInputMode.BackgroundActivation
                ? KeyboardInputMode.Adaptive
                : value;
            if (SetProperty(ref _keyboardInputMode, normalized))
            {
                OnPropertyChanged(nameof(UseForegroundKeyboardInput));
                Touch();
            }
        }
    }

    [JsonIgnore]
    public Array KeyboardInputModes => new[]
    {
        KeyboardInputMode.Adaptive,
        KeyboardInputMode.ForegroundCompatibility,
        KeyboardInputMode.BackgroundMessages
    };

    public ObservableCollection<MacroAction> Actions { get; set; } = new();

    public TaskSchedule Schedule
    {
        get => _schedule;
        set
        {
            if (SetProperty(ref _schedule, value))
            {
                NotifySummary();
                Touch();
            }
        }
    }

    public bool IsScheduleEnabled
    {
        get => _isScheduleEnabled;
        set
        {
            if (SetProperty(ref _isScheduleEnabled, value))
            {
                NotifySummary();
                Touch();
            }
        }
    }

    public int LoopCount
    {
        get => _loopCount;
        set
        {
            if (SetProperty(ref _loopCount, Math.Clamp(value, 1, 10_000)))
            {
                NotifySummary();
                Touch();
            }
        }
    }

    public bool IsLoopInfinite
    {
        get => _isLoopInfinite;
        set
        {
            if (SetProperty(ref _isLoopInfinite, value))
            {
                NotifySummary();
                Touch();
            }
        }
    }

    public TaskShortcuts Shortcuts
    {
        get => _shortcuts;
        set
        {
            if (SetProperty(ref _shortcuts, value))
            {
                Touch();
            }
        }
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    [JsonIgnore]
    public TaskRuntimeState Runtime { get; } = new();

    [JsonIgnore]
    public string? HotKeyError
    {
        get => _hotKeyError;
        set => SetProperty(ref _hotKeyError, value);
    }

    [JsonIgnore]
    public string Summary => $"{Actions.Count} 个动作 · "
        + (IsLoopInfinite ? "无限循环" : $"{LoopCount} 次")
        + $" · {Target?.ProcessName ?? "未选择窗口"}";

    [JsonIgnore]
    public string ScheduleSummary
    {
        get
        {
            if (!IsScheduleEnabled || Schedule.Mode == ScheduleMode.Manual)
            {
                return "手动触发";
            }

            return Schedule.Mode == ScheduleMode.Interval
                ? $"每 {Schedule.IntervalSeconds:0.##} 秒"
                : $"每天 {Schedule.Hour:00}:{Schedule.Minute:00}";
        }
    }

    [JsonIgnore]
    public string ShortcutSummary
    {
        get
        {
            var shortcuts = new List<string>();
            if (Shortcuts.Start is not null)
            {
                shortcuts.Add($"启动 {Shortcuts.Start.DisplayName}");
            }
            if (Shortcuts.Stop is not null)
            {
                shortcuts.Add($"停止 {Shortcuts.Stop.DisplayName}");
            }
            return shortcuts.Count == 0
                ? "未设置快捷键"
                : string.Join("  ·  ", shortcuts);
        }
    }

    public static MacroTask CreateNew()
    {
        var task = new MacroTask();
        task.Actions.Add(new MacroAction
        {
            Kind = ActionKind.Delay,
            DelaySeconds = 0.2
        });
        return task;
    }

    public void NotifyActionSummary()
    {
        NotifySummary();
    }

    public void NotifyShortcutSummary()
    {
        OnPropertyChanged(nameof(ShortcutSummary));
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ScheduleSummary));
    }

    private void Touch()
    {
        UpdatedAt = DateTime.Now;
    }
}
