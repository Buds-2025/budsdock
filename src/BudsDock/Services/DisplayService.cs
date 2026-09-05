using System.Windows;
using BudsDock.Interop;
using Screen = System.Windows.Forms.Screen;

namespace BudsDock.Services;

public sealed record DisplayOption(string Id, string Label);

public static class DisplayService
{
    public static IReadOnlyList<DisplayOption> GetDisplays()
        => Screen.AllScreens.Select((screen, index) => new DisplayOption(screen.DeviceName,
            $"{index + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " ★" : ""))).ToArray();

    public static Screen Resolve(string? id)
        => Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == id)
            ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];

    public static double GetScale(IntPtr handle)
        => Math.Max(96u, NativeMethods.GetDpiForWindow(handle)) / 96d;

    // Work in monitor-local DIPs; native placement converts the origin to physical pixels.
    // Scaling global screen coordinates with one monitor's DPI breaks mixed-DPI layouts.
    public static Rect LocalWorkArea(Screen screen, double scale)
        => new(0, 0, screen.WorkingArea.Width / scale, screen.WorkingArea.Height / scale);

    public static double FitScale(Size content, Size available)
        => content.Width <= 0 || content.Height <= 0 ? 1
            : Math.Min(1, Math.Min(available.Width / content.Width, available.Height / content.Height));
}
