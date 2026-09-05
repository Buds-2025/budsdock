using System.Collections.ObjectModel;
using System.Windows.Input;
using BudsDock.Models;
using BudsDock.Services;

namespace BudsDock.ViewModels;

public sealed class DockViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly LauncherService _launcherService;

    public DockViewModel(SettingsService settingsService, LauncherService launcherService)
    {
        _settingsService = settingsService;
        _launcherService = launcherService;
        LaunchCommand = new RelayCommand(parameter =>
        {
            if (parameter is DockItem item)
            {
                _launcherService.Launch(item);
            }
        });
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        ToggleLockCommand = new RelayCommand(() => Settings.IsPositionLocked = !Settings.IsPositionLocked);
        ToggleClickThroughCommand = new RelayCommand(() => Settings.IsClickThrough = !Settings.IsClickThrough);
        PlacementCommand = new RelayCommand(parameter =>
        {
            if (parameter is DockPlacement placement)
            {
                RequestPlacement(placement);
            }
        });

        _settingsService.SettingsReplaced += (_, _) =>
        {
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(Items));
        };

        ((App)System.Windows.Application.Current).ThemeService.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ThemeRevision));
    }

    public AppSettings Settings => _settingsService.Settings;
    public ObservableCollection<DockItem> Items => Settings.Items;
    public int ThemeRevision => ((App)System.Windows.Application.Current).ThemeService.Revision;

    public ICommand LaunchCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ToggleLockCommand { get; }
    public ICommand ToggleClickThroughCommand { get; }
    public ICommand PlacementCommand { get; }

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<DockPlacement>? PlacementRequested;
    public event EventHandler<string>? NotificationRequested;

    public void RequestPlacement(DockPlacement placement)
    {
        Settings.Placement = placement;
        Settings.Orientation = DockPositionService.OrientationFor(placement, Settings.Orientation);
        PlacementRequested?.Invoke(this, placement);
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths.Where(LaunchTargetService.IsSupported))
        {
            var normalized = LaunchTargetService.NormalizePath(path);
            if (Items.Any(item => string.Equals(item.TargetPath, normalized, StringComparison.OrdinalIgnoreCase))) continue;
            Items.Add(LaunchTargetService.Create(normalized, Settings.DefaultIconVisualMode));
            added++;
        }

        if (added > 0)
        {
            await _settingsService.SaveAsync();
            ((App)System.Windows.Application.Current).IconService.ClearCache();
        }
        else
        {
            var localization = ((App)System.Windows.Application.Current).LocalizationService;
            NotificationRequested?.Invoke(this, localization.Translate("Message.OnlyExeLnk"));
        }
    }

    public void MoveItem(DockItem item, DockItem target)
    {
        var from = Items.IndexOf(item);
        var to = Items.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        SetHover(null);
        Items.Move(from, to);
    }

    public void SetHover(DockItem? hoveredItem)
    {
        var hoveredIndex = hoveredItem is null ? -1 : Items.IndexOf(hoveredItem);
        var visualHoverEnabled = Settings.EnableHoverAnimation && System.Windows.SystemParameters.ClientAreaAnimation;
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            item.IsHovered = visualHoverEnabled && index == hoveredIndex;
            item.HoverScale = CalculateHoverScale(index, hoveredIndex, Settings, visualHoverEnabled);
            var hoverOffset = CalculateHoverOffset(index, hoveredIndex, Settings, visualHoverEnabled);
            item.HoverOffsetX = Settings.Orientation == DockOrientation.Horizontal ? hoverOffset : 0d;
            item.HoverOffsetY = Settings.Orientation == DockOrientation.Vertical ? hoverOffset : 0d;
        }
    }

    public static double CalculateHoverScale(
        int itemIndex,
        int hoveredIndex,
        AppSettings settings,
        bool visualHoverEnabled)
    {
        if (!visualHoverEnabled || hoveredIndex < 0)
        {
            return 1.0;
        }

        if (itemIndex == hoveredIndex)
        {
            return settings.HoverScale;
        }

        var distance = Math.Abs(itemIndex - hoveredIndex);
        if (distance == 1)
        {
            return Math.Min(settings.AdjacentHoverScale, settings.HoverScale);
        }

        if (distance == 2)
        {
            var secondNeighborScale = 1.0 + ((settings.AdjacentHoverScale - 1.0) * 0.35);
            return Math.Min(secondNeighborScale, settings.HoverScale);
        }

        return 1.0;
    }

    public static double CalculateHoverOffset(
        int itemIndex,
        int hoveredIndex,
        AppSettings settings,
        bool visualHoverEnabled)
    {
        if (!visualHoverEnabled || hoveredIndex < 0 || itemIndex == hoveredIndex)
        {
            return 0d;
        }

        var distance = Math.Abs(itemIndex - hoveredIndex);
        if (distance > 2)
        {
            return 0d;
        }

        var requiredPush = Math.Max(
            0d,
            (settings.IconSize * ((settings.HoverScale + settings.AdjacentHoverScale) / 2d - 1d))
            - settings.IconSpacing);
        var attenuation = distance == 1 ? 1d : 0.35d;
        return Math.Sign(itemIndex - hoveredIndex) * requiredPush * attenuation;
    }

    /// <summary>
    /// Public hook for the View to surface a drop-failure message through the
    /// same channel the Add-failure path uses, so the user always gets a
    /// localized balloon instead of a silent exception.
    /// </summary>
    public void OnDropFailed(string message) => NotificationRequested?.Invoke(this, message);
}
