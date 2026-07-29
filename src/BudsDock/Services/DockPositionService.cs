using System.Windows;
using BudsDock.Models;

namespace BudsDock.Services;

public static class DockPositionService
{
    public const double EdgeMargin = 24;

    public static DockOrientation OrientationFor(DockPlacement placement, DockOrientation current)
        => placement switch
        {
            DockPlacement.TopCenter or DockPlacement.BottomCenter => DockOrientation.Horizontal,
            DockPlacement.LeftCenter or DockPlacement.RightCenter => DockOrientation.Vertical,
            _ => current
        };

    public static Point Calculate(
        DockPlacement placement,
        Size dockSize,
        Rect workArea,
        double margin = EdgeMargin,
        double bottomTaskbarHeight = 0)
    {
        var centeredLeft = workArea.Left + (workArea.Width - dockSize.Width) / 2;
        var centeredTop = workArea.Top + (workArea.Height - dockSize.Height) / 2;
        var bottomMargin = bottomTaskbarHeight > 0
            ? bottomTaskbarHeight * 1.5
            : margin;

        return placement switch
        {
            DockPlacement.TopCenter => Clamp(new Point(centeredLeft, workArea.Top + margin), dockSize, workArea),
            DockPlacement.BottomCenter => Clamp(new Point(centeredLeft, workArea.Bottom - dockSize.Height - bottomMargin), dockSize, workArea),
            DockPlacement.LeftCenter => Clamp(new Point(workArea.Left + margin, centeredTop), dockSize, workArea),
            DockPlacement.RightCenter => Clamp(new Point(workArea.Right - dockSize.Width - margin, centeredTop), dockSize, workArea),
            DockPlacement.ScreenCenter => Clamp(new Point(centeredLeft, centeredTop), dockSize, workArea),
            _ => Clamp(new Point(centeredLeft, workArea.Bottom - dockSize.Height - margin), dockSize, workArea)
        };
    }

    public static Point Clamp(Point requested, Size dockSize, Rect workArea)
    {
        var maxLeft = Math.Max(workArea.Left, workArea.Right - dockSize.Width);
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - dockSize.Height);
        return new Point(
            Math.Clamp(requested.X, workArea.Left, maxLeft),
            Math.Clamp(requested.Y, workArea.Top, maxTop));
    }
}
