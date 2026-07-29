using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using BudsDock.Models;
using BudsDock.Services;
using BudsDock.ViewModels;
using BudsDock.Views;

namespace BudsDock;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private DockWindow? _dockWindow;
    private SettingsWindow? _settingsWindow;
    private TrayService? _trayService;
    private DockViewModel? _dockViewModel;
    private SettingsViewModel? _settingsViewModel;
    private StartupService? _startupService;
    private NativeWindowService? _nativeWindowService;
    private TextWriterTraceListener? _bindingTraceListener;
    private StreamWriter? _bindingTraceWriter;
    private bool _rollingBackAutoStart;
    private bool _lastKnownAutoStart;

    public App()
    {
        IconService = new IconService();
        LocalizationService = new LocalizationService();
        ThemeService = new ThemeService();
    }

    public IconService IconService { get; }
    public LocalizationService LocalizationService { get; }
    public ThemeService ThemeService { get; }
    public SettingsService SettingsService { get; private set; } = null!;
    public bool IsShuttingDown { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var isSmokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var isDockVisualTest = e.Args.Contains("--dock-visual-test", StringComparer.OrdinalIgnoreCase);
        var isUiTest = e.Args.Contains("--ui-test", StringComparer.OrdinalIgnoreCase) || isDockVisualTest;
        var isDiagnostic = isSmokeTest || isUiTest;
        var mutexName = isSmokeTest
            ? "BudsDock.SmokeTest.SingleInstance"
            : isUiTest
                ? "BudsDock.UiTest.SingleInstance"
                : "BudsDock.SingleInstance";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            var loc = LocalizationService;
            MessageBox.Show(
                loc.Translate("Message.AlreadyRunning"),
                loc.Translate("App.Name"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            SettingsService = new SettingsService();
            if (isDiagnostic)
            {
                EnableBindingDiagnostics();
            }
            var settings = await SettingsService.LoadAsync();
            if (isDiagnostic)
            {
                ApplyDiagnosticPreferences(e.Args, settings);
            }
            LocalizationService.Apply(settings.Language);
            ThemeService.Apply(settings.ThemeMode);

            _startupService = new StartupService();
            _nativeWindowService = new NativeWindowService();
            var launcherService = new LauncherService();
            launcherService.LaunchFailed += OnLaunchFailed;

            _dockViewModel = new DockViewModel(SettingsService, launcherService);
            _settingsViewModel = new SettingsViewModel(SettingsService, _dockViewModel);
            _dockViewModel.OpenSettingsRequested += (_, _) => OpenSettings();
            _dockViewModel.ExitRequested += async (_, _) => await ExitApplicationAsync();
            _dockViewModel.NotificationRequested += (_, message) => _trayService?.ShowBalloon(LocalizationService.Translate("App.Name"), message);

            _dockWindow = new DockWindow(_dockViewModel, SettingsService, _nativeWindowService);
            MainWindow = _dockWindow;
            _settingsWindow = new SettingsWindow { DataContext = _settingsViewModel };
            if (isUiTest)
            {
                _dockWindow.ShowInTaskbar = true;
                _dockWindow.Title = "BudsDock Dock Test";
                ApplyDiagnosticWindowSize(e.Args, _settingsWindow);
            }

            SettingsService.SettingChanged += OnSettingChanged;
            SettingsService.SettingsReplaced += OnSettingsReplaced;
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            var startupApplied = true;
            if (!isDiagnostic)
            {
                _lastKnownAutoStart = _startupService.TryGetEnabled(out var actualAutoStart)
                    ? actualAutoStart
                    : settings.AutoStart;
                startupApplied = _startupService.Apply(settings.AutoStart);
                if (startupApplied)
                {
                    _lastKnownAutoStart = settings.AutoStart;
                }
            }
            else
            {
                _lastKnownAutoStart = settings.AutoStart;
            }
            CreateTray(settings);
            if (!startupApplied)
            {
                RollBackAutoStart(settings, _lastKnownAutoStart);
                _trayService?.ShowBalloon(LocalizationService.Translate("App.Name"), LocalizationService.Translate("Message.StartupFailed"));
            }
            if (isDockVisualTest)
            {
                settings.Placement = DockPlacement.ScreenCenter;
                settings.Orientation = DockOrientation.Horizontal;
            }
            _dockWindow.Show();
            if (isDockVisualTest)
            {
                _dockWindow.ApplyPlacement(DockPlacement.ScreenCenter);
                _ = Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(350);
                    if (_dockViewModel?.Items.Count > 0)
                    {
                        _dockViewModel.SetHover(_dockViewModel.Items[_dockViewModel.Items.Count / 2]);
                    }
                    await Task.Delay(260);
                    if (_dockWindow is not null)
                    {
                        File.WriteAllText(
                            Path.Combine(SettingsService.DataDirectory, "dock-visual-diagnostics.txt"),
                            _dockWindow.GetVisualDiagnostics());
                    }
                    if (_dockWindow?.ContextMenu is { } menu)
                    {
                        _dockWindow.Activate();
                        menu.PlacementTarget = _dockWindow;
                        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                        menu.IsOpen = true;
                    }
                });
            }
            if (isDiagnostic)
            {
                settings.IsClickThrough = false;
                settings.HideOnFullscreen = false;
                _settingsViewModel.SelectedPageIndex = isSmokeTest ? 1 : 0;
                if (!isDockVisualTest)
                {
                    OpenSettings();
                }
            }
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (_dockWindow is not null && !_dockWindow.RecoveryHotkeyRegistered)
                {
                    _trayService?.ShowBalloon(LocalizationService.Translate("App.Name"), LocalizationService.Translate("Message.HotkeyUnavailable"));
                }
                _settingsViewModel?.SetRecoveryHotkeyAvailable(_dockWindow?.RecoveryHotkeyRegistered == true);
            });

            if (isSmokeTest)
            {
                await Task.Delay(1200);
                _bindingTraceWriter?.Flush();
                var bindingLogPath = Path.Combine(SettingsService.DataDirectory, "binding-errors.log");
                if (File.Exists(bindingLogPath) && new FileInfo(bindingLogPath).Length > 0)
                {
                    throw new InvalidOperationException($"WPF binding errors were recorded in {bindingLogPath}.");
                }
                await ExitApplicationAsync();
            }
        }
        catch (Exception ex)
        {
            if (isDiagnostic)
            {
                var smokeDirectory = Environment.GetEnvironmentVariable("BUDSDOCK_DATA_DIR") ?? Path.GetTempPath();
                Directory.CreateDirectory(smokeDirectory);
                File.WriteAllText(Path.Combine(smokeDirectory, "startup-error.log"), ex.ToString());
            }
            else
            {
                MessageBox.Show(
                    $"{LocalizationService.Translate("Message.StartupFailedHard")}\n\n{ex.Message}",
                    LocalizationService.Translate("App.Name"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            IsShuttingDown = true;
            Shutdown(1);
        }
    }

    private void CreateTray(AppSettings settings)
    {
        _trayService?.Dispose();
        _trayService = new TrayService(LocalizationService, settings);
        _trayService.OpenSettingsRequested += (_, _) => OpenSettings();
        _trayService.RestoreInteractionRequested += (_, _) => _dockWindow?.RestoreInteraction();
        _trayService.ExitRequested += async (_, _) => await ExitApplicationAsync();
        _trayService.PlacementRequested += (_, placement) => _dockViewModel?.RequestPlacement(placement);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }
        _settingsWindow.Activate();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        var settings = SettingsService.Settings;
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ThemeMode):
                ThemeService.Apply(settings.ThemeMode);
                IconService.ClearCache();
                break;
            case nameof(AppSettings.Language):
                LocalizationService.Apply(settings.Language);
                _settingsWindow?.RefreshLocalization();
                _settingsViewModel?.RefreshLocalizedProperties();
                _trayService?.RebuildMenu();
                break;
            case nameof(AppSettings.AutoStart):
                if (!_rollingBackAutoStart && _startupService is not null)
                {
                    var priorState = _startupService.TryGetEnabled(out var actualState)
                        ? actualState
                        : _lastKnownAutoStart;
                    if (_startupService.Apply(settings.AutoStart))
                    {
                        _lastKnownAutoStart = settings.AutoStart;
                    }
                    else
                    {
                        RollBackAutoStart(settings, priorState);
                        _trayService?.ShowBalloon(LocalizationService.Translate("App.Name"), LocalizationService.Translate("Message.StartupFailed"));
                    }
                }
                break;
            case nameof(AppSettings.IsClickThrough):
            case nameof(AppSettings.IsPositionLocked):
                _trayService?.RebuildMenu();
                break;
        }
    }

    private void OnSettingsReplaced(object? sender, EventArgs e)
    {
        var settings = SettingsService.Settings;
        LocalizationService.Apply(settings.Language);
        ThemeService.Apply(settings.ThemeMode);
        var priorAutoStart = _startupService?.TryGetEnabled(out var actualAutoStart) == true
            ? actualAutoStart
            : _lastKnownAutoStart;
        var startupApplied = _startupService?.Apply(settings.AutoStart) != false;
        if (startupApplied)
        {
            _lastKnownAutoStart = settings.AutoStart;
        }
        IconService.ClearCache();
        CreateTray(settings);
        if (!startupApplied)
        {
            RollBackAutoStart(settings, priorAutoStart);
            _trayService?.ShowBalloon(LocalizationService.Translate("App.Name"), LocalizationService.Translate("Message.StartupFailed"));
        }
        _settingsWindow?.RefreshLocalization();
        _settingsViewModel?.RefreshLocalizedProperties();
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (SettingsService.Settings.ThemeMode == BudsDock.Models.ThemeMode.System)
            {
                ThemeService.Apply(BudsDock.Models.ThemeMode.System);
                IconService.ClearCache();
            }
            RefreshFixedPlacement();
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.Invoke(RefreshFixedPlacement);

    private void RefreshFixedPlacement()
    {
        if (_dockWindow is not null && SettingsService.Settings.Placement != DockPlacement.Free)
        {
            _dockWindow.ApplyPlacement(SettingsService.Settings.Placement);
        }
    }

    private void OnLaunchFailed(object? sender, string message)
    {
        MessageBox.Show(
            $"{LocalizationService.Translate("Message.LaunchFailed")}\n{message}", LocalizationService.Translate("App.Name"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async Task ExitApplicationAsync()
    {
        if (IsShuttingDown)
        {
            return;
        }

        IsShuttingDown = true;
        if (_settingsWindow is not null)
        {
            _settingsWindow.IsEnabled = false;
        }
        if (_dockWindow is not null)
        {
            _dockWindow.IsEnabled = false;
        }
        _trayService?.SetInteractionEnabled(false);
        SettingsService.StopScheduledSave();
        try
        {
            if (_settingsViewModel is not null)
            {
                await _settingsViewModel.WaitForPendingOperationsAsync();
            }
        }
        catch
        {
            // Async commands surface their own localized error. Exit still
            // performs the final settings save after the operation settles.
        }
        while (true)
        {
            try
            {
                await SettingsService.SaveAsync();
                if (SettingsService.SaveState == SaveState.Saved)
                {
                    break;
                }
                continue;
            }
            catch (Exception ex)
            {
                var result = MessageBox.Show(
                    $"{LocalizationService.Translate("Message.ExitSaveFailed")}\n\n{ex.Message}\n\n{LocalizationService.Translate("Message.ExitSaveChoices")}",
                    LocalizationService.Translate("App.Name"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    continue;
                }
                if (result == MessageBoxResult.Cancel)
                {
                    IsShuttingDown = false;
                    if (_settingsWindow is not null)
                    {
                        _settingsWindow.IsEnabled = true;
                    }
                    if (_dockWindow is not null)
                    {
                        _dockWindow.IsEnabled = true;
                    }
                    _trayService?.SetInteractionEnabled(true);
                    return;
                }
                break;
            }
        }

        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _trayService?.Dispose();
        _trayService = null;
        if (_settingsWindow?.IsLoaded == true)
        {
            _settingsWindow.Close();
        }
        if (_dockWindow?.IsLoaded == true)
        {
            _dockWindow.Close();
        }
        Shutdown();
    }

    private void RollBackAutoStart(AppSettings settings, bool actualState)
    {
        _rollingBackAutoStart = true;
        try
        {
            settings.AutoStart = actualState;
            _lastKnownAutoStart = actualState;
        }
        finally
        {
            _rollingBackAutoStart = false;
        }
    }

    private void EnableBindingDiagnostics()
    {
        var bindingLogPath = Path.Combine(SettingsService.DataDirectory, "binding-errors.log");
        _bindingTraceWriter = new StreamWriter(bindingLogPath, append: false) { AutoFlush = true };
        _bindingTraceListener = new TextWriterTraceListener(_bindingTraceWriter);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_bindingTraceListener);
    }

    private static void ApplyDiagnosticPreferences(string[] args, AppSettings settings)
    {
        var themeArgument = args.FirstOrDefault(argument => argument.StartsWith("--ui-test-theme=", StringComparison.OrdinalIgnoreCase));
        if (themeArgument is not null
            && Enum.TryParse<ThemeMode>(themeArgument.Split('=', 2)[1], ignoreCase: true, out var theme))
        {
            settings.ThemeMode = theme;
        }

        var languageArgument = args.FirstOrDefault(argument => argument.StartsWith("--ui-test-language=", StringComparison.OrdinalIgnoreCase));
        if (languageArgument is not null
            && Enum.TryParse<AppLanguage>(languageArgument.Split('=', 2)[1], ignoreCase: true, out var language))
        {
            settings.Language = language;
        }
    }

    private static void ApplyDiagnosticWindowSize(string[] args, SettingsWindow window)
    {
        var sizeArgument = args.FirstOrDefault(argument => argument.StartsWith("--ui-test-size=", StringComparison.OrdinalIgnoreCase));
        var value = sizeArgument?.Split('=', 2)[1];
        var parts = value?.Split('x', 2, StringSplitOptions.TrimEntries);
        if (parts is { Length: 2 }
            && double.TryParse(parts[0], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var width)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var height))
        {
            window.Width = Math.Max(320, width);
            window.Height = Math.Max(320, height);
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var directory = SettingsService?.DataDirectory
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BudsDock");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTime.Now:O}] {e.Exception}\n\n");
        }
        catch
        {
            // Crash logging must not recursively fail.
        }

        MessageBox.Show(e.Exception.Message, LocalizationService.Translate("App.Name"), MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        if (_bindingTraceListener is not null)
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(_bindingTraceListener);
            _bindingTraceListener.Flush();
            _bindingTraceListener.Close();
            _bindingTraceListener = null;
            _bindingTraceWriter = null;
        }
        _trayService?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
