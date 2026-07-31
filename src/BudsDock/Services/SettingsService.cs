using System.Collections.Specialized;
using System.ComponentModel;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using BudsDock.Models;

namespace BudsDock.Services;

public sealed class SettingsService : INotifyPropertyChanged
{
    public const int CurrentSchemaVersion = 2;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly DispatcherTimer _saveTimer;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly HashSet<DockItem> _trackedItems = [];
    private SaveState _saveState;
    private DateTimeOffset? _lastSavedAt;
    private string? _lastSaveError;
    private long _changeVersion;
    private bool _automaticSavingBlocked;

    public SettingsService()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("BUDSDOCK_DATA_DIR");
        DataDirectory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BudsDock")
            : Path.GetFullPath(overrideDirectory);
        IconsDirectory = Path.Combine(DataDirectory, "icons");
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        Directory.CreateDirectory(IconsDirectory);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            try
            {
                await SaveAsync();
            }
            catch
            {
                // SaveAsync publishes the failure state. Scheduled saves must
                // not escape through an async-void DispatcherTimer handler.
            }
        };
    }

    public string DataDirectory { get; }
    public string IconsDirectory { get; }
    public string SettingsPath { get; }
    public AppSettings Settings { get; private set; } = new();
    public bool IsFirstRun { get; private set; }
    public SaveState SaveState
    {
        get => _saveState;
        private set
        {
            if (_saveState == value)
            {
                return;
            }
            _saveState = value;
            RaiseServicePropertyChanged(nameof(SaveState));
        }
    }
    public DateTimeOffset? LastSavedAt
    {
        get => _lastSavedAt;
        private set
        {
            _lastSavedAt = value;
            RaiseServicePropertyChanged(nameof(LastSavedAt));
        }
    }
    public string? LastSaveError
    {
        get => _lastSaveError;
        private set
        {
            _lastSaveError = value;
            RaiseServicePropertyChanged(nameof(LastSaveError));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PropertyChangedEventArgs>? SettingChanged;
    public event EventHandler? SettingsReplaced;

    public async Task<AppSettings> LoadAsync()
    {
        var canPersistRecoveredSettings = true;
        IsFirstRun = !File.Exists(SettingsPath);
        if (!IsFirstRun)
        {
            try
            {
                var json = await File.ReadAllTextAsync(SettingsPath);
                json = PrepareJsonForCurrentSchema(json, SettingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                try
                {
                    File.Copy(SettingsPath, CreateUniqueRecoveryPath(), overwrite: false);
                }
                catch (Exception backupException)
                {
                    canPersistRecoveredSettings = false;
                    _automaticSavingBlocked = true;
                    LastSaveError = $"The damaged settings file could not be preserved: {backupException.Message}";
                }
                Settings = new AppSettings();
                IsFirstRun = true;
            }
        }

        Normalize(Settings);
        if (Settings.Items.Count == 0 && IsFirstRun)
        {
            foreach (var item in DefaultDockItems.Create())
            {
                Settings.Items.Add(item);
            }
        }

        Attach(Settings);
        if (canPersistRecoveredSettings)
        {
            await SaveAsync();
        }
        else
        {
            SaveState = SaveState.Failed;
        }
        return Settings;
    }

    public void ScheduleSave()
    {
        Interlocked.Increment(ref _changeVersion);
        if (_automaticSavingBlocked)
        {
            SaveState = SaveState.Failed;
            return;
        }
        SaveState = SaveState.Idle;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void StopScheduledSave() => _saveTimer.Stop();

    public async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            await SaveCoreAsync();
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task SaveCoreAsync()
    {
        if (_automaticSavingBlocked)
        {
            SaveState = SaveState.Failed;
            throw new IOException(LastSaveError ?? "Automatic saving is blocked because the damaged settings file could not be preserved.");
        }
        var savingVersion = Volatile.Read(ref _changeVersion);
        try
        {
            SaveState = SaveState.Saving;
            LastSaveError = null;
            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(Settings, _jsonOptions);
            var temporaryPath = SettingsPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
            LastSavedAt = DateTimeOffset.Now;
            SaveState = savingVersion == Volatile.Read(ref _changeVersion)
                ? SaveState.Saved
                : SaveState.Idle;
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
            SaveState = SaveState.Failed;
            throw;
        }
    }

    public async Task<string> ImportIconAsync(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!IsSupportedIconExtension(extension))
        {
            throw new NotSupportedException("Only PNG, JPG, BMP, GIF and ICO icon files are supported in this beta.");
        }

        Directory.CreateDirectory(IconsDirectory);
        var baseName = string.Concat(Path.GetFileNameWithoutExtension(sourcePath)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var destination = Path.Combine(IconsDirectory, $"{baseName}-{Guid.NewGuid():N}{extension}");
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target);
        return destination;
    }

    public async Task ExportAsync(string destinationPath)
    {
        var temporaryPath = destinationPath + ".tmp";
        try
        {
            if (Path.GetExtension(destinationPath).Equals(".budsdock", StringComparison.OrdinalIgnoreCase))
            {
                await ExportBundleCoreAsync(temporaryPath);
            }
            else
            {
                var plainJson = JsonSerializer.Serialize(Settings, _jsonOptions);
                await File.WriteAllTextAsync(temporaryPath, plainJson);
            }
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task ExportBundleCoreAsync(string destinationPath)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(Settings, _jsonOptions))?.AsObject()
            ?? throw new JsonException("Unable to serialize settings.");
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);

        if (root["Items"] is JsonArray items)
        {
            foreach (var itemNode in items.OfType<JsonObject>())
            {
                var iconPath = itemNode["CustomIconPath"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
                {
                    itemNode["CustomIconPath"] = null;
                    continue;
                }

                var extension = Path.GetExtension(iconPath).ToLowerInvariant();
                if (!IsSupportedIconExtension(extension))
                {
                    itemNode["CustomIconPath"] = null;
                    continue;
                }

                var entryName = $"icons/{Guid.NewGuid():N}{extension}";
                var iconEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                await using var iconEntryStream = iconEntry.Open();
                await using var iconSource = new FileStream(iconPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await iconSource.CopyToAsync(iconEntryStream);
                itemNode["CustomIconPath"] = entryName;
            }
        }

        var settingsEntry = archive.CreateEntry("settings.json", CompressionLevel.Optimal);
        await using var settingsStream = settingsEntry.Open();
        await using var writer = new StreamWriter(settingsStream);
        await writer.WriteAsync(root.ToJsonString(_jsonOptions));
    }

    public async Task ImportAsync(string sourcePath)
    {
        var importedIconPaths = new List<string>();
        string json;
        if (Path.GetExtension(sourcePath).Equals(".budsdock", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(sourcePath);
            var settingsEntry = archive.GetEntry("settings.json")
                ?? throw new JsonException("The BudsDock bundle does not contain settings.json.");
            await using (var entryStream = settingsEntry.Open())
            using (var reader = new StreamReader(entryStream))
            {
                json = await reader.ReadToEndAsync();
            }

            var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("The bundled settings are invalid.");
            if (root["Items"] is JsonArray items)
            {
                foreach (var itemNode in items.OfType<JsonObject>())
                {
                    var relativeIconPath = itemNode["CustomIconPath"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(relativeIconPath))
                    {
                        continue;
                    }

                    var normalizedEntryName = relativeIconPath.Replace('\\', '/');
                    var iconEntry = archive.GetEntry(normalizedEntryName);
                    var extension = Path.GetExtension(normalizedEntryName).ToLowerInvariant();
                    if (iconEntry is null || !normalizedEntryName.StartsWith("icons/", StringComparison.OrdinalIgnoreCase) || !IsSupportedIconExtension(extension))
                    {
                        itemNode["CustomIconPath"] = null;
                        continue;
                    }

                    var destination = Path.Combine(IconsDirectory, $"imported-{Guid.NewGuid():N}{extension}");
                    await using var iconSource = iconEntry.Open();
                    await using var iconTarget = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await iconSource.CopyToAsync(iconTarget);
                    importedIconPaths.Add(destination);
                    itemNode["CustomIconPath"] = destination;
                }
            }
            json = root.ToJsonString(_jsonOptions);
        }
        else
        {
            json = await File.ReadAllTextAsync(sourcePath);
        }

        AppSettings imported;
        try
        {
            json = PrepareJsonForCurrentSchema(json, sourcePath);
            imported = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                ?? throw new JsonException("The settings file is empty or invalid.");
            Normalize(imported);
        }
        catch
        {
            DeleteCreatedIconCopies(importedIconPaths);
            throw;
        }

        var previous = Settings;
        imported.AutoStart = previous.AutoStart;
        imported.IsClickThrough = false;
        var settingsReplaced = false;
        _saveTimer.Stop();
        await _saveGate.WaitAsync();
        try
        {
            await SaveCoreAsync();
            BackupCurrentSettings();
            Detach(previous);
            Settings = imported;
            Attach(Settings);
            settingsReplaced = true;
            await SaveCoreAsync();
        }
        catch
        {
            if (settingsReplaced)
            {
                Detach(Settings);
                Settings = previous;
                Attach(Settings);
            }
            DeleteCreatedIconCopies(importedIconPaths);
            throw;
        }
        finally
        {
            _saveGate.Release();
        }

        NotifySettingsReplaced();
    }

    private void BackupCurrentSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        File.Copy(SettingsPath, CreateUniqueRecoveryPath(), overwrite: false);
    }

    private string CreateUniqueRecoveryPath()
        => Path.Combine(DataDirectory, $"settings.recovery-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");

    private static void DeleteCreatedIconCopies(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Cleanup is best-effort; the primary import error remains actionable.
            }
        }
    }

    private static bool IsSupportedIconExtension(string extension)
        => extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico";

    private string PrepareJsonForCurrentSchema(string json, string sourcePath)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("The settings file is invalid.");
        var versionNode = root["SchemaVersion"];
        var version = versionNode is null
            ? 1
            : versionNode is JsonValue value && value.TryGetValue<int>(out var parsedVersion)
                ? parsedVersion
                : throw new JsonException("SchemaVersion must be an integer.");
        if (version > CurrentSchemaVersion)
        {
            var sourceExtension = Path.GetExtension(sourcePath);
            var safeExtension = string.IsNullOrWhiteSpace(sourceExtension) ? ".json" : sourceExtension;
            var backupPath = Path.Combine(
                DataDirectory,
                $"settings.unsupported-v{version}-{DateTime.Now:yyyyMMdd-HHmmss}{safeExtension}");
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, backupPath, overwrite: false);
            }
            throw new InvalidDataException($"Settings schema {version} is newer than supported schema {CurrentSchemaVersion}.");
        }

        if (version < CurrentSchemaVersion && File.Exists(sourcePath))
        {
            var backupPath = Path.Combine(
                DataDirectory,
                $"settings.pre-migration-v{version}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
            try
            {
                File.Copy(sourcePath, backupPath, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException(
                    $"Settings migration from schema {version} was stopped because the original file could not be backed up.",
                    ex);
            }
        }

        while (version < CurrentSchemaVersion)
        {
            root = MigrateOneVersion(root, version);
            version++;
        }

        root["SchemaVersion"] = CurrentSchemaVersion;
        return root.ToJsonString(_jsonOptions);
    }

    private static JsonObject MigrateOneVersion(JsonObject root, int sourceVersion)
    {
        return sourceVersion switch
        {
            1 => MigrateFromVersion1(root),
            _ => throw new InvalidDataException($"No migration is available from settings schema {sourceVersion}.")
        };
    }

    private static JsonObject MigrateFromVersion1(JsonObject root)
    {
        if (root["HoverScale"] is JsonValue hoverValue
            && hoverValue.TryGetValue<double>(out var hoverScale)
            && Math.Abs(hoverScale - 1.12) < 0.0001)
        {
            root["HoverScale"] = 1.50;
        }

        if (root["AdjacentHoverScale"] is JsonValue adjacentValue
            && adjacentValue.TryGetValue<double>(out var adjacentScale)
            && Math.Abs(adjacentScale - 1.04) < 0.0001)
        {
            root["AdjacentHoverScale"] = 1.16;
        }

        return root;
    }

    private static void Normalize(AppSettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.ThemeMode = settings.ThemeMode is ThemeMode.Dark or ThemeMode.Light
            ? settings.ThemeMode
            : ThemeMode.Dark;
        settings.Language = Enum.IsDefined(settings.Language) ? settings.Language : AppLanguage.System;
        settings.Orientation = Enum.IsDefined(settings.Orientation) ? settings.Orientation : DockOrientation.Horizontal;
        settings.Placement = Enum.IsDefined(settings.Placement) ? settings.Placement : DockPlacement.BottomCenter;
        settings.DefaultIconVisualMode = Enum.IsDefined(settings.DefaultIconVisualMode)
            ? settings.DefaultIconVisualMode
            : IconVisualMode.Original;
        settings.Items ??= [];

        for (var index = settings.Items.Count - 1; index >= 0; index--)
        {
            var item = settings.Items[index];
            if (item is null)
            {
                settings.Items.RemoveAt(index);
                continue;
            }
            item.VisualMode = Enum.IsDefined(item.VisualMode) ? item.VisualMode : IconVisualMode.Original;
            item.Kind = Enum.IsDefined(item.Kind) ? item.Kind : LaunchTargetKind.Executable;
        }
    }

    private void NotifySettingsReplaced()
    {
        var handlers = SettingsReplaced;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogSubscriberFailure("SettingsReplaced", ex);
            }
        }
    }

    private void Attach(AppSettings settings)
    {
        settings.PropertyChanged += SettingsOnPropertyChanged;
        settings.Items.CollectionChanged += ItemsOnCollectionChanged;
        foreach (var item in settings.Items)
        {
            TrackItem(item);
        }
    }

    private void Detach(AppSettings settings)
    {
        settings.PropertyChanged -= SettingsOnPropertyChanged;
        settings.Items.CollectionChanged -= ItemsOnCollectionChanged;
        foreach (var item in _trackedItems.ToArray())
        {
            UntrackItem(item);
        }
    }

    private void ItemsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DockItem item in e.OldItems)
            {
                UntrackItem(item);
            }
        }
        if (e.NewItems is not null)
        {
            foreach (DockItem item in e.NewItems)
            {
                TrackItem(item);
            }
        }
        ScheduleSave();
    }

    private void TrackItem(DockItem item)
    {
        if (_trackedItems.Add(item))
        {
            item.PropertyChanged += ItemOnPropertyChanged;
        }
    }

    private void UntrackItem(DockItem item)
    {
        if (_trackedItems.Remove(item))
        {
            item.PropertyChanged -= ItemOnPropertyChanged;
        }
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ScheduleSave();
        NotifySettingChanged(e);
    }

    private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DockItem.HoverScale)
            or nameof(DockItem.HoverOffsetX)
            or nameof(DockItem.HoverOffsetY)
            or nameof(DockItem.IsHovered))
        {
            return;
        }
        ScheduleSave();
    }

    private void NotifySettingChanged(PropertyChangedEventArgs e)
    {
        var handlers = SettingChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<PropertyChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, e);
            }
            catch (Exception ex)
            {
                LogSubscriberFailure($"SettingChanged.{e.PropertyName}", ex);
            }
        }
    }

    private void RaiseServicePropertyChanged(string propertyName)
    {
        var handlers = PropertyChanged;
        if (handlers is null)
        {
            return;
        }

        var args = new PropertyChangedEventArgs(propertyName);
        foreach (PropertyChangedEventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                LogSubscriberFailure($"PropertyChanged.{propertyName}", ex);
            }
        }
    }

    private void LogSubscriberFailure(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(
                Path.Combine(DataDirectory, "service.log"),
                $"[{DateTime.Now:O}] {source} subscriber failed: {exception}\n\n");
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"{source} subscriber failed: {exception}");
        }
    }
}
