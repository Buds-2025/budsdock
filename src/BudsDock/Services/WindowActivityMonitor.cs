using BudsDock.Interop;

namespace BudsDock.Services;

// Created and disposed on the window's dispatcher thread. Out-of-context hooks
// deliver notifications on that thread without injecting code into other apps.
internal sealed class WindowActivityMonitor : IDisposable
{
    private readonly NativeMethods.WinEventProc _callback;
    private readonly List<IntPtr> _hooks = [];
    private bool _disposed;

    internal WindowActivityMonitor(Action changed)
    {
        _callback = (_, _, _, _, _, _, _) =>
        {
            if (!_disposed) changed();
        };
        Add(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground);
        Add(NativeMethods.EventSystemMoveSizeEnd, NativeMethods.EventSystemMoveSizeEnd);
        Add(NativeMethods.EventSystemMinimizeStart, NativeMethods.EventSystemMinimizeEnd);
        Add(NativeMethods.EventSystemDesktopSwitch, NativeMethods.EventSystemDesktopSwitch);
    }

    private void Add(uint first, uint last)
    {
        var hook = NativeMethods.SetWinEventHook(first, last, IntPtr.Zero, _callback, 0, 0, 0);
        if (hook != IntPtr.Zero) _hooks.Add(hook);
        // The periodic visibility check remains active if a hook is unavailable.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var hook in _hooks) NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
        GC.KeepAlive(_callback);
    }
}
