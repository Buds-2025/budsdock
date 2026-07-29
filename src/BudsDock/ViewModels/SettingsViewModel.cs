using System.Diagnostics;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using BudsDock.Models;
using BudsDock.Services;

namespace BudsDock.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly DockViewModel _dockViewModel;
    private DockItem? _selectedItem;
    private int _selectedPageIndex;
    private bool _isCompactDetailsOpen;
    private bool _isRecoveryHotkeyAvailable = true;
    private readonly List<AsyncRelayCommand> _asyncCommands = [];
    private System.Collections.ObjectModel.ObservableCollection<DockItem>? _observedItems;

    public SettingsViewModel(SettingsService settingsService, DockViewModel dockViewModel)
    {
        _settingsService = settingsService;
        _dockViewModel = dockViewModel;

        AddCommand = CreateAsyncCommand(AddApplicationAsync);
        RemoveCommand = new RelayCommand(RemoveSelected, () => SelectedItem is not null);
        MoveUpCommand = new RelayCommand(() => MoveSelected(-1), () => CanMove(-1));
        MoveDownCommand = new RelayCommand(() => MoveSelected(1), () => CanMove(1));
        ChooseIconCommand = CreateAsyncCommand(ChooseIconAsync, () => SelectedItem is not null);
        ResetIconCommand = new RelayCommand(ResetIcon, () => SelectedItem is not null && !string.IsNullOrWhiteSpace(SelectedItem.CustomIconPath));
        BrowseTargetCommand = CreateAsyncCommand(BrowseTargetAsync, () => SelectedItem is { IsBuiltIn: false });
        BrowseWorkingDirectoryCommand = new RelayCommand(BrowseWorkingDirectory, () => SelectedItem is not null);
        ClearWorkingDirectoryCommand = new RelayCommand(ClearWorkingDirectory, () => SelectedItem is not null && !string.IsNullOrWhiteSpace(SelectedItem.WorkingDirectory));
        ExportCommand = CreateAsyncCommand(ExportAsync);
        ImportCommand = CreateAsyncCommand(ImportAsync);
        RetrySaveCommand = CreateAsyncCommand(_settingsService.SaveAsync);
        OpenDataDirectoryCommand = new RelayCommand(OpenDataDirectory);
        ResetAppearanceCommand = new RelayCommand(ResetAppearance);
        BackToIconListCommand = new RelayCommand(() => IsCompactDetailsOpen = false);
        PlacementCommand = _dockViewModel.PlacementCommand;

        AttachCollection(Settings);
        SelectedItem = Settings.Items.FirstOrDefault();

        _settingsService.SettingsReplaced += (_, _) =>
        {
            AttachCollection(Settings);
            SelectedItem = Settings.Items.FirstOrDefault();
            IsCompactDetailsOpen = false;
            OnPropertyChanged(nameof(Settings));
        };
        _settingsService.PropertyChanged += OnSettingsServicePropertyChanged;

        ((App)Application.Current).ThemeService.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ThemeRevision));
    }

    public AppSettings Settings => _settingsService.Settings;
    public int ThemeRevision => ((App)Application.Current).ThemeService.Revision;
    public IReadOnlyList<ThemeMode> ThemeModes { get; } = Enum.GetValues<ThemeMode>();
    public IReadOnlyList<AppLanguage> Languages { get; } = Enum.GetValues<AppLanguage>();
    public IReadOnlyList<IconVisualMode> IconVisualModes { get; } = Enum.GetValues<IconVisualMode>();
    public IReadOnlyList<DockPlacement> FixedPlacements { get; } =
    [
        DockPlacement.TopCenter,
        DockPlacement.BottomCenter,
        DockPlacement.LeftCenter,
        DockPlacement.RightCenter,
        DockPlacement.ScreenCenter
    ];
    public SaveState SaveState => _settingsService.SaveState;
    public string SaveStatusText => ((App)Application.Current).LocalizationService.Translate(_settingsService.SaveState switch
    {
        SaveState.Saving => "Status.Saving",
        SaveState.Saved => "Status.Saved",
        SaveState.Failed => "Status.SaveFailed",
        _ => "Status.Ready"
    });
    public bool IsRecoveryHotkeyAvailable
    {
        get => _isRecoveryHotkeyAvailable;
        private set
        {
            if (SetProperty(ref _isRecoveryHotkeyAvailable, value))
            {
                OnPropertyChanged(nameof(RecoveryHotkeyStatusText));
            }
        }
    }
    public string RecoveryHotkeyStatusText => ((App)Application.Current).LocalizationService.Translate(
        IsRecoveryHotkeyAvailable ? "System.HotkeyAvailable" : "Message.HotkeyUnavailableShort");

    public DockItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            var previous = _selectedItem;
            if (SetProperty(ref _selectedItem, value))
            {
                if (previous is not null)
                {
                    previous.PropertyChanged -= OnSelectedItemPropertyChanged;
                }
                if (_selectedItem is not null)
                {
                    _selectedItem.PropertyChanged += OnSelectedItemPropertyChanged;
                }
                RaiseItemCommandStates();
            }
        }
    }

    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        set
        {
            if (SetProperty(ref _selectedPageIndex, value) && value != 0)
            {
                IsCompactDetailsOpen = false;
            }
        }
    }
    public bool IsCompactDetailsOpen
    {
        get => _isCompactDetailsOpen;
        set => SetProperty(ref _isCompactDetailsOpen, value);
    }

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ChooseIconCommand { get; }
    public ICommand ResetIconCommand { get; }
    public ICommand BrowseTargetCommand { get; }
    public ICommand BrowseWorkingDirectoryCommand { get; }
    public ICommand ClearWorkingDirectoryCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand RetrySaveCommand { get; }
    public ICommand OpenDataDirectoryCommand { get; }
    public ICommand ResetAppearanceCommand { get; }
    public ICommand BackToIconListCommand { get; }
    public ICommand PlacementCommand { get; }

    public void OpenCompactDetails()
    {
        if (SelectedItem is not null)
        {
            IsCompactDetailsOpen = true;
        }
    }

    public void SetRecoveryHotkeyAvailable(bool available) => IsRecoveryHotkeyAvailable = available;

    public Task WaitForPendingOperationsAsync()
        => Task.WhenAll(_asyncCommands.Select(command => command.Completion));

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(SaveStatusText));
        OnPropertyChanged(nameof(RecoveryHotkeyStatusText));
    }

    private async Task AddApplicationAsync()
    {
        var loc = ((App)Application.Current).LocalizationService;
        var dialog = new OpenFileDialog
        {
            Title = loc.Translate("Action.Add"),
            Filter = loc.Translate("Filter.Applications"),
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            await AddAndSelectAsync(dialog.FileNames);
        }
    }

    private async Task AddAndSelectAsync(string[] paths)
    {
        var previousCount = Settings.Items.Count;
        await _dockViewModel.AddFilesAsync(paths);
        if (Settings.Items.Count > previousCount)
        {
            SelectedItem = Settings.Items.Last();
        }
    }

    private void RemoveSelected()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var localization = ((App)Application.Current).LocalizationService;
        var result = MessageBox.Show(
            localization.Translate("Message.ConfirmRemove"),
            localization.Translate("Action.Remove"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var index = Settings.Items.IndexOf(SelectedItem);
        Settings.Items.Remove(SelectedItem);
        SelectedItem = Settings.Items.Count == 0 ? null : Settings.Items[Math.Clamp(index, 0, Settings.Items.Count - 1)];
        ((App)Application.Current).IconService.ClearCache();
    }

    private bool CanMove(int delta)
    {
        if (SelectedItem is null)
        {
            return false;
        }
        var index = Settings.Items.IndexOf(SelectedItem);
        var target = index + delta;
        return index >= 0 && target >= 0 && target < Settings.Items.Count;
    }

    private void MoveSelected(int delta)
    {
        if (!CanMove(delta) || SelectedItem is null)
        {
            return;
        }
        var index = Settings.Items.IndexOf(SelectedItem);
        Settings.Items.Move(index, index + delta);
        RaiseItemCommandStates();
    }

    private async Task ChooseIconAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var loc = ((App)Application.Current).LocalizationService;
        var dialog = new OpenFileDialog
        {
            Title = loc.Translate("Action.ChooseIcon"),
            Filter = loc.Translate("Filter.IconImages"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SelectedItem.CustomIconPath = await _settingsService.ImportIconAsync(dialog.FileName);
            ((App)Application.Current).IconService.ClearCache();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            var l = ((App)Application.Current).LocalizationService;
            MessageBox.Show($"{l.Translate("Message.IconFailed")}\n{ex.Message}", l.Translate("App.Name"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RaiseItemCommandStates();
    }

    private Task BrowseTargetAsync()
    {
        if (SelectedItem is null || SelectedItem.IsBuiltIn)
        {
            return Task.CompletedTask;
        }

        var loc = ((App)Application.Current).LocalizationService;
        var dialog = new OpenFileDialog
        {
            Title = loc.Translate("Action.BrowseTarget"),
            Filter = loc.Translate("Filter.Applications"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            SelectedItem.TargetPath = dialog.FileName;
            SelectedItem.WorkingDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            ((App)Application.Current).IconService.ClearCache();
        }
        return Task.CompletedTask;
    }

    private void BrowseWorkingDirectory()
    {
        if (SelectedItem is null)
        {
            return;
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = ((App)Application.Current).LocalizationService.Translate("Action.BrowseWorkingDirectory"),
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(SelectedItem.WorkingDirectory)
                ? SelectedItem.WorkingDirectory
                : Path.GetDirectoryName(SelectedItem.TargetPath) ?? string.Empty
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SelectedItem.WorkingDirectory = dialog.SelectedPath;
        }
    }

    private void ClearWorkingDirectory()
    {
        if (SelectedItem is not null)
        {
            SelectedItem.WorkingDirectory = string.Empty;
        }
    }

    private void ResetIcon()
    {
        if (SelectedItem is null)
        {
            return;
        }
        SelectedItem.CustomIconPath = null;
        ((App)Application.Current).IconService.ClearCache();
        RaiseItemCommandStates();
    }

    private async Task ExportAsync()
    {
        var loc = ((App)Application.Current).LocalizationService;
        var dialog = new SaveFileDialog
        {
            FileName = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                loc.Translate("Format.ExportFilename"),
                DateTime.Now.ToString("yyyyMMdd")),
            Filter = loc.Translate("Filter.BudsDockBundle"),
            DefaultExt = ".budsdock",
            AddExtension = true
        };
        if (dialog.ShowDialog() == true)
        {
            await _settingsService.ExportAsync(dialog.FileName);
        }
    }

    private async Task ImportAsync()
    {
        var loc = ((App)Application.Current).LocalizationService;
        var dialog = new OpenFileDialog
        {
            Filter = loc.Translate("Filter.BudsDockBundle"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var localization = ((App)Application.Current).LocalizationService;
        if (MessageBox.Show(
                localization.Translate("Message.ConfirmImport"),
                localization.Translate("Action.Import"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _settingsService.ImportAsync(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show($"{localization.Translate("Message.ImportFailed")}\n{ex.Message}", localization.Translate("App.Name"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenDataDirectory()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _settingsService.DataDirectory,
            UseShellExecute = true
        });
    }

    private void ResetAppearance()
    {
        var localization = ((App)Application.Current).LocalizationService;
        if (MessageBox.Show(
                localization.Translate("Message.ConfirmResetAppearance"),
                localization.Translate("Action.ResetAppearance"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Settings.BackgroundOpacity = 0.88;
        Settings.IconSize = 54;
        Settings.IconSpacing = 12;
        Settings.DockScale = 1.0;
        Settings.PanelPadding = 12;
        Settings.CornerRadius = 18;
        Settings.ReflectionOpacity = 0.24;
        Settings.GlowIntensity = 0.34;
        Settings.HoverScale = 1.50;
        Settings.AdjacentHoverScale = 1.16;
        Settings.ShowReflection = true;
        Settings.EnableHoverAnimation = true;
    }

    private void RaiseItemCommandStates()
    {
        (RemoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ChooseIconCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ChooseIconCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ResetIconCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (BrowseTargetCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (BrowseWorkingDirectoryCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearWorkingDirectoryCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private AsyncRelayCommand CreateAsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        var command = new AsyncRelayCommand(execute, canExecute);
        command.ExecutionFailed += OnAsyncCommandFailed;
        _asyncCommands.Add(command);
        return command;
    }

    private void OnAsyncCommandFailed(object? sender, Exception ex)
    {
        var localization = ((App)Application.Current).LocalizationService;
        MessageBox.Show(
            $"{localization.Translate("Message.OperationFailed")}\n{ex.Message}",
            localization.Translate("App.Name"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void AttachCollection(AppSettings settings)
    {
        if (_observedItems is not null)
        {
            _observedItems.CollectionChanged -= OnItemsCollectionChanged;
        }
        _observedItems = settings.Items;
        settings.Items.CollectionChanged += OnItemsCollectionChanged;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseItemCommandStates();

    private void OnSelectedItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseItemCommandStates();

    private void OnSettingsServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsService.SaveState) or nameof(SettingsService.LastSavedAt) or nameof(SettingsService.LastSaveError))
        {
            OnPropertyChanged(nameof(SaveState));
            OnPropertyChanged(nameof(SaveStatusText));
        }
    }
}
