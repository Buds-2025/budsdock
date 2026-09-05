using System.Runtime.InteropServices;
using System.Text;
using BudsDock.Interop;

namespace BudsDock.Services;

public sealed class NativeWindowService
{
    public bool ApplyClickThrough(IntPtr handle, bool enabled, bool showInTaskbar = false)
    {
        if (handle == IntPtr.Zero)
        {
            return true;
        }

        if (!NativeMethods.TryGetWindowLongPtr(handle, NativeMethods.GwlExStyle, out var style))
        {
            return false;
        }

        var current = style.ToInt64();
        if (showInTaskbar) current &= ~NativeMethods.WsExToolWindow;
        var required = NativeMethods.WsExLayered | (showInTaskbar ? 0 : NativeMethods.WsExToolWindow);
        var updated = enabled
            ? current | required | NativeMethods.WsExTransparent
            : (current | required) & ~NativeMethods.WsExTransparent;

        if (updated == style.ToInt64())
        {
            return true;
        }

        if (!NativeMethods.TrySetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(updated)))
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged);
    }

    public bool RegisterRecoveryHotkey(IntPtr handle, int id)
        => NativeMethods.RegisterHotKey(
            handle,
            id,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            NativeMethods.VkD);

    public void UnregisterRecoveryHotkey(IntPtr handle, int id)
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(handle, id);
        }
    }

    public bool IsForegroundFullscreen(IntPtr dockHandle = default)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero
            || NativeMethods.IsOwnProcessWindow(foreground)
            || !NativeMethods.IsWindowVisible(foreground)
            || NativeMethods.IsIconic(foreground))
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowAttributeInt(
                foreground,
                NativeMethods.DwmwaCloaked,
                out var cloaked,
                Marshal.SizeOf<int>()) == 0
            && cloaked != 0)
        {
            return false;
        }

        var className = new StringBuilder(128);
        NativeMethods.GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        var frameResult = NativeMethods.DwmGetWindowAttributeRect(
            foreground,
            NativeMethods.DwmwaExtendedFrameBounds,
            out var windowRect,
            Marshal.SizeOf<NativeMethods.NativeRect>());
        if (frameResult != 0 && !NativeMethods.GetWindowRect(foreground, out windowRect))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MonitorDefaultToNearest);
        if (dockHandle != IntPtr.Zero && NativeMethods.MonitorFromWindow(dockHandle, NativeMethods.MonitorDefaultToNearest) != monitor)
            return false;
        var monitorInfo = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 8;
        return Math.Abs(windowRect.Left - monitorInfo.Monitor.Left) <= tolerance
            && Math.Abs(windowRect.Top - monitorInfo.Monitor.Top) <= tolerance
            && Math.Abs(windowRect.Right - monitorInfo.Monitor.Right) <= tolerance
            && Math.Abs(windowRect.Bottom - monitorInfo.Monitor.Bottom) <= tolerance;
    }
}
