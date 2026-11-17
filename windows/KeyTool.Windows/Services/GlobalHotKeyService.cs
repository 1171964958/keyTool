using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using KeyTool.Windows.Models;

namespace KeyTool.Windows.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    private readonly Dictionary<int, (Guid TaskId, bool IsStart)> _hotKeys = new();
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private int _nextId = 20_000;

    public event Action<Guid, bool>? HotKeyPressed;

    public void Attach(IntPtr windowHandle, IReadOnlyList<MacroTask> tasks)
    {
        Detach();
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WindowProcedure);
        Refresh(tasks);
    }

    public void Refresh(IReadOnlyList<MacroTask> tasks)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        UnregisterAll();
        var owners = new Dictionary<ShortcutGesture, MacroTask>();
        foreach (var task in tasks)
        {
            task.HotKeyError = null;
            RegisterShortcut(task, task.Shortcuts.Start, true, owners);
            RegisterShortcut(task, task.Shortcuts.Stop, false, owners);
        }
    }

    public void Detach()
    {
        UnregisterAll();
        _source?.RemoveHook(WindowProcedure);
        _source = null;
        _windowHandle = IntPtr.Zero;
    }

    public void Dispose() => Detach();

    private void RegisterShortcut(
        MacroTask task,
        ShortcutGesture? shortcut,
        bool isStart,
        IDictionary<ShortcutGesture, MacroTask> owners)
    {
        if (shortcut is null)
        {
            return;
        }

        if (owners.ContainsKey(shortcut))
        {
            task.HotKeyError = $"快捷键 {shortcut.DisplayName} 已被其他任务使用。";
            return;
        }

        var id = _nextId++;
        if (!RegisterHotKey(
            _windowHandle,
            id,
            ToWin32Modifiers(shortcut.Modifiers) | ModNoRepeat,
            (uint)shortcut.VirtualKey))
        {
            task.HotKeyError = $"快捷键 {shortcut.DisplayName} 已被系统或其他应用占用。";
            return;
        }

        owners[shortcut] = task;
        _hotKeys[id] = (task.Id, isStart);
    }

    private IntPtr WindowProcedure(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotKey && _hotKeys.TryGetValue(wParam.ToInt32(), out var hotKey))
        {
            HotKeyPressed?.Invoke(hotKey.TaskId, hotKey.IsStart);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        foreach (var id in _hotKeys.Keys)
        {
            UnregisterHotKey(_windowHandle, id);
        }
        _hotKeys.Clear();
    }

    private static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= ModWin;
        return result;
    }
}
