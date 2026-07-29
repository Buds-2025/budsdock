using System.Collections.ObjectModel;
using BudsDock.ViewModels;

namespace BudsDock.Models;

public sealed class AppSettings : ObservableObject
{
    private int _schemaVersion = 2;
    private ThemeMode _themeMode = ThemeMode.System;
    private AppLanguage _language = AppLanguage.System;
    private DockOrientation _orientation = DockOrientation.Horizontal;
    private DockPlacement _placement = DockPlacement.BottomCenter;
    private IconVisualMode _defaultIconVisualMode = IconVisualMode.Original;
    private double _backgroundOpacity = 0.88;
    private double _iconSize = 54;
    private double _iconSpacing = 12;
    private double _dockScale = 1.0;
    private double _panelPadding = 12;
    private double _cornerRadius = 18;
    private double _reflectionOpacity = 0.24;
    private double _glowIntensity = 0.34;
    private double _hoverScale = 1.50;
    private double _adjacentHoverScale = 1.16;
    private bool _showReflection = true;
    private bool _enableHoverAnimation = true;
    private bool _isPositionLocked;
    private bool _isClickThrough;
    private bool _topmost = true;
    private bool _hideOnFullscreen = true;
    private bool _autoStart = true;
    private double? _left;
    private double? _top;

    public int SchemaVersion { get => _schemaVersion; set => SetProperty(ref _schemaVersion, value); }
    public ThemeMode ThemeMode { get => _themeMode; set => SetProperty(ref _themeMode, value); }
    public AppLanguage Language { get => _language; set => SetProperty(ref _language, value); }
    public DockOrientation Orientation { get => _orientation; set => SetProperty(ref _orientation, value); }
    public DockPlacement Placement { get => _placement; set => SetProperty(ref _placement, value); }
    public IconVisualMode DefaultIconVisualMode { get => _defaultIconVisualMode; set => SetProperty(ref _defaultIconVisualMode, value); }
    public double BackgroundOpacity { get => _backgroundOpacity; set => SetProperty(ref _backgroundOpacity, Math.Clamp(value, 0, 1)); }
    public double IconSize { get => _iconSize; set => SetProperty(ref _iconSize, Math.Clamp(value, 28, 112)); }
    public double IconSpacing { get => _iconSpacing; set => SetProperty(ref _iconSpacing, Math.Clamp(value, 0, 48)); }
    public double DockScale { get => _dockScale; set => SetProperty(ref _dockScale, Math.Clamp(value, 0.65, 1.8)); }
    public double PanelPadding { get => _panelPadding; set => SetProperty(ref _panelPadding, Math.Clamp(value, 4, 36)); }
    public double CornerRadius { get => _cornerRadius; set => SetProperty(ref _cornerRadius, Math.Clamp(value, 0, 36)); }
    public double ReflectionOpacity { get => _reflectionOpacity; set => SetProperty(ref _reflectionOpacity, Math.Clamp(value, 0, 0.7)); }
    public double GlowIntensity { get => _glowIntensity; set => SetProperty(ref _glowIntensity, Math.Clamp(value, 0, 1)); }
    public double HoverScale { get => _hoverScale; set => SetProperty(ref _hoverScale, Math.Clamp(value, 1, 1.65)); }
    public double AdjacentHoverScale { get => _adjacentHoverScale; set => SetProperty(ref _adjacentHoverScale, Math.Clamp(value, 1, 1.3)); }
    public bool ShowReflection { get => _showReflection; set => SetProperty(ref _showReflection, value); }
    public bool EnableHoverAnimation { get => _enableHoverAnimation; set => SetProperty(ref _enableHoverAnimation, value); }
    public bool IsPositionLocked { get => _isPositionLocked; set => SetProperty(ref _isPositionLocked, value); }
    public bool IsClickThrough { get => _isClickThrough; set => SetProperty(ref _isClickThrough, value); }
    public bool Topmost { get => _topmost; set => SetProperty(ref _topmost, value); }
    public bool HideOnFullscreen { get => _hideOnFullscreen; set => SetProperty(ref _hideOnFullscreen, value); }
    public bool AutoStart { get => _autoStart; set => SetProperty(ref _autoStart, value); }
    public double? Left { get => _left; set => SetProperty(ref _left, value); }
    public double? Top { get => _top; set => SetProperty(ref _top, value); }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Hotkey => "Ctrl+Alt+D";

    public ObservableCollection<DockItem> Items { get; set; } = [];
}
