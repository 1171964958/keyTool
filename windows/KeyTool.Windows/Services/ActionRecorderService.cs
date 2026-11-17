using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using KeyTool.Windows.Infrastructure;
using KeyTool.Windows.Models;

namespace KeyTool.Windows.Services;

public sealed class ActionRecorderService : ObservableObject
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;
    private const int WmMouseMove = 0x0200;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public Point Position;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        HookProcedure procedure,
        IntPtr moduleHandle,
        uint threadId
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int code,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId
    );

    private readonly HookProcedure _keyboardProcedure;
    private readonly HookProcedure _mouseProcedure;
    private readonly List<MacroAction> _actions = new();
    private readonly Stopwatch _clock = new();
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private double? _lastEventTime;
    private double _lastMoveTime = -1;
    private Point? _lastMovePoint;
    private long _lastQueuedMoveTick;
    private bool _isRecording;
    private int _recordedCount;
    private Guid? _recordingTaskId;
    private string? _errorMessage;

    public ActionRecorderService()
    {
        _keyboardProcedure = KeyboardHook;
        _mouseProcedure = MouseHook;
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set => SetProperty(ref _isRecording, value);
    }

    public int RecordedCount
    {
        get => _recordedCount;
        private set => SetProperty(ref _recordedCount, value);
    }

    public Guid? RecordingTaskId
    {
        get => _recordingTaskId;
        private set => SetProperty(ref _recordingTaskId, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public void Start(Guid taskId)
    {
        if (IsRecording)
        {
            return;
        }

        _actions.Clear();
        ErrorMessage = null;
        _lastEventTime = null;
        _lastMoveTime = -1;
        _lastMovePoint = null;
        RecordingTaskId = taskId;
        RecordedCount = 0;
        IsRecording = true;
        _clock.Restart();

        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(
            WhKeyboardLl,
            _keyboardProcedure,
            module,
            0
        );
        _mouseHook = SetWindowsHookEx(
            WhMouseLl,
            _mouseProcedure,
            module,
            0
        );

        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
        {
            ErrorMessage = "无法安装全局输入录制钩子，请尝试以管理员身份运行。";
            StopInternal();
        }
    }

    public IReadOnlyList<MacroAction> Stop()
    {
        var result = _actions.ToArray();
        StopInternal();
        return result;
    }

    private void StopInternal()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        _clock.Stop();
        _actions.Clear();
        RecordedCount = 0;
        RecordingTaskId = null;
        IsRecording = false;
    }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && IsTargetForeground())
        {
            var message = wParam.ToInt32();
            if (message is WmKeyDown or WmSysKeyDown)
            {
                var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
                if (!IsModifierKey((int)data.VirtualKey))
                {
                    var modifiers = CurrentModifiers();
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        var gesture = new ShortcutGesture
                        {
                            VirtualKey = (int)data.VirtualKey,
                            Modifiers = modifiers,
                            KeyLabel = VirtualKeyLabel.ForVirtualKey((int)data.VirtualKey)
                        };
                        AppendAction(new MacroAction
                        {
                            Kind = ActionKind.Keyboard,
                            Shortcut = gesture
                        });
                    });
                }
            }
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && IsTargetForeground())
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<MouseHookData>(lParam);
            if (IsOwnWindowAt(data.Position))
            {
                return CallNextHookEx(_mouseHook, code, wParam, lParam);
            }

            if (message == WmMouseMove)
            {
                var now = Environment.TickCount64;
                var previous = Interlocked.Read(ref _lastQueuedMoveTick);
                if (now - previous < 200)
                {
                    return CallNextHookEx(_mouseHook, code, wParam, lParam);
                }
                Interlocked.Exchange(ref _lastQueuedMoveTick, now);
            }

            if (message is WmLButtonDown or WmRButtonDown or WmMButtonDown
                or WmMouseWheel or WmMouseHWheel or WmMouseMove)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                    CaptureMouse(message, data)
                );
            }
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void CaptureMouse(int message, MouseHookData data)
    {
        if (message == WmMouseMove)
        {
            var ellapsed = _clock.Elapsed.TotalSeconds;
            if (ellapsed - _lastMoveTime < 0.2)
            {
                return;
            }
            if (_lastMovePoint is { } previous
                && Math.Abs(previous.X - data.Position.X)
                    + Math.Abs(previous.Y - data.Position.Y) < 10)
            {
                return;
            }

            _lastMoveTime = ellapsed;
            _lastMovePoint = data.Position;
            AppendAction(new MacroAction
            {
                Kind = ActionKind.MouseMove,
                X = data.Position.X,
                Y = data.Position.Y
            });
            return;
        }

        if (message is WmMouseWheel or WmMouseHWheel)
        {
            var delta = (short)((data.MouseData >> 16) & 0xFFFF);
            AppendAction(new MacroAction
            {
                Kind = ActionKind.Scroll,
                DeltaX = message == WmMouseHWheel ? -delta : 0,
                DeltaY = message == WmMouseWheel ? delta : 0
            });
            return;
        }

        var button = message switch
        {
            WmRButtonDown => MouseButtonKind.Right,
            WmMButtonDown => MouseButtonKind.Middle,
            _ => MouseButtonKind.Left
        };
        AppendAction(new MacroAction
        {
            Kind = ActionKind.MouseClick,
            MouseButton = button,
            X = data.Position.X,
            Y = data.Position.Y
        });
    }

    private void AppendAction(MacroAction action)
    {
        if (!IsRecording)
        {
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (_lastEventTime.HasValue)
        {
            var elapsed = now - _lastEventTime.Value;
            if (elapsed >= 0.05)
            {
                _actions.Add(new MacroAction
                {
                    Kind = ActionKind.Delay,
                    DelaySeconds = Math.Clamp(elapsed, 0.05, 3_600)
                });
            }
        }
        _actions.Add(action);
        _lastEventTime = now;
        RecordedCount = _actions.Count;
    }

    private static bool IsTargetForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }
        GetWindowThreadProcessId(foreground, out var processId);
        return processId != (uint)Environment.ProcessId;
    }

    private static bool IsOwnWindowAt(Point point)
    {
        var window = WindowFromPoint(point);
        if (window == IntPtr.Zero)
        {
            return false;
        }
        GetWindowThreadProcessId(window, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private static bool IsModifierKey(int virtualKey)
    {
        return virtualKey is 0x10 or 0x11 or 0x12
            or 0x5B or 0x5C
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
    }

    private static ModifierKeys CurrentModifiers()
    {
        var modifiers = ModifierKeys.None;
        if ((GetAsyncKeyState(VkControl) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Control;
        }
        if ((GetAsyncKeyState(VkShift) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Shift;
        }
        if ((GetAsyncKeyState(VkAlt) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Alt;
        }
        if ((GetAsyncKeyState(VkLWin) & 0x8000) != 0
            || (GetAsyncKeyState(VkRWin) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Windows;
        }
        return modifiers;
    }
}
