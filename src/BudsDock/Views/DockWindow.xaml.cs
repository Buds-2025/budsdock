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
    private Point _iconDragStart;
    private DockItem? _dragCandidate;
    private bool _draggingItem;
    private bool _draggingWindow;

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

        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _fullscreenTimer.Tick += OnFullscreenTimerTick;
        if (_viewModel.Settings.HideOnFullscreen) _fullscreenTimer.Start();
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
        if (!IsLoaded || _draggingWindow)
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
        _placementOperation = null;
        if (_draggingWindow) return;
        _applyingPlacement = true;
        try
        {
            var (size, offset) = GetDockVisualGeometry();
            var screen = DisplayService.Resolve(_viewModel.Settings.MonitorId);
            var scale = DisplayService.GetScale(_handle);
            var workArea = DisplayService.LocalWorkArea(screen, scale);
            DockViewport.MaxWidth = Math.Max(100, workArea.Width - 16);
            DockViewport.MaxHeight = Math.Max(100, workArea.Height - 16);
            UpdateLayout();
            (size, offset) = GetDockVisualGeometry();
            Point point;
            if (_pendingPlacement == DockPlacement.Free && _viewModel.Settings.RelativeX is double x
                && _viewModel.Settings.RelativeY is double y)
            {
                point = new Point(x * Math.Max(0, workArea.Width - size.Width),
                    y * Math.Max(0, workArea.Height - size.Height));
            }
            else if (_pendingPlacement == DockPlacement.Free && _viewModel.Settings.Left is double left
                && _viewModel.Settings.Top is double top)
            {
                // Legacy positions were stored in primary-monitor DIPs.
                point = DockPositionService.Clamp(new Point(left, top), size, workArea);
            }
            else
            {
                var taskbarHeight = (screen.Bounds.Bottom - screen.WorkingArea.Bottom) / scale;
                point = DockPositionService.Calculate(_pendingPlacement, size, workArea,
                    DockPositionService.EdgeMargin, taskbarHeight >= 8 ? taskbarHeight : 0);
            }
            NativeMethods.SetWindowPos(_handle, IntPtr.Zero,
                (int)Math.Round(screen.WorkingArea.Left + (point.X - offset.X) * scale),
                (int)Math.Round(screen.WorkingArea.Top + (point.Y - offset.Y) * scale), 0, 0,
                NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);

        }
        finally
        {
            _applyingPlacement = false;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Topmost = _viewModel.Settings.Topmost;
        ApplyPlacement(_viewModel.Settings.Placement);
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
        if (message == NativeMethods.WmDpiChanged)
            ApplyPlacement(_viewModel.Settings.Placement);
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
            _draggingWindow = true;
            DragMove();
            _draggingWindow = false;
            var screen = System.Windows.Forms.Screen.FromHandle(_handle);
            _viewModel.Settings.MonitorId = screen.DeviceName;
            var scale = DisplayService.GetScale(_handle);
            DockViewport.MaxWidth = Math.Max(100, screen.WorkingArea.Width / scale - 16);
            DockViewport.MaxHeight = Math.Max(100, screen.WorkingArea.Height / scale - 16);
            UpdateLayout();
            var (size, offset) = GetDockVisualGeometry();
            NativeMethods.GetWindowRect(_handle, out var rect);
            var workArea = DisplayService.LocalWorkArea(screen, scale);
            var clamped = DockPositionService.Clamp(new Point(
                (rect.Left - screen.WorkingArea.Left) / scale + offset.X,
                (rect.Top - screen.WorkingArea.Top) / scale + offset.Y), size, workArea);
            _viewModel.Settings.Placement = DockPlacement.Free;
            _viewModel.Settings.RelativeX = clamped.X / Math.Max(1, workArea.Width - size.Width);
            _viewModel.Settings.RelativeY = clamped.Y / Math.Max(1, workArea.Height - size.Height);
            ApplyPlacement(DockPlacement.Free);
        }
        catch (InvalidOperationException)
        {
            // DragMove can fail if the mouse button is released during message dispatch.
        }
        finally { _draggingWindow = false; }
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
        if (e.Data.GetDataPresent(typeof(DockItem)) && e.Data.GetData(typeof(DockItem)) is DockItem source)
        {
            var target = FindDockItem(e.OriginalSource as DependencyObject);
            if (target is not null) _viewModel.MoveItem(source, target);
            e.Handled = true;
            DropOverlay.Visibility = Visibility.Collapsed;
            return;
        }
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
        if (e.Data.GetDataPresent(typeof(DockItem)))
        {
            e.Effects = FindDockItem(e.OriginalSource as DependencyObject) is not null ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var valid = e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(LaunchTargetService.IsSupported);
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnIconPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _iconDragStart = e.GetPosition(this);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as DockItem;
    }

    private void OnIconPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingItem || _dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _iconDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _iconDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _draggingItem = true;
        ClearHover();
        try
        {
            Mouse.Capture(null);
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(DockItem), _dragCandidate), DragDropEffects.Move);
        }
        finally { _dragCandidate = null; _draggingItem = false; ClearHover(); }
        e.Handled = true;
    }

    private void OnIconContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DockItem item) return;
        ClearHover();
        var app = (App)Application.Current;
        var menu = new ContextMenu { Style = (Style)FindResource("DockContextMenuStyle") };
        void Add(string key, Action action)
        {
            var entry = new MenuItem { Header = app.LocalizationService.Translate(key), Style = (Style)FindResource("DockContextMenuItemStyle") };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }
        Add("Dock.Open", () => _viewModel.LaunchCommand.Execute(item));
        Add("Dock.Edit", () => app.EditItem(item));
        Add("Action.MoveUp", () => { var i = _viewModel.Items.IndexOf(item); if (i > 0) _viewModel.MoveItem(item, _viewModel.Items[i - 1]); });
        Add("Action.MoveDown", () => { var i = _viewModel.Items.IndexOf(item); if (i + 1 < _viewModel.Items.Count) _viewModel.MoveItem(item, _viewModel.Items[i + 1]); });
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnDockPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { ClearHover(); Keyboard.ClearFocus(); e.Handled = true; }
        else if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        { _viewModel.OpenSettingsCommand.Execute(null); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Keyboard.FocusedElement is UIElement element)
        {
            var previous = e.Key is Key.Left or Key.Up;
            element.MoveFocus(new TraversalRequest(previous ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
            e.Handled = true;
        }
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

        if (_draggingItem || e.LeftButton == MouseButtonState.Pressed) return;
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
        if (!_applyingPlacement)
        {
            ApplyPlacement(_viewModel.Settings.Placement);
        }
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.HideOnFullscreen):
                if (_viewModel.Settings.HideOnFullscreen) _fullscreenTimer.Start(); else _fullscreenTimer.Stop();
                OnFullscreenTimerTick(null, EventArgs.Empty);
                break;
            case nameof(AppSettings.MonitorId):
                ApplyPlacement(_viewModel.Settings.Placement);
                break;
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
                ApplyPlacement(_viewModel.Settings.Placement);
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
        if (_viewModel.Settings.HideOnFullscreen) _fullscreenTimer.Start(); else _fullscreenTimer.Stop();
        OnFullscreenTimerTick(null, EventArgs.Empty);
        ApplyClickThrough();
        ApplyPlacement(_viewModel.Settings.Placement);
    }

    private void ApplyClickThrough()
    {
        if (_handle == IntPtr.Zero || _nativeWindowService.ApplyClickThrough(_handle, _viewModel.Settings.IsClickThrough, ShowInTaskbar))
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
            && _nativeWindowService.IsForegroundFullscreen(_handle);

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
