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
    private readonly ThemeService _themeService;
    private readonly AppSettings _settings;
    private ContextMenuStrip? _menu;
    private Font? _menuFont;
    private Font? _restoreFont;
    private bool _isInteractionEnabled = true;

    public TrayService(LocalizationService localization, ThemeService themeService, AppSettings settings)
    {
        _localization = localization;
        _themeService = themeService;
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
        var palette = MenuPalette.ForTheme(_themeService.IsDark);
        _notifyIcon.Text = _localization.Translate("App.Name");
        var menuFont = new Font("Segoe UI", 9.25f);
        var restoreFont = new Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold);
        var menu = new ContextMenuStrip
        {
            BackColor = palette.Background,
            ForeColor = palette.Text,
            Font = menuFont,
            Padding = new Padding(6),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Renderer = new ThemedMenuRenderer(palette)
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
        ConfigureDropDown(menu, palette);

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

    private static void ConfigureDropDown(ToolStripDropDown dropDown, MenuPalette palette)
    {
        dropDown.BackColor = palette.Background;
        dropDown.ForeColor = palette.Text;
        dropDown.Renderer = new ThemedMenuRenderer(palette);
        if (dropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
        }
        dropDown.Opened += (_, _) => ApplyRoundedRegion(dropDown);
        dropDown.SizeChanged += (_, _) => ApplyRoundedRegion(dropDown);
        foreach (ToolStripItem item in dropDown.Items)
        {
            item.BackColor = palette.Background;
            item.ForeColor = item.Enabled ? palette.Text : palette.DisabledText;
            if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
            {
                ConfigureDropDown(menuItem.DropDown, palette);
            }
        }
    }

    private static void ApplyRoundedRegion(ToolStripDropDown dropDown)
    {
        if (dropDown.Width <= 2 || dropDown.Height <= 2)
        {
            return;
        }

        using var path = ThemedMenuRenderer.CreateRoundedRectangle(
            new Rectangle(0, 0, dropDown.Width, dropDown.Height),
            ScaleRadius(dropDown.DeviceDpi));
        var previous = dropDown.Region;
        dropDown.Region = new Region(path);
        previous?.Dispose();
    }

    private static int ScaleRadius(int deviceDpi)
        => Math.Max(8, (int)Math.Round(10 * Math.Max(96, deviceDpi) / 96d));

    private sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly MenuPalette _palette;

        public ThemedMenuRenderer(MenuPalette palette) : base(new ThemedMenuColorTable(palette))
        {
            _palette = palette;
            RoundedEdges = true;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _palette.Text : _palette.DisabledText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled != false ? _palette.Text : _palette.DisabledText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(_palette.Border);
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

    private sealed class ThemedMenuColorTable : ProfessionalColorTable
    {
        private readonly MenuPalette _palette;

        public ThemedMenuColorTable(MenuPalette palette) => _palette = palette;

        public override Color ToolStripDropDownBackground => _palette.Background;
        public override Color ImageMarginGradientBegin => _palette.Background;
        public override Color ImageMarginGradientMiddle => _palette.Background;
        public override Color ImageMarginGradientEnd => _palette.Background;
        public override Color MenuBorder => _palette.Border;
        public override Color MenuItemBorder => _palette.Hover;
        public override Color MenuItemSelected => _palette.Hover;
        public override Color MenuItemSelectedGradientBegin => _palette.Hover;
        public override Color MenuItemSelectedGradientEnd => _palette.Hover;
        public override Color MenuItemPressedGradientBegin => _palette.Hover;
        public override Color MenuItemPressedGradientMiddle => _palette.Hover;
        public override Color MenuItemPressedGradientEnd => _palette.Hover;
        public override Color SeparatorDark => _palette.Border;
        public override Color SeparatorLight => _palette.Border;
        public override Color CheckBackground => _palette.Hover;
        public override Color CheckSelectedBackground => _palette.Hover;
        public override Color CheckPressedBackground => _palette.Hover;
    }

    private sealed record MenuPalette(Color Background, Color Hover, Color Border, Color Text, Color DisabledText)
    {
        public static MenuPalette ForTheme(bool isDark)
            => isDark
                ? new MenuPalette(
                    Color.FromArgb(17, 21, 27),
                    Color.FromArgb(40, 50, 65),
                    Color.FromArgb(58, 67, 81),
                    Color.FromArgb(245, 247, 251),
                    Color.FromArgb(134, 147, 168))
                : new MenuPalette(
                    Color.FromArgb(255, 255, 255),
                    Color.FromArgb(228, 235, 245),
                    Color.FromArgb(216, 224, 235),
                    Color.FromArgb(23, 32, 51),
                    Color.FromArgb(113, 128, 150));
    }
}
