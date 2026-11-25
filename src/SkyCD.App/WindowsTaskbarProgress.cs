using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SkyCD.App;

/// <summary>
/// Taskbar progress states mirrored from the Win32 <c>TBPFLAG</c> values.
/// </summary>
internal enum TaskbarProgressState
{
    NoProgress = 0,
    Indeterminate = 0x1,
    Normal = 0x2
}

/// <summary>
/// Mirrors an operation's progress onto the owning taskbar button through <c>ITaskbarList3</c>.
/// Every call is best-effort: unsupported platforms, missing window handles and COM failures stay
/// silent, so progress reporting can never fail the operation it tracks.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsTaskbarProgress
{
    private const ulong ProgressTotal = 100;

    private static readonly object Gate = new();
    private static ITaskbarList3? taskbar;

    public static void SetIndeterminate(IntPtr windowHandle) =>
        SetState(windowHandle, TaskbarProgressState.Indeterminate, 0);

    public static void SetProgress(IntPtr windowHandle, double percent) =>
        SetState(windowHandle, TaskbarProgressState.Normal, (ulong)Math.Clamp(percent, 0, ProgressTotal));

    public static void Clear(IntPtr windowHandle) =>
        SetState(windowHandle, TaskbarProgressState.NoProgress, 0);

    private static void SetState(IntPtr windowHandle, TaskbarProgressState state, ulong completed)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var list = GetTaskbarList();
            if (list is null)
            {
                return;
            }

            list.SetProgressState(windowHandle, state);
            if (state == TaskbarProgressState.Normal)
            {
                list.SetProgressValue(windowHandle, completed, ProgressTotal);
            }
        }
        catch (Exception exception) when (exception is COMException
            or InvalidCastException
            or InvalidOperationException
            or UnauthorizedAccessException)
        {
            // The shell may be unavailable (Server Core, kiosk shells); progress is decorative.
        }
    }

    private static ITaskbarList3? GetTaskbarList()
    {
        lock (Gate)
        {
            if (taskbar is not null)
            {
                return taskbar;
            }

            try
            {
                var list = (ITaskbarList3)new TaskbarList();
                list.HrInit();
                taskbar = list;
                return taskbar;
            }
            catch (Exception exception) when (exception is COMException
                or InvalidCastException
                or InvalidOperationException
                or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList
    {
    }

    // Vtable order must cover ITaskbarList and ITaskbarList2 before the ITaskbarList3 members.
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, TaskbarProgressState flags);
    }
}
