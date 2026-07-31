using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BudsDock.Interop;
using BudsDock.Models;
using BudsDock.Services;
using BudsDock.ViewModels;

namespace BudsDock.Views;

public partial class DockWindow : Window
{
    private const int RecoveryHotkeyId = 0xB0D;
    private readonly DockViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly NativeWindowService _nativeWindowService;
    private readonly DispatcherTimer _fullscreenTimer;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _hiddenForFullscreen;
    private bool _applyingPlacement;
    private bool _clickThroughFailureShown;
    private long _hoverRevision;
    private DispatcherOperation? _placementOperation;
    private DockPlacement _pendingPlacement;

    public DockWindow(DockViewModel viewModel, SettingsService settingsService, NativeWindowService nativeWindowService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _settingsService = settingsService;
        _nativeWindowService = nativeWindowService;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += OnSizeChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        _viewModel.PlacementRequested += OnPlacementRequested;
        _settingsService.SettingChanged += OnSettingChanged;
        _settingsService.SettingsReplaced += OnSettingsReplaced;

        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _fullscreenTimer.Tick += OnFullscreenTimerTick;
        _fullscreenTimer.Start();
    }

    public bool RecoveryHotkeyRegistered { get; private set; }

    public void RestoreInteraction()
    {
        _viewModel.Settings.IsClickThrough = false;
        ApplyClickThrough();
        if (!IsVisible)
        {
            Show();
        }
        _hiddenForFullscreen = false;
        Activate();
    }

    public string GetVisualDiagnostics()
    {
        UpdateLayout();
        var rows = new List<string>
        {
            $"Window={ActualWidth:F2}x{ActualHeight:F2}",
            $"DockRoot={DockRoot.ActualWidth:F2}x{DockRoot.ActualHeight:F2}"
        };
        for (var index = 0; index < DockItems.Items.Count; index++)
        {
            if (DockItems.ItemContainerGenerator.ContainerFromIndex(index) is not ContentPresenter presenter)
            {
                continue;
            }

            var child = VisualTreeHelper.GetChildrenCount(presenter) > 0
                ? VisualTreeHelper.GetChild(presenter, 0) as FrameworkElement
                : null;
            var scale = child?.RenderTransform switch
            {
                ScaleTransform transform => transform.ScaleX,
                TransformGroup group => group.Children.OfType<ScaleTransform>().LastOrDefault()?.ScaleX ?? 1d,
                _ => 1d
            };
            rows.Add($"Item{index}=Container:{presenter.ActualWidth:F2}x{presenter.ActualHeight:F2};Scale:{scale:F3}");
        }
        return string.Join(Environment.NewLine, rows);
    }

    public void ApplyPlacement(DockPlacement placement)
    {
        if (!IsLoaded)
        {
            return;
        }

        _pendingPlacement = placement;
        if (_placementOperation is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        _placementOperation = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(ApplyPendingPlacement));
    }

    private void ApplyPendingPlacement()
    {
        _applyingPlacement = true;
        try
        {
            var (size, offset) = GetDockVisualGeometry();
            var workArea = SystemParameters.WorkArea;
            var bottomTaskbarHeight = GetBottomTaskbarHeight(workArea);
            var point = DockPositionService.Calculate(
                _pendingPlacement,
                size,
                workArea,
                DockPositionService.EdgeMargin,
                bottomTaskbarHeight);
            Left = point.X - offset.X;
            Top = point.Y - offset.Y;
        }
        finally
        {
            _applyingPlacement = false;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Topmost = _viewModel.Settings.Topmost;
        if (_viewModel.Settings.Left.HasValue && _viewModel.Settings.Top.HasValue && _viewModel.Settings.Placement == DockPlacement.Free)
        {
            UpdateLayout();
            var (size, offset) = GetDockVisualGeometry();
            var point = DockPositionService.Clamp(
                new Point(_viewModel.Settings.Left.Value, _viewModel.Settings.Top.Value),
                size,
                SystemParameters.WorkArea);
            Left = point.X - offset.X;
            Top = point.Y - offset.Y;
        }
        else
        {
            ApplyPlacement(_viewModel.Settings.Placement);
        }
        ApplyClickThrough();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WindowProcedure);
        RecoveryHotkeyRegistered = _nativeWindowService.RegisterRecoveryHotkey(_handle, RecoveryHotkeyId);
        ApplyClickThrough();
    }

    private IntPtr WindowProcedure(IntPtr window, int message, IntPtr wordParameter, IntPtr longParameter, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wordParameter.ToInt32() == RecoveryHotkeyId)
        {
            RestoreInteraction();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _viewModel.Settings.IsPositionLocked || FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
            var (size, offset) = GetDockVisualGeometry();
            var clamped = DockPositionService.Clamp(
                new Point(Left + offset.X, Top + offset.Y),
                size,
                SystemParameters.WorkArea);
            Left = clamped.X - offset.X;
            Top = clamped.Y - offset.Y;
            _viewModel.Settings.Placement = DockPlacement.Free;
            _viewModel.Settings.Left = clamped.X;
            _viewModel.Settings.Top = clamped.Y;
        }
        catch (InvalidOperationException)
        {
            // DragMove can fail if the mouse button is released during message dispatch.
        }
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // The Dock now exposes a ContextMenu at the Window level; we let the
        // system handle the gesture so the menu appears at the cursor.
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Settings.IsClickThrough)
        {
            _viewModel.OpenSettingsCommand.Execute(null);
        }
    }

    private void OnRestoreInteractionClick(object sender, RoutedEventArgs e)
    {
        RestoreInteraction();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ExitCommand.Execute(null);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }
        DropOverlay.Visibility = Visibility.Collapsed;
        try
        {
            await _viewModel.AddFilesAsync(paths);
        }
        catch (Exception ex)
        {
            var app = (App)Application.Current;
            _viewModel.OnDropFailed(app.LocalizationService.Translate("Message.OnlyExeLnk"));
            System.Diagnostics.Debug.WriteLine($"Drop failed: {ex}");
        }
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var valid = e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && paths.Any(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                                 || Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase));
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnIconMouseEnter(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DockItem item)
        {
            _hoverRevision++;
            _viewModel.SetHover(item);
        }
    }

    private void OnIconMouseLeave(object sender, MouseEventArgs e)
    {
        var revision = ++_hoverRevision;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (revision != _hoverRevision || !IsVisible || _viewModel.Settings.IsClickThrough)
            {
                return;
            }
            UpdateHoverFromMouse();
        }));
    }

    private void OnDockRootPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel.Settings.IsClickThrough)
        {
            return;
        }

        var item = FindDockItem(e.OriginalSource as DependencyObject)
                   ?? FindNearestDockItem(e.GetPosition(DockRoot));
        if (item is not null)
        {
            _hoverRevision++;
            _viewModel.SetHover(item);
        }
    }

    private void OnDockWindowMouseLeave(object sender, MouseEventArgs e) => ClearHover();

    private void ClearHover()
    {
        _hoverRevision++;
        _viewModel.SetHover(null);
    }

    private void UpdateHoverFromMouse()
    {
        var item = FindDockItem(Mouse.DirectlyOver as DependencyObject)
                   ?? FindNearestDockItem(Mouse.GetPosition(DockRoot));
        _viewModel.SetHover(item);
    }

    private DockItem? FindNearestDockItem(Point pointer)
    {
        if (!DockRoot.IsMouseOver || DockItems.Items.Count == 0)
        {
            return null;
        }

        DockItem? nearest = null;
        var shortestDistanceSquared = double.PositiveInfinity;
        for (var index = 0; index < DockItems.Items.Count; index++)
        {
            if (DockItems.ItemContainerGenerator.ContainerFromIndex(index) is not ContentPresenter presenter
                || presenter.DataContext is not DockItem item
                || presenter.ActualWidth <= 0
                || presenter.ActualHeight <= 0)
            {
                continue;
            }

            var bounds = presenter.TransformToAncestor(DockRoot)
                .TransformBounds(new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
            var deltaX = pointer.X - (bounds.Left + (bounds.Width / 2));
            var deltaY = pointer.Y - (bounds.Top + (bounds.Height / 2));
            var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            if (distanceSquared < shortestDistanceSquared)
            {
                shortestDistanceSquared = distanceSquared;
                nearest = item;
            }
        }

        return nearest;
    }

    private void OnPlacementRequested(object? sender, DockPlacement placement) => ApplyPlacement(placement);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_applyingPlacement && _viewModel.Settings.Placement != DockPlacement.Free)
        {
            ApplyPlacement(_viewModel.Settings.Placement);
        }
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.IsClickThrough):
                ApplyClickThrough();
                break;
            case nameof(AppSettings.Topmost):
                Topmost = _viewModel.Settings.Topmost;
                break;
            case nameof(AppSettings.EnableHoverAnimation):
            case nameof(AppSettings.HoverScale):
            case nameof(AppSettings.AdjacentHoverScale):
                if (_viewModel.Settings.EnableHoverAnimation)
                {
                    _hoverRevision++;
                    UpdateHoverFromMouse();
                }
                else
                {
                    ClearHover();
                }
                break;
            case nameof(AppSettings.Orientation):
            case nameof(AppSettings.DockScale):
            case nameof(AppSettings.IconSize):
            case nameof(AppSettings.IconSpacing):
            case nameof(AppSettings.PanelPadding):
                if (_viewModel.Settings.Placement != DockPlacement.Free)
                {
                    ApplyPlacement(_viewModel.Settings.Placement);
                }
                if (e.PropertyName == nameof(AppSettings.IconSize))
                {
                    _hoverRevision++;
                    UpdateHoverFromMouse();
                }
                else if (e.PropertyName == nameof(AppSettings.Orientation))
                {
                    _hoverRevision++;
                    UpdateHoverFromMouse();
                }
                break;
        }
    }

    private void OnSettingsReplaced(object? sender, EventArgs e)
    {
        Topmost = _viewModel.Settings.Topmost;
        ApplyClickThrough();
        ApplyPlacement(_viewModel.Settings.Placement);
    }

    private void ApplyClickThrough()
    {
        if (_handle == IntPtr.Zero || _nativeWindowService.ApplyClickThrough(_handle, _viewModel.Settings.IsClickThrough))
        {
            ClearHover();
            _clickThroughFailureShown = false;
            return;
        }

        if (_viewModel.Settings.IsClickThrough)
        {
            _viewModel.Settings.IsClickThrough = false;
        }

        if (!_clickThroughFailureShown)
        {
            _clickThroughFailureShown = true;
            var localization = ((App)Application.Current).LocalizationService;
            MessageBox.Show(
                localization.Translate("Message.ClickThroughFailed"),
                "BudsDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnFullscreenTimerTick(object? sender, EventArgs e)
    {
        var shouldHide = _viewModel.Settings.HideOnFullscreen
            && _nativeWindowService.IsForegroundFullscreen();

        if (shouldHide && !_hiddenForFullscreen)
        {
            _hiddenForFullscreen = true;
            ClearHover();
            Hide();
        }
        else if (!shouldHide && _hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            Show();
            ClearHover();
            Topmost = _viewModel.Settings.Topmost;
            ApplyClickThrough();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!((App)Application.Current).IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _fullscreenTimer.Stop();
        _nativeWindowService.UnregisterRecoveryHotkey(_handle, RecoveryHotkeyId);
        _source?.RemoveHook(WindowProcedure);
        _viewModel.PlacementRequested -= OnPlacementRequested;
        _settingsService.SettingChanged -= OnSettingChanged;
        _settingsService.SettingsReplaced -= OnSettingsReplaced;
    }

    private static DockItem? FindDockItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement element && element.DataContext is DockItem item)
            {
                return item;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static double GetBottomTaskbarHeight(Rect workArea)
    {
        var height = SystemParameters.PrimaryScreenHeight - workArea.Bottom;
        return height >= 8 ? height : 0;
    }

    private (Size Size, Point Offset) GetDockVisualGeometry()
    {
        var bounds = DockRoot.TransformToAncestor(this)
            .TransformBounds(new Rect(0, 0, DockRoot.ActualWidth, DockRoot.ActualHeight));
        return (new Size(bounds.Width, bounds.Height), new Point(bounds.Left, bounds.Top));
    }
}
