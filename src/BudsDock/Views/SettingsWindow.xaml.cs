using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BudsDock.ViewModels;

namespace BudsDock.Views;

public partial class SettingsWindow : Window
{
    private const double CompactBreakpoint = 900;
    private const double PreferredMinWidth = 640;
    private const double PreferredMinHeight = 480;

    public static readonly DependencyProperty IsCompactLayoutProperty = DependencyProperty.Register(
        nameof(IsCompactLayout),
        typeof(bool),
        typeof(SettingsWindow),
        new PropertyMetadata(false));

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
    }

    public bool IsCompactLayout
    {
        get => (bool)GetValue(IsCompactLayoutProperty);
        private set => SetValue(IsCompactLayoutProperty, value);
    }

    public void RefreshLocalization()
    {
        ThemeCombo.Items.Refresh();
        LanguageCombo.Items.Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWorkAreaConstraints();
        UpdateLayoutMode();
        UpdateMaximizeButtonAccessibility();
    }

    private void ApplyWorkAreaConstraints()
    {
        var workArea = GetCurrentMonitorWorkArea();
        MaxWidth = Math.Max(320, workArea.Width - 32);
        MaxHeight = Math.Max(320, workArea.Height - 32);
        MinWidth = Math.Min(PreferredMinWidth, MaxWidth);
        MinHeight = Math.Min(PreferredMinHeight, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var pixels = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(pixels.Left, pixels.Top));
        var bottomRight = transform.Transform(new Point(pixels.Right, pixels.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            var openCombo = FindVisualDescendants<ComboBox>(this).FirstOrDefault(combo => combo.IsDropDownOpen);
            if (openCombo is not null)
            {
                openCombo.IsDropDownOpen = false;
                e.Handled = true;
                return;
            }
            if (IsCompactLayout && DataContext is SettingsViewModel compactViewModel && compactViewModel.IsCompactDetailsOpen)
            {
                compactViewModel.IsCompactDetailsOpen = false;
                e.Handled = true;
                return;
            }
            Hide();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.F)
        {
            viewModel.SelectedPageIndex = 0;
            viewModel.IsCompactDetailsOpen = false;
            IconSearchBox.Focus();
            IconSearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        var index = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            _ => -1
        };
        if (index >= 0)
        {
            viewModel.SelectedPageIndex = index;
            e.Handled = true;
        }
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void ToggleMaximizeRestore()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (WindowState == WindowState.Maximized)
        {
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            WindowSurface.CornerRadius = new CornerRadius(0);
            if (chrome is not null)
            {
                chrome.CornerRadius = new CornerRadius(0);
            }
        }
        else
        {
            ApplyWorkAreaConstraints();
            WindowSurface.CornerRadius = new CornerRadius(12);
            if (chrome is not null)
            {
                chrome.CornerRadius = new CornerRadius(12);
            }
        }
        UpdateMaximizeButtonAccessibility();
    }

    private void UpdateMaximizeButtonAccessibility()
    {
        if (MaximizeButton is null)
        {
            return;
        }
        var key = WindowState == WindowState.Maximized ? "Action.Restore" : "Action.Maximize";
        MaximizeButton.SetResourceReference(ToolTipProperty, key);
        MaximizeButton.SetResourceReference(AutomationProperties.NameProperty, key);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (IsLoaded && WindowState == WindowState.Normal)
        {
            ApplyWorkAreaConstraints();
        }
    }

    private void UpdateLayoutMode() => IsCompactLayout = ActualWidth > 0 && ActualWidth < CompactBreakpoint;

    private void OnIconListMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsCompactLayout && DataContext is SettingsViewModel viewModel && viewModel.SelectedItem is not null)
        {
            viewModel.OpenCompactDetails();
        }
    }

    private void OnIconListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsCompactLayout
            && e.Key is Key.Enter or Key.Space
            && DataContext is SettingsViewModel viewModel
            && viewModel.SelectedItem is not null)
        {
            viewModel.OpenCompactDetails();
            e.Handled = true;
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

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
