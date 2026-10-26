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

    public bool NeedsVisibilityRecovery(IntPtr handle)
        => handle != IntPtr.Zero && (!NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle));

    public void RestoreWithoutActivation(IntPtr handle)
    {
        if (NeedsVisibilityRecovery(handle))
            NativeMethods.ShowWindow(handle, NativeMethods.SwShowNoActivate);
    }

    public void EnsureTopmost(IntPtr handle, bool topmost, bool raise = false)
    {
        if (handle == IntPtr.Zero) return;
        var matches = NativeMethods.TryGetWindowLongPtr(handle, NativeMethods.GwlExStyle, out var style)
            && ((style.ToInt64() & NativeMethods.WsExTopmost) != 0) == topmost;
        if (matches && !(raise && topmost)) return;
        NativeMethods.SetWindowPos(handle, topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
            0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    // A null foreground is transient during shell switches. Keep the previous
    // visibility until Windows supplies a foreground window on the next check.
    public bool? IsForegroundFullscreen(string? monitorId)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;
        if (NativeMethods.IsOwnProcessWindow(foreground)
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
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
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
        var monitorInfo = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        // A minimized HWND can be parked off-screen. Resolve the configured
        // display instead of using its temporary native window coordinates.
        var target = DisplayService.Resolve(monitorId).Bounds;
        if (monitorInfo.Monitor.Left != target.Left || monitorInfo.Monitor.Top != target.Top
            || monitorInfo.Monitor.Right != target.Right || monitorInfo.Monitor.Bottom != target.Bottom)
            return false;

        const int tolerance = 8;
        return Math.Abs(windowRect.Left - monitorInfo.Monitor.Left) <= tolerance
            && Math.Abs(windowRect.Top - monitorInfo.Monitor.Top) <= tolerance
            && Math.Abs(windowRect.Right - monitorInfo.Monitor.Right) <= tolerance
            && Math.Abs(windowRect.Bottom - monitorInfo.Monitor.Bottom) <= tolerance;
    }
}
