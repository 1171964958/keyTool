using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KeyTool.Windows.Models;

namespace KeyTool.Windows.Services;

public static class WindowInputService
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmActivate = 0x0006;
    private const uint WmSetFocus = 0x0007;
    private const uint WmKillFocus = 0x0008;
    private const uint WmActivateApp = 0x001C;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WmMouseWheel = 0x020A;

    private const uint GaRoot = 2;
    private const uint SwRestore = 9;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventMove = 0x0001;
    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint WindowMessageTimeoutMilliseconds = 200;
    private const int WaActive = 1;
    private const int WaInactive = 0;
    private const int VkShift = 0xA0;
    private const int VkControl = 0xA2;
    private const int VkAlt = 0xA4;
    private const int VkLeftWindows = 0x5B;
    private const int MkLeftButton = 0x0001;
    private const int MkRightButton = 0x0002;
    private const int MkMiddleButton = 0x0010;

    private delegate void WinEventDelegate(
        IntPtr hookHandle,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime);

    private static readonly ConcurrentDictionary<IntPtr, byte> BackgroundTargets = new();
    private static readonly ConcurrentDictionary<IntPtr, DateTime> RecoveryUntil = new();
    private static readonly ConcurrentDictionary<(IntPtr Target, int Key, bool IsAlt), byte>
        TrackedKeys = new();
    private static readonly ConcurrentDictionary<
        (IntPtr Target, MouseButtonKind Button),
        (IntPtr Window, IntPtr Point)>
        TrackedMouseButtons = new();
    private static readonly object ForegroundHookLock = new();
    private static readonly object TraceLock = new();
    private static readonly SemaphoreSlim AdaptiveInputGate = new(1, 1);
    private static WinEventDelegate? _foregroundHookDelegate;
    private static IntPtr _foregroundHook;
    private static Thread? _foregroundHookThread;
    private static uint _foregroundHookThreadId;
    private static ManualResetEventSlim? _foregroundHookReady;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr ActiveWindow;
        public IntPtr FocusWindow;
        public IntPtr CaptureWindow;
        public IntPtr MenuOwnerWindow;
        public IntPtr MoveSizeWindow;
        public IntPtr CaretWindow;
        public Rect CaretRectangle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Cursor;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint virtualKey, uint mapType);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr windowHandle, ref Point point);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        IntPtr moduleHandle,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(
        uint threadId,
        ref GuiThreadInfo threadInfo);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(
        uint attachThreadId,
        uint attachToThreadId,
        bool attach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags,
        int x,
        int y,
        uint data,
        UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        Input[] inputs,
        int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeoutMilliseconds,
        out UIntPtr result);

    private sealed class ForegroundInputSession : IDisposable
    {
        private readonly uint _currentThreadId;
        private readonly uint _foregroundThreadId;
        private readonly uint _targetThreadId;
        private readonly bool _attachedPair;
        private readonly bool _attachedTarget;

        public ForegroundInputSession(IntPtr targetWindow)
        {
            _currentThreadId = GetCurrentThreadId();
            OriginalForeground = GetForegroundWindow();
            _foregroundThreadId = OriginalForeground == IntPtr.Zero
                ? 0
                : GetWindowThreadProcessId(OriginalForeground, out _);
            _targetThreadId = GetWindowThreadProcessId(targetWindow, out _);
            _attachedPair = _foregroundThreadId != 0
                && _targetThreadId != 0
                && _foregroundThreadId != _targetThreadId
                && AttachThreadInput(
                    _foregroundThreadId,
                    _targetThreadId,
                    true);
            _attachedTarget = _targetThreadId != 0
                && _targetThreadId != _currentThreadId
                && _targetThreadId != _foregroundThreadId
                && AttachThreadInput(_currentThreadId, _targetThreadId, true);
        }

        public IntPtr OriginalForeground { get; }

        public bool Activate(IntPtr windowHandle)
        {
            if (!IsWindow(windowHandle))
            {
                return false;
            }

            if (IsIconic(windowHandle))
            {
                ShowWindow(windowHandle, SwRestore);
            }

            BringWindowToTop(windowHandle);
            SetForegroundWindow(windowHandle);
            return IsWindowInForeground(windowHandle);
        }

        public bool Restore()
        {
            if (OriginalForeground == IntPtr.Zero || !IsWindow(OriginalForeground))
            {
                return false;
            }

            BringWindowToTop(OriginalForeground);
            SetForegroundWindow(OriginalForeground);
            return IsWindowInForeground(OriginalForeground);
        }

        public void Dispose()
        {
            if (_attachedTarget)
            {
                AttachThreadInput(_currentThreadId, _targetThreadId, false);
            }
            if (_attachedPair)
            {
                AttachThreadInput(
                    _foregroundThreadId,
                    _targetThreadId,
                    false);
            }
        }
    }

    public static async Task ExecuteAsync(
        MacroAction action,
        IntPtr windowHandle,
        KeyboardInputMode keyboardInputMode,
        CancellationToken cancellationToken)
    {
        if (keyboardInputMode == KeyboardInputMode.BackgroundActivation)
        {
            keyboardInputMode = KeyboardInputMode.Adaptive;
        }
        if (keyboardInputMode == KeyboardInputMode.Adaptive)
        {
            WatchTarget(windowHandle);
        }
        else
        {
            UnwatchTarget(windowHandle);
        }
        Trace(
            $"action={action.Kind} mode={keyboardInputMode} "
            + $"target=0x{windowHandle.ToInt64():X} "
            + $"foreground=0x{GetForegroundWindow().ToInt64():X} "
            + $"targetForeground={IsWindowInForeground(windowHandle)}");

        if (keyboardInputMode == KeyboardInputMode.Adaptive)
        {
            await WaitForRecoveryWindowAsync(windowHandle, cancellationToken);
        }

        var coordinateAdaptiveInput = keyboardInputMode == KeyboardInputMode.Adaptive
            && action.Kind is ActionKind.Keyboard or ActionKind.MouseClick;
        if (coordinateAdaptiveInput)
        {
            await AdaptiveInputGate.WaitAsync(cancellationToken);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsWindow(windowHandle))
            {
                throw new InvalidOperationException("目标窗口已关闭，请重新选择目标窗口。");
            }

            switch (action.Kind)
            {
                case ActionKind.Keyboard:
                    if (keyboardInputMode == KeyboardInputMode.ForegroundCompatibility
                        || (keyboardInputMode == KeyboardInputMode.Adaptive
                            && IsWindowInForeground(windowHandle)))
                    {
                        await SendKeyboardInForegroundAsync(
                            action,
                            windowHandle,
                            cancellationToken);
                    }
                    else if (keyboardInputMode == KeyboardInputMode.Adaptive)
                    {
                        await SendKeyboardWithBackgroundActivationAsync(
                            action,
                            windowHandle,
                            cancellationToken);
                    }
                    else
                    {
                        await SendKeyboardMessageAsync(
                            action,
                            windowHandle,
                            cancellationToken);
                    }
                    break;
                case ActionKind.MouseClick:
                    if (keyboardInputMode == KeyboardInputMode.ForegroundCompatibility
                        || (keyboardInputMode == KeyboardInputMode.Adaptive
                            && IsWindowInForeground(windowHandle)))
                    {
                        await SendMouseClickInForegroundAsync(
                            action,
                            windowHandle,
                            cancellationToken);
                    }
                    else if (keyboardInputMode == KeyboardInputMode.Adaptive)
                    {
                        await SendMouseClickWithBackgroundActivationAsync(
                            action,
                            windowHandle,
                            cancellationToken);
                    }
                    else
                    {
                        await SendMouseClickMessageAsync(
                            action,
                            windowHandle,
                            cancellationToken);
                    }
                    break;
                case ActionKind.MouseMove:
                    SendMouseMove(action, windowHandle);
                    break;
                case ActionKind.Scroll:
                    SendScroll(action, windowHandle);
                    break;
                case ActionKind.Delay:
                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Max(0, action.DelaySeconds)),
                        cancellationToken
                    );
                    break;
            }
        }
        finally
        {
            if (coordinateAdaptiveInput)
            {
                AdaptiveInputGate.Release();
            }
        }
    }

    public static (double X, double Y) GetCursorPosition()
    {
        if (!GetCursorPos(out var point))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return (point.X, point.Y);
    }

    public static void ShutdownForegroundWatch()
    {
        Thread? hookThread;
        uint hookThreadId;
        lock (ForegroundHookLock)
        {
            hookThread = _foregroundHookThread;
            hookThreadId = _foregroundHookThreadId;
            _foregroundHookThread = null;
            _foregroundHookThreadId = 0;
        }

        if (hookThread?.IsAlive == true && hookThreadId != 0)
        {
            PostThreadMessage(hookThreadId, 0x0012, UIntPtr.Zero, IntPtr.Zero);
            hookThread.Join(TimeSpan.FromSeconds(1));
        }

        BackgroundTargets.Clear();
        RecoveryUntil.Clear();
        TrackedKeys.Clear();
        TrackedMouseButtons.Clear();
        _foregroundHookReady?.Dispose();
        _foregroundHookReady = null;
    }

    private static void Trace(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeyTool");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "input.log");
            lock (TraceLock)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                {
                    File.Delete(path);
                }
                File.AppendAllText(
                    path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never interrupt input execution.
        }
    }

    private static async Task SendKeyboardMessageAsync(
        MacroAction action,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        var shortcut = action.Shortcut
            ?? throw new InvalidOperationException("按键动作尚未设置具体按键。");

        var modifierKeys = GetModifierKeys(shortcut);
        var messageWindow = GetKeyboardMessageWindow(windowHandle);

        foreach (var modifierKey in modifierKeys)
        {
            PostKey(messageWindow, modifierKey, keyUp: false, isAlt: false);
        }

        var isAlt = shortcut.Modifiers.HasFlag(ModifierKeys.Alt);
        PostKey(messageWindow, shortcut.VirtualKey, keyUp: false, isAlt: isAlt);
        await Task.Delay(12, cancellationToken);
        PostKey(messageWindow, shortcut.VirtualKey, keyUp: true, isAlt: isAlt);

        for (var index = modifierKeys.Count - 1; index >= 0; index--)
        {
            PostKey(messageWindow, modifierKeys[index], keyUp: true, isAlt: false);
        }
    }

    private static async Task SendKeyboardWithBackgroundActivationAsync(
        MacroAction action,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        var shortcut = action.Shortcut
            ?? throw new InvalidOperationException("按键动作尚未设置具体按键。");

        if (IsWindowInForeground(windowHandle))
        {
            await SendKeyboardInForegroundAsync(
                action,
                windowHandle,
                cancellationToken);
            return;
        }

        var previousForeground = GetForegroundWindow();
        var messageWindow = GetKeyboardMessageWindow(windowHandle);
        var targetThreadId = GetWindowThreadProcessId(windowHandle, out _);
        var activationAttempted = false;
        WatchBackgroundTarget(windowHandle);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            activationAttempted = true;
            ActivateWindowWithMessages(
                windowHandle,
                messageWindow,
                previousForeground,
                targetThreadId);
            await SendKeyboardMessagesAsync(
                shortcut,
                messageWindow,
                cancellationToken);
        }
        finally
        {
            if (activationAttempted)
            {
                RestoreWindowAfterFakeActivation(
                    windowHandle,
                    messageWindow,
                    previousForeground,
                    targetThreadId);
            }
        }
    }

    private static void ActivateWindowWithMessages(
        IntPtr windowHandle,
        IntPtr messageWindow,
        IntPtr previousForeground,
        uint targetThreadId)
    {
        SendWindowMessage(
            windowHandle,
            WmActivateApp,
            new IntPtr(1),
            new IntPtr(targetThreadId),
            "目标窗口没有响应伪激活消息。");
        SendWindowMessage(
            windowHandle,
            WmActivate,
            new IntPtr(WaActive),
            previousForeground,
            "目标窗口没有响应伪激活消息。");
        SendWindowMessage(
            messageWindow,
            WmSetFocus,
            previousForeground,
            IntPtr.Zero,
            "目标窗口没有响应焦点消息。");
    }

    private static void RestoreWindowAfterFakeActivation(
        IntPtr windowHandle,
        IntPtr messageWindow,
        IntPtr previousForeground,
        uint targetThreadId)
    {
        if (!IsWindowInForeground(windowHandle))
        {
            TrySendWindowMessage(
                messageWindow,
                WmKillFocus,
                windowHandle,
                IntPtr.Zero);
            TrySendWindowMessage(
                windowHandle,
                WmActivate,
                new IntPtr(WaInactive),
                previousForeground);
            TrySendWindowMessage(
                windowHandle,
                WmActivateApp,
                IntPtr.Zero,
                new IntPtr(targetThreadId));
        }

        if (IsWindowInForeground(windowHandle))
        {
            RestoreRealActivationState(
                windowHandle,
                messageWindow,
                previousForeground,
                targetThreadId);
        }
    }

    private static void RestoreRealActivationState(
        IntPtr windowHandle,
        IntPtr messageWindow,
        IntPtr previousForeground,
        uint targetThreadId)
    {
        TrySendWindowMessage(
            windowHandle,
            WmActivateApp,
            new IntPtr(1),
            new IntPtr(targetThreadId));
        TrySendWindowMessage(
            windowHandle,
            WmActivate,
            new IntPtr(WaActive),
            previousForeground);
        TrySendWindowMessage(
            messageWindow,
            WmSetFocus,
            previousForeground,
            IntPtr.Zero);
    }

    private static async Task SendKeyboardMessagesAsync(
        ShortcutGesture shortcut,
        IntPtr messageWindow,
        CancellationToken cancellationToken)
    {
        var modifierKeys = GetModifierKeys(shortcut);
        var pressedModifiers = new List<int>(modifierKeys.Count);
        var isAlt = shortcut.Modifiers.HasFlag(ModifierKeys.Alt);
        var primaryKeyDown = false;
        var targetWindow = GetAncestor(messageWindow, GaRoot);

        try
        {
            foreach (var modifierKey in modifierKeys)
            {
                if (IsWindowInForeground(targetWindow))
                {
                    return;
                }
                TrackedKeys.TryAdd((targetWindow, modifierKey, false), 0);
                pressedModifiers.Add(modifierKey);
                SendKeyMessage(
                    messageWindow,
                    modifierKey,
                    keyUp: false,
                    isAlt: false);
            }

            if (IsWindowInForeground(targetWindow))
            {
                return;
            }
            TrackedKeys.TryAdd((targetWindow, shortcut.VirtualKey, isAlt), 0);
            primaryKeyDown = true;
            SendKeyMessage(
                messageWindow,
                shortcut.VirtualKey,
                keyUp: false,
                isAlt: isAlt);
            await Task.Delay(12, cancellationToken);
            if (IsWindowInForeground(targetWindow))
            {
                return;
            }
            SendKeyMessage(
                messageWindow,
                shortcut.VirtualKey,
                keyUp: true,
                isAlt: isAlt);
            primaryKeyDown = false;
            TrackedKeys.TryRemove(
                (targetWindow, shortcut.VirtualKey, isAlt),
                out _);

            for (var index = pressedModifiers.Count - 1; index >= 0; index--)
            {
                SendKeyMessage(
                    messageWindow,
                    pressedModifiers[index],
                    keyUp: true,
                    isAlt: false);
                TrackedKeys.TryRemove(
                    (targetWindow, pressedModifiers[index], false),
                    out _);
            }
        }
        finally
        {
            if (primaryKeyDown)
            {
                if (TrySendKeyMessage(
                    messageWindow,
                    shortcut.VirtualKey,
                    keyUp: true,
                    isAlt: isAlt))
                {
                    TrackedKeys.TryRemove(
                        (targetWindow, shortcut.VirtualKey, isAlt),
                        out _);
                }
            }
            for (var index = pressedModifiers.Count - 1; index >= 0; index--)
            {
                if (TrySendKeyMessage(
                    messageWindow,
                    pressedModifiers[index],
                    keyUp: true,
                    isAlt: false))
                {
                    TrackedKeys.TryRemove(
                        (targetWindow, pressedModifiers[index], false),
                        out _);
                }
            }
        }
    }

    private static async Task SendKeyboardInForegroundAsync(
        MacroAction action,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        var shortcut = action.Shortcut
            ?? throw new InvalidOperationException("按键动作尚未设置具体按键。");
        var originalForeground = GetForegroundWindow();
        var targetWasForeground = IsWindowInForeground(windowHandle);
        ForegroundInputSession? inputSession = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!targetWasForeground)
            {
                inputSession = new ForegroundInputSession(windowHandle);
                if (!inputSession.Activate(windowHandle))
                {
                    await Task.Delay(30, cancellationToken);
                    inputSession.Activate(windowHandle);
                }
            }

            if (!IsWindowInForeground(windowHandle))
            {
                throw new InvalidOperationException(
                    "无法激活目标窗口。若目标以管理员身份运行，请同样以管理员身份运行 KeyTool。");
            }

            SendKeyboardInput(shortcut);
            await Task.Delay(targetWasForeground ? 12 : 40, cancellationToken);
        }
        finally
        {
            if (inputSession is not null)
            {
                if (originalForeground != IntPtr.Zero
                    && IsWindowInForeground(windowHandle))
                {
                    inputSession.Restore();
                }
                inputSession.Dispose();
            }
        }
    }

    private static void SendKeyboardInput(ShortcutGesture shortcut)
    {
        var modifierKeys = GetModifierKeys(shortcut);
        var inputs = new List<Input>(modifierKeys.Count * 2 + 2);
        foreach (var modifierKey in modifierKeys)
        {
            inputs.Add(CreateKeyboardInput(modifierKey, keyUp: false));
        }

        inputs.Add(CreateKeyboardInput(shortcut.VirtualKey, keyUp: false));
        inputs.Add(CreateKeyboardInput(shortcut.VirtualKey, keyUp: true));

        for (var index = modifierKeys.Count - 1; index >= 0; index--)
        {
            inputs.Add(CreateKeyboardInput(modifierKeys[index], keyUp: true));
        }

        var inputArray = inputs.ToArray();
        var sent = SendInput(
            (uint)inputArray.Length,
            inputArray,
            Marshal.SizeOf<Input>());
        if (sent != inputArray.Length)
        {
            var error = Marshal.GetLastWin32Error();
            throw error == 0
                ? new InvalidOperationException("系统拒绝了键盘输入，请检查目标程序权限。")
                : new Win32Exception(error);
        }
    }

    private static Input CreateKeyboardInput(int virtualKey, bool keyUp)
    {
        var flags = keyUp ? KeyEventKeyUp : 0;
        if (IsExtendedKey(virtualKey))
        {
            flags |= KeyEventExtended;
        }

        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = (ushort)virtualKey,
                    ScanCode = (ushort)MapVirtualKey((uint)virtualKey, 0),
                    Flags = flags
                }
            }
        };
    }

    private static List<int> GetModifierKeys(ShortcutGesture shortcut)
    {
        var modifierKeys = new List<int>();
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Control)) modifierKeys.Add(VkControl);
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Shift)) modifierKeys.Add(VkShift);
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Alt)) modifierKeys.Add(VkAlt);
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Windows)) modifierKeys.Add(VkLeftWindows);
        return modifierKeys;
    }

    private static IntPtr GetKeyboardMessageWindow(IntPtr windowHandle)
    {
        var threadId = GetWindowThreadProcessId(windowHandle, out _);
        if (threadId == 0)
        {
            return windowHandle;
        }

        var threadInfo = new GuiThreadInfo
        {
            Size = Marshal.SizeOf<GuiThreadInfo>()
        };
        var rootWindow = GetAncestor(windowHandle, GaRoot);
        if (!GetGUIThreadInfo(threadId, ref threadInfo)
            || threadInfo.FocusWindow == IntPtr.Zero
            || GetAncestor(threadInfo.FocusWindow, GaRoot) != rootWindow)
        {
            return windowHandle;
        }

        return threadInfo.FocusWindow;
    }

    private static bool IsWindowInForeground(IntPtr windowHandle)
    {
        var foregroundWindow = GetForegroundWindow();
        return foregroundWindow != IntPtr.Zero
            && GetAncestor(foregroundWindow, GaRoot) == GetAncestor(windowHandle, GaRoot);
    }

    public static void WatchTarget(IntPtr windowHandle)
    {
        if (IsWindow(windowHandle))
        {
            WatchBackgroundTarget(windowHandle);
        }
    }

    public static void UnwatchTarget(IntPtr windowHandle)
    {
        if (IsWindow(windowHandle))
        {
            BackgroundTargets.TryRemove(GetAncestor(windowHandle, GaRoot), out _);
        }
    }

    private static void WatchBackgroundTarget(IntPtr windowHandle)
    {
        BackgroundTargets[GetAncestor(windowHandle, GaRoot)] = 0;
        lock (ForegroundHookLock)
        {
            if (_foregroundHookThread?.IsAlive == true)
            {
                return;
            }

            _foregroundHookReady?.Dispose();
            _foregroundHookReady = new ManualResetEventSlim(false);
            _foregroundHookThread = new Thread(ForegroundHookThreadMain)
            {
                IsBackground = true,
                Name = "KeyTool foreground input watcher"
            };
            _foregroundHookThread.Start();
        }

        _foregroundHookReady?.Wait(TimeSpan.FromSeconds(1));
    }

    private static void ForegroundHookThreadMain()
    {
        _foregroundHookThreadId = GetCurrentThreadId();
        _foregroundHookDelegate = ForegroundWindowChanged;
        _foregroundHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _foregroundHookDelegate,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        _foregroundHookReady?.Set();

        try
        {
            while (GetMessage(
                out var message,
                IntPtr.Zero,
                0,
                0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            if (_foregroundHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
            _foregroundHookDelegate = null;
        }
    }

    private static void ForegroundWindowChanged(
        IntPtr hookHandle,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var rootWindow = GetAncestor(windowHandle, GaRoot);
        if (!BackgroundTargets.ContainsKey(rootWindow))
        {
            return;
        }

        var messageWindow = GetKeyboardMessageWindow(rootWindow);
        var targetThreadId = GetWindowThreadProcessId(rootWindow, out _);
        RestoreRealActivationState(
            rootWindow,
            messageWindow,
            IntPtr.Zero,
            targetThreadId);
        ResetTargetInputState(rootWindow, messageWindow);
        Trace(
            $"repair-immediate target=0x{rootWindow.ToInt64():X} "
            + $"focus=0x{messageWindow.ToInt64():X} reset=target-key-state");
        Trace($"foreground-enter target=0x{rootWindow.ToInt64():X}");
        RecoveryUntil[rootWindow] = DateTime.UtcNow.AddMilliseconds(1500);
        Trace($"recovery-window target=0x{rootWindow.ToInt64():X} duration=1500ms");
        RepairForegroundInputState(rootWindow);
    }

    private static async Task WaitForRecoveryWindowAsync(
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        var rootWindow = GetAncestor(windowHandle, GaRoot);
        while (RecoveryUntil.TryGetValue(rootWindow, out var recoveryUntil))
        {
            var remaining = recoveryUntil - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                RecoveryUntil.TryRemove(rootWindow, out _);
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Min(100, remaining.TotalMilliseconds)),
                cancellationToken);
        }
    }

    private static void RepairForegroundInputState(IntPtr windowHandle)
    {
        if (!AdaptiveInputGate.Wait(500))
        {
            Trace($"repair-gate-timeout target=0x{windowHandle.ToInt64():X}");
            return;
        }
        try
        {
            try
            {
                if (!IsWindow(windowHandle))
                {
                    return;
                }

                var targetIsForeground = IsWindowInForeground(windowHandle);
                ForegroundInputSession? activateSession = null;
                try
                {
                    if (targetIsForeground)
                    {
                        activateSession = new ForegroundInputSession(windowHandle);
                        if (!activateSession.Activate(windowHandle))
                        {
                            Trace($"repair-activate-failed target=0x{windowHandle.ToInt64():X}");
                            return;
                        }
                    }

                    var targetThreadId = GetWindowThreadProcessId(windowHandle, out _);
                    var messageWindow = GetKeyboardMessageWindow(windowHandle);
                    if (targetIsForeground)
                    {
                        SetFocus(messageWindow);
                    }
                    RestoreRealActivationState(
                        windowHandle,
                        messageWindow,
                        IntPtr.Zero,
                        targetThreadId);
                    ResetPhysicalInputState(windowHandle, messageWindow);
                    Trace(
                        $"repair-done target=0x{windowHandle.ToInt64():X} "
                        + $"focus=0x{messageWindow.ToInt64():X} "
                        + $"foreground={targetIsForeground} reset=all-key-state");
                }
                finally
                {
                    activateSession?.Dispose();
                }
            }
            catch
            {
                // A later foreground event or the next action will retry the repair.
                Trace($"repair-error target=0x{windowHandle.ToInt64():X}");
            }
        }
        finally
        {
            AdaptiveInputGate.Release();
        }
    }

    private static void ReleaseTrackedInputState(
        IntPtr windowHandle,
        IntPtr messageWindow)
    {
        foreach (var tracked in TrackedKeys.ToArray())
        {
            if (tracked.Key.Target != windowHandle)
            {
                continue;
            }

            if (TrySendKeyMessage(
                messageWindow,
                tracked.Key.Key,
                keyUp: true,
                isAlt: tracked.Key.IsAlt))
            {
                TrackedKeys.TryRemove(tracked.Key, out _);
            }
        }

        foreach (var tracked in TrackedMouseButtons.ToArray())
        {
            if (tracked.Key.Target != windowHandle)
            {
                continue;
            }

            if (TrySendMouseUpMessage(
                tracked.Value.Window,
                tracked.Key.Button,
                tracked.Value.Point))
            {
                TrackedMouseButtons.TryRemove(tracked.Key, out _);
            }
        }
    }

    private static void ResetPhysicalInputState(
        IntPtr windowHandle,
        IntPtr messageWindow)
    {
        ResetTargetInputState(windowHandle, messageWindow);

        var isForeground = IsWindowInForeground(windowHandle);
        if (!isForeground)
        {
            return;
        }

        foreach (var virtualKey in new[]
        {
            VkShift,
            VkControl,
            VkAlt,
            VkLeftWindows
        })
        {
            keybd_event(
                (byte)virtualKey,
                0,
                KeyEventKeyUp,
                UIntPtr.Zero);
        }

        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventRightUp, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventMiddleUp, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventMove, 0, 0, 0, UIntPtr.Zero);

        var keyUps = new List<Input>(0xFE - 0x08 + 1);
        for (var virtualKey = 0x08; virtualKey <= 0xFE; virtualKey++)
        {
            keyUps.Add(CreateKeyboardInput(virtualKey, keyUp: true));
        }
        SendInput(
            (uint)keyUps.Count,
            keyUps.ToArray(),
            Marshal.SizeOf<Input>());
        Thread.Sleep(20);
    }

    private static void ResetTargetInputState(
        IntPtr windowHandle,
        IntPtr messageWindow)
    {
        for (var virtualKey = 0x08; virtualKey <= 0xFE; virtualKey++)
        {
            PostMessage(
                messageWindow,
                WmKeyUp,
                new IntPtr(virtualKey),
                GetKeyMessageParameters(virtualKey, keyUp: true));
            if (virtualKey is 0x12 or 0xA4 or 0xA5)
            {
                PostMessage(
                    messageWindow,
                    WmSysKeyUp,
                    new IntPtr(virtualKey),
                    GetKeyMessageParameters(virtualKey, keyUp: true));
            }
        }
        TrySendWindowMessage(messageWindow, WmLButtonUp, IntPtr.Zero, IntPtr.Zero);
        TrySendWindowMessage(messageWindow, WmRButtonUp, IntPtr.Zero, IntPtr.Zero);
        TrySendWindowMessage(messageWindow, WmMButtonUp, IntPtr.Zero, IntPtr.Zero);
        ReleaseTrackedInputState(windowHandle, messageWindow);
    }

    private static bool TrySendMouseUpMessage(
        IntPtr windowHandle,
        MouseButtonKind button,
        IntPtr packedPoint)
    {
        var upMessage = button switch
        {
            MouseButtonKind.Right => WmRButtonUp,
            MouseButtonKind.Middle => WmMButtonUp,
            _ => WmLButtonUp
        };
        return TrySendWindowMessage(
            windowHandle,
            upMessage,
            IntPtr.Zero,
            packedPoint);
    }

    private static async Task SendMouseClickWithBackgroundActivationAsync(
        MacroAction action,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        if (IsWindowInForeground(windowHandle))
        {
            await SendMouseClickInForegroundAsync(
                action,
                windowHandle,
                cancellationToken);
            return;
        }

        var previousForeground = GetForegroundWindow();
        var targetThreadId = GetWindowThreadProcessId(windowHandle, out _);
        var screenPoint = new Point { X = (int)action.X, Y = (int)action.Y };
        var messageWindow = GetMouseMessageWindow(windowHandle, screenPoint);
        var activationAttempted = false;
        WatchBackgroundTarget(windowHandle);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            activationAttempted = true;
            ActivateWindowWithMessages(
                windowHandle,
                messageWindow,
                previousForeground,
                targetThreadId);
            await SendMouseClickMessagesAsync(
                action,
                messageWindow,
                screenPoint,
                cancellationToken);
        }
        finally
        {
            if (activationAttempted)
            {
                RestoreWindowAfterFakeActivation(
                    windowHandle,
                    messageWindow,
                    previousForeground,
                    targetThreadId);
            }
        }
    }

    private static async Task SendMouseClickMessagesAsync(
        MacroAction action,
        IntPtr messageWindow,
        Point screenPoint,
        CancellationToken cancellationToken)
    {
        var clientPoint = screenPoint;
        if (!ScreenToClient(messageWindow, ref clientPoint))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var (downMessage, upMessage, buttonFlag) = action.MouseButton switch
        {
            MouseButtonKind.Right => (WmRButtonDown, WmRButtonUp, MkRightButton),
            MouseButtonKind.Middle => (WmMButtonDown, WmMButtonUp, MkMiddleButton),
            _ => (WmLButtonDown, WmLButtonUp, MkLeftButton)
        };
        var packedPoint = PackPoint(clientPoint.X, clientPoint.Y);
        var buttonDown = false;
        var targetWindow = GetAncestor(messageWindow, GaRoot);

        try
        {
            if (IsWindowInForeground(targetWindow))
            {
                return;
            }
            SendWindowMessage(
                messageWindow,
                WmMouseMove,
                IntPtr.Zero,
                packedPoint,
                "目标窗口没有响应后台鼠标移动。");
            TrackedMouseButtons[
                (targetWindow, action.MouseButton)] = (messageWindow, packedPoint);
            buttonDown = true;
            if (IsWindowInForeground(targetWindow))
            {
                return;
            }
            SendWindowMessage(
                messageWindow,
                downMessage,
                new IntPtr(buttonFlag),
                packedPoint,
                "目标窗口没有响应后台鼠标按下。");
            await Task.Delay(20, cancellationToken);
            if (IsWindowInForeground(targetWindow))
            {
                return;
            }
            SendWindowMessage(
                messageWindow,
                upMessage,
                IntPtr.Zero,
                packedPoint,
                "目标窗口没有响应后台鼠标抬起。");
            buttonDown = false;
            TrackedMouseButtons.TryRemove(
                (targetWindow, action.MouseButton),
                out _);
        }
        finally
        {
            if (buttonDown)
            {
                if (TrySendWindowMessage(
                    messageWindow,
                    upMessage,
                    IntPtr.Zero,
                    packedPoint))
                {
                    TrackedMouseButtons.TryRemove(
                        (targetWindow, action.MouseButton),
                        out _);
                }
            }
        }
    }

    private static IntPtr GetMouseMessageWindow(IntPtr windowHandle, Point screenPoint)
    {
        var windowAtPoint = WindowFromPoint(screenPoint);
        return windowAtPoint != IntPtr.Zero
            && GetAncestor(windowAtPoint, GaRoot) == GetAncestor(windowHandle, GaRoot)
            ? windowAtPoint
            : windowHandle;
    }

    private static async Task SendMouseClickInForegroundAsync(
        MacroAction action,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        var originalForeground = GetForegroundWindow();
        var targetWasForeground = IsWindowInForeground(windowHandle);
        var hasOriginalCursor = GetCursorPos(out var originalCursor);
        ForegroundInputSession? inputSession = null;
        var buttonDown = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!targetWasForeground)
            {
                inputSession = new ForegroundInputSession(windowHandle);
                if (!inputSession.Activate(windowHandle))
                {
                    await Task.Delay(30, cancellationToken);
                    inputSession.Activate(windowHandle);
                }
            }

            if (!IsWindowInForeground(windowHandle))
            {
                throw new InvalidOperationException(
                    "无法激活目标窗口。若目标以管理员身份运行，请同样以管理员身份运行 KeyTool。");
            }

            if (!SetCursorPos((int)action.X, (int)action.Y))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            buttonDown = true;
            SendMouseButton(action.MouseButton, keyUp: false);
            await Task.Delay(20, cancellationToken);
            SendMouseButton(action.MouseButton, keyUp: true);
            buttonDown = false;
        }
        finally
        {
            if (buttonDown)
            {
                TrySendMouseButton(action.MouseButton, keyUp: true);
            }
            if (inputSession is not null)
            {
                if (originalForeground != IntPtr.Zero
                    && IsWindowInForeground(windowHandle))
                {
                    inputSession.Restore();
                }
                inputSession.Dispose();
            }
            if (hasOriginalCursor)
            {
                SetCursorPos(originalCursor.X, originalCursor.Y);
            }
        }
    }

    private static void SendMouseButton(MouseButtonKind button, bool keyUp)
    {
        var input = CreateMouseButtonInput(button, keyUp);
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1)
        {
            var error = Marshal.GetLastWin32Error();
            throw error == 0
                ? new InvalidOperationException("系统拒绝了鼠标输入，请检查目标程序权限。")
                : new Win32Exception(error);
        }
    }

    private static void TrySendMouseButton(MouseButtonKind button, bool keyUp)
    {
        SendInput(
            1,
            new[] { CreateMouseButtonInput(button, keyUp) },
            Marshal.SizeOf<Input>());
    }

    private static Input CreateMouseButtonInput(MouseButtonKind button, bool keyUp)
    {
        var flags = button switch
        {
            MouseButtonKind.Right => keyUp ? MouseEventRightUp : MouseEventRightDown,
            MouseButtonKind.Middle => keyUp ? MouseEventMiddleUp : MouseEventMiddleDown,
            _ => keyUp ? MouseEventLeftUp : MouseEventLeftDown
        };
        return new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput { Flags = flags }
            }
        };
    }

    private static async Task SendMouseClickMessageAsync(
        MacroAction action,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        var point = new Point { X = (int)action.X, Y = (int)action.Y };
        if (!ScreenToClient(windowHandle, ref point))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var (downMessage, upMessage, buttonFlag) = action.MouseButton switch
        {
            MouseButtonKind.Right => (WmRButtonDown, WmRButtonUp, MkRightButton),
            MouseButtonKind.Middle => (WmMButtonDown, WmMButtonUp, MkMiddleButton),
            _ => (WmLButtonDown, WmLButtonUp, MkLeftButton)
        };

        var packedPoint = PackPoint(point.X, point.Y);
        Post(windowHandle, WmMouseMove, IntPtr.Zero, packedPoint);
        Post(windowHandle, downMessage, new IntPtr(buttonFlag), packedPoint);
        await Task.Delay(20, cancellationToken);
        Post(windowHandle, upMessage, IntPtr.Zero, packedPoint);
    }

    private static void SendMouseMove(MacroAction action, IntPtr windowHandle)
    {
        var point = new Point { X = (int)action.X, Y = (int)action.Y };
        if (!ScreenToClient(windowHandle, ref point))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        Post(windowHandle, WmMouseMove, IntPtr.Zero, PackPoint(point.X, point.Y));
    }

    private static void SendScroll(MacroAction action, IntPtr windowHandle)
    {
        if (!GetCursorPos(out var screenPoint))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var wheelDelta = (long)(action.DeltaY << 16);
        var wParam = new IntPtr(wheelDelta & 0xFFFF_FFFF);
        Post(windowHandle, WmMouseWheel, wParam, PackPoint(screenPoint.X, screenPoint.Y));
    }

    private static void PostKey(
        IntPtr windowHandle,
        int virtualKey,
        bool keyUp,
        bool isAlt)
    {
        var message = isAlt
            ? (keyUp ? WmSysKeyUp : WmSysKeyDown)
            : (keyUp ? WmKeyUp : WmKeyDown);
        var lParam = GetKeyMessageParameters(virtualKey, keyUp);

        Post(windowHandle, message, new IntPtr(virtualKey), lParam);
    }

    private static void SendKeyMessage(
        IntPtr windowHandle,
        int virtualKey,
        bool keyUp,
        bool isAlt)
    {
        var message = isAlt
            ? (keyUp ? WmSysKeyUp : WmSysKeyDown)
            : (keyUp ? WmKeyUp : WmKeyDown);
        SendWindowMessage(
            windowHandle,
            message,
            new IntPtr(virtualKey),
            GetKeyMessageParameters(virtualKey, keyUp),
            "目标窗口没有响应后台按键消息。");
    }

    private static bool TrySendKeyMessage(
        IntPtr windowHandle,
        int virtualKey,
        bool keyUp,
        bool isAlt)
    {
        var message = isAlt
            ? (keyUp ? WmSysKeyUp : WmSysKeyDown)
            : (keyUp ? WmKeyUp : WmKeyDown);
        return TrySendWindowMessage(
            windowHandle,
            message,
            new IntPtr(virtualKey),
            GetKeyMessageParameters(virtualKey, keyUp));
    }

    private static IntPtr GetKeyMessageParameters(int virtualKey, bool keyUp)
    {
        var scanCode = MapVirtualKey((uint)virtualKey, 0);
        long lParam = 1 | ((long)scanCode << 16);
        if (IsExtendedKey(virtualKey))
        {
            lParam |= 1L << 24;
        }
        if (keyUp)
        {
            lParam |= 1L << 30;
            lParam |= 1L << 31;
        }
        return new IntPtr(lParam);
    }

    private static void SendWindowMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        string failureMessage)
    {
        if (SendMessageTimeout(
            windowHandle,
            message,
            wParam,
            lParam,
            SmtoBlock | SmtoAbortIfHung,
            WindowMessageTimeoutMilliseconds,
            out _) != IntPtr.Zero)
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        throw error == 0
            ? new InvalidOperationException(failureMessage)
            : new Win32Exception(error, failureMessage);
    }

    private static bool TrySendWindowMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        return SendMessageTimeout(
            windowHandle,
            message,
            wParam,
            lParam,
            SmtoBlock | SmtoAbortIfHung,
            WindowMessageTimeoutMilliseconds,
            out _) != IntPtr.Zero;
    }

    private static bool IsExtendedKey(int virtualKey)
    {
        return virtualKey is 0x21 or 0x22 or 0x23 or 0x24
            or 0x25 or 0x26 or 0x27 or 0x28
            or 0x2D or 0x2E;
    }

    private static IntPtr PackPoint(int x, int y)
    {
        var packed = (y << 16) | (x & 0xFFFF);
        return new IntPtr(packed);
    }

    private static void Post(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (!PostMessage(windowHandle, message, wParam, lParam))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
