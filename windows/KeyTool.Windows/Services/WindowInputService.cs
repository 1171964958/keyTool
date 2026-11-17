using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KeyTool.Windows.Models;

namespace KeyTool.Windows.Services;

public static class WindowInputService
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WmMouseWheel = 0x020A;

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int MkLeftButton = 0x0001;
    private const int MkRightButton = 0x0002;
    private const int MkMiddleButton = 0x0010;

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
    private static extern bool IsWindow(IntPtr windowHandle);

    public static async Task ExecuteAsync(
        MacroAction action,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsWindow(windowHandle))
        {
            throw new InvalidOperationException("目标窗口已关闭，请重新选择目标窗口。");
        }

        switch (action.Kind)
        {
            case ActionKind.Keyboard:
                await SendKeyboardAsync(action, windowHandle, cancellationToken);
                break;
            case ActionKind.MouseClick:
                await SendMouseClickAsync(action, windowHandle, cancellationToken);
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

    public static (double X, double Y) GetCursorPosition()
    {
        if (!GetCursorPos(out var point))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return (point.X, point.Y);
    }

    private static async Task SendKeyboardAsync(
        MacroAction action,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        var shortcut = action.Shortcut
            ?? throw new InvalidOperationException("按键动作尚未设置快捷键。");

        var modifierKeys = new System.Collections.Generic.List<int>();
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Control)) modifierKeys.Add(VkControl);
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Shift)) modifierKeys.Add(VkShift);
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Alt)) modifierKeys.Add(VkAlt);
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Windows)) modifierKeys.Add(VkLeftWindows);

        foreach (var modifierKey in modifierKeys)
        {
            PostKey(windowHandle, modifierKey, keyUp: false, isAlt: false);
        }

        var isAlt = shortcut.Modifiers.HasFlag(ModifierKeys.Alt);
        PostKey(windowHandle, shortcut.VirtualKey, keyUp: false, isAlt: isAlt);
        await Task.Delay(12, cancellationToken);
        PostKey(windowHandle, shortcut.VirtualKey, keyUp: true, isAlt: isAlt);

        for (var index = modifierKeys.Count - 1; index >= 0; index--)
        {
            PostKey(windowHandle, modifierKeys[index], keyUp: true, isAlt: false);
        }
    }

    private static async Task SendMouseClickAsync(
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

        Post(windowHandle, message, new IntPtr(virtualKey), new IntPtr(lParam));
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
