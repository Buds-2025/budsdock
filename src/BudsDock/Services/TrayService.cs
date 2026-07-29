using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using BudsDock.Models;
using Application = System.Windows.Application;

namespace BudsDock.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private ContextMenuStrip? _menu;
    private Font? _menuFont;
    private Font? _restoreFont;
    private bool _isInteractionEnabled = true;

    public TrayService(LocalizationService localization, AppSettings settings)
    {
        _localization = localization;
        _settings = settings;
        _trayIcon = LoadIcon();
        _notifyIcon = new NotifyIcon
        {
            Text = "BudsDock",
            Visible = true,
            Icon = _trayIcon
        };
        _notifyIcon.DoubleClick += (_, _) =>
        {
            if (_isInteractionEnabled)
            {
                OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        RebuildMenu();
    }

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? RestoreInteractionRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<DockPlacement>? PlacementRequested;

    public void RebuildMenu()
    {
        _notifyIcon.Text = _localization.Translate("App.Name");
        var menuFont = new Font("Segoe UI", 9.25f);
        var restoreFont = new Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold);
        var menu = new ContextMenuStrip
        {
            BackColor = DarkMenuColorTable.Background,
            ForeColor = DarkMenuColorTable.Text,
            Font = menuFont,
            Padding = new Padding(6),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Renderer = DarkMenuRenderer.Instance
        };
        var restoreItem = new ToolStripMenuItem(_localization.Translate("Action.RestoreInteraction"), null, (_, _) => Dispatch(() => RestoreInteractionRequested?.Invoke(this, EventArgs.Empty)))
        {
            Font = restoreFont,
            Enabled = _settings.IsClickThrough
        };
        menu.Items.Add(restoreItem);
        menu.Items.Add(_localization.Translate("Action.Settings"), null, (_, _) => Dispatch(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripSeparator());

        var placement = new ToolStripMenuItem(_localization.Translate("Nav.Position"));
        placement.DropDownItems.Add(_localization.Translate("Position.Top"), null, (_, _) => RequestPlacement(DockPlacement.TopCenter));
        placement.DropDownItems.Add(_localization.Translate("Position.Bottom"), null, (_, _) => RequestPlacement(DockPlacement.BottomCenter));
        placement.DropDownItems.Add(_localization.Translate("Position.Left"), null, (_, _) => RequestPlacement(DockPlacement.LeftCenter));
        placement.DropDownItems.Add(_localization.Translate("Position.Right"), null, (_, _) => RequestPlacement(DockPlacement.RightCenter));
        placement.DropDownItems.Add(_localization.Translate("Position.Center"), null, (_, _) => RequestPlacement(DockPlacement.ScreenCenter));
        menu.Items.Add(placement);

        var lockItem = new ToolStripMenuItem(_localization.Translate("Behavior.Lock")) { Checked = _settings.IsPositionLocked, CheckOnClick = true };
        lockItem.Click += (_, _) => Dispatch(() => _settings.IsPositionLocked = lockItem.Checked);
        menu.Items.Add(lockItem);

        var clickThrough = new ToolStripMenuItem(_localization.Translate("Behavior.ClickThrough")) { Checked = _settings.IsClickThrough, CheckOnClick = true };
        clickThrough.Click += (_, _) => Dispatch(() => _settings.IsClickThrough = clickThrough.Checked);
        menu.Items.Add(clickThrough);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_localization.Translate("Action.Exit"), null, (_, _) => Dispatch(() => ExitRequested?.Invoke(this, EventArgs.Empty)));
        ConfigureDropDown(menu);

        var previousMenu = _menu;
        var previousMenuFont = _menuFont;
        var previousRestoreFont = _restoreFont;
        _menu = menu;
        _menuFont = menuFont;
        _restoreFont = restoreFont;
        _notifyIcon.ContextMenuStrip = menu;
        previousMenu?.Dispose();
        previousMenuFont?.Dispose();
        previousRestoreFont?.Dispose();
    }

    public void ShowBalloon(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3500);
    }

    public void SetInteractionEnabled(bool enabled)
    {
        _isInteractionEnabled = enabled;
        if (_notifyIcon.ContextMenuStrip is not null)
        {
            _notifyIcon.ContextMenuStrip.Enabled = enabled;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _notifyIcon.Icon = null;
        _notifyIcon.Dispose();
        _menu?.Dispose();
        _menuFont?.Dispose();
        _restoreFont?.Dispose();
        _trayIcon.Dispose();
    }

    private void RequestPlacement(DockPlacement placement)
    {
        if (_isInteractionEnabled)
        {
            Dispatch(() => PlacementRequested?.Invoke(this, placement));
        }
    }

    private static void Dispatch(Action action)
        => Application.Current.Dispatcher.Invoke(action);

    private static Icon LoadIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                using var associated = Icon.ExtractAssociatedIcon(executablePath);
                if (associated is not null)
                {
                    return (Icon)associated.Clone();
                }
            }
        }
        catch
        {
            // Fall back to the embedded application icon below.
        }

        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/BudsDock.ico"));
            if (resource?.Stream is not null)
            {
                using (resource.Stream)
                using (var embedded = new Icon(resource.Stream))
                {
                    return (Icon)embedded.Clone();
                }
            }
        }
        catch
        {
            // The Windows application icon is the final fallback.
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static void ConfigureDropDown(ToolStripDropDown dropDown)
    {
        dropDown.BackColor = DarkMenuColorTable.Background;
        dropDown.ForeColor = DarkMenuColorTable.Text;
        dropDown.Renderer = DarkMenuRenderer.Instance;
        if (dropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
        }
        dropDown.Opened += (_, _) => ApplyRoundedRegion(dropDown);
        dropDown.SizeChanged += (_, _) => ApplyRoundedRegion(dropDown);
        foreach (ToolStripItem item in dropDown.Items)
        {
            item.BackColor = DarkMenuColorTable.Background;
            item.ForeColor = item.Enabled ? DarkMenuColorTable.Text : DarkMenuColorTable.DisabledText;
            if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
            {
                ConfigureDropDown(menuItem.DropDown);
            }
        }
    }

    private static void ApplyRoundedRegion(ToolStripDropDown dropDown)
    {
        if (dropDown.Width <= 2 || dropDown.Height <= 2)
        {
            return;
        }

        using var path = DarkMenuRenderer.CreateRoundedRectangle(
            new Rectangle(0, 0, dropDown.Width, dropDown.Height),
            ScaleRadius(dropDown.DeviceDpi));
        var previous = dropDown.Region;
        dropDown.Region = new Region(path);
        previous?.Dispose();
    }

    private static int ScaleRadius(int deviceDpi)
        => Math.Max(8, (int)Math.Round(10 * Math.Max(96, deviceDpi) / 96d));

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public static DarkMenuRenderer Instance { get; } = new();

        private DarkMenuRenderer() : base(new DarkMenuColorTable())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? DarkMenuColorTable.Text : DarkMenuColorTable.DisabledText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled != false ? DarkMenuColorTable.Text : DarkMenuColorTable.DisabledText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(DarkMenuColorTable.Border);
            using var path = CreateRoundedRectangle(
                new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1),
                ScaleRadius(e.ToolStrip.DeviceDpi));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }

        public static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public static Color Background { get; } = Color.FromArgb(13, 15, 20);
        public static Color SurfaceHover { get; } = Color.FromArgb(36, 42, 52);
        public static Color Border { get; } = Color.FromArgb(52, 58, 69);
        public static Color Text { get; } = Color.FromArgb(247, 248, 251);
        public static Color DisabledText { get; } = Color.FromArgb(116, 125, 140);

        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => SurfaceHover;
        public override Color MenuItemSelected => SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => SurfaceHover;
        public override Color MenuItemPressedGradientBegin => SurfaceHover;
        public override Color MenuItemPressedGradientMiddle => SurfaceHover;
        public override Color MenuItemPressedGradientEnd => SurfaceHover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => SurfaceHover;
        public override Color CheckSelectedBackground => SurfaceHover;
        public override Color CheckPressedBackground => SurfaceHover;
    }
}
