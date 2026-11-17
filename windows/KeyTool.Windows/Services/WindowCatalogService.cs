using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using KeyTool.Windows.Models;

namespace KeyTool.Windows.Services;

public static class WindowCatalogService
{
    private const uint GwOwner = 4;

    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr windowHandle,
        StringBuilder text,
        int maxCount
    );

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId
    );

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr windowHandle);

    public static IReadOnlyList<TargetWindow> GetOpenWindows()
    {
        var windows = new List<TargetWindow>();
        var ownProcessId = (uint)Environment.ProcessId;

        EnumWindows((windowHandle, _) =>
        {
            try
            {
                if (!IsWindowVisible(windowHandle) || GetWindow(windowHandle, GwOwner) != IntPtr.Zero)
                {
                    return true;
                }

                var titleBuffer = new StringBuilder(512);
                if (GetWindowText(windowHandle, titleBuffer, titleBuffer.Capacity) <= 0)
                {
                    return true;
                }

                GetWindowThreadProcessId(windowHandle, out var processId);
                if (processId == ownProcessId)
                {
                    return true;
                }

                var processName = "未知程序";
                try
                {
                    processName = Process.GetProcessById((int)processId).ProcessName;
                }
                catch
                {
                    // The process can exit while windows are being enumerated.
                }

                windows.Add(new TargetWindow
                {
                    WindowHandle = windowHandle.ToInt64(),
                    Title = titleBuffer.ToString(),
                    ProcessName = processName
                });
            }
            catch
            {
                // Ignore inaccessible windows and continue enumeration.
            }

            return true;
        }, IntPtr.Zero);

        windows.Sort((left, right) => string.Compare(
            left.DisplayName,
            right.DisplayName,
            StringComparison.CurrentCultureIgnoreCase
        ));
        return windows;
    }

    public static TargetWindow? CaptureForegroundWindow()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var matchingWindow = GetOpenWindows().FirstOrDefault(window =>
            window.WindowHandle == handle.ToInt64());
        return matchingWindow;
    }

    public static TargetWindow? ResolveTarget(TargetWindow target)
    {
        var windows = GetOpenWindows();
        var handleMatch = windows.FirstOrDefault(window =>
            window.WindowHandle == target.WindowHandle);
        if (handleMatch is not null && MatchesTarget(handleMatch, target))
        {
            return handleMatch;
        }

        if (!string.IsNullOrWhiteSpace(target.ProcessName))
        {
            var processMatches = windows.Where(window =>
                string.Equals(
                    window.ProcessName,
                    target.ProcessName,
                    StringComparison.CurrentCultureIgnoreCase)).ToList();
            var titleMatch = processMatches.FirstOrDefault(window =>
                string.Equals(
                    window.Title,
                    target.Title,
                    StringComparison.CurrentCulture));
            if (titleMatch is not null)
            {
                return titleMatch;
            }
            if (processMatches.Count == 1)
            {
                return processMatches[0];
            }
        }

        if (!string.IsNullOrWhiteSpace(target.Title))
        {
            var titleMatches = windows.Where(window =>
                string.Equals(
                    window.Title,
                    target.Title,
                    StringComparison.CurrentCulture)).ToList();
            if (titleMatches.Count == 1)
            {
                return titleMatches[0];
            }
        }

        return null;
    }

    private static bool MatchesTarget(TargetWindow window, TargetWindow target)
    {
        return (string.IsNullOrWhiteSpace(target.ProcessName)
                || string.Equals(
                    window.ProcessName,
                    target.ProcessName,
                    StringComparison.CurrentCultureIgnoreCase))
            && (string.IsNullOrWhiteSpace(target.Title)
                || string.Equals(
                    window.Title,
                    target.Title,
                    StringComparison.CurrentCulture));
    }
}
