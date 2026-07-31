using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BudsDock.Converters;
using BudsDock.Models;
using BudsDock.Services;
using BudsDock.ViewModels;

var tests = new (string Name, Action Run)[]
{
    ("Top and bottom placements are horizontal", TestPlacementOrientation),
    ("Left and right placements are vertical", TestVerticalOrientation),
    ("Bottom-center placement respects work area", TestBottomCenter),
    ("Bottom-center placement stays 2.5 taskbar heights above screen bottom", TestBottomTaskbarOffset),
    ("Free position clamps into primary work area", TestClamp),
    ("Appearance values are bounded", TestSettingsBounds),
    ("Required built-in items exist", TestDefaultItems),
    ("Portable bundle includes imported icons", TestPortableBundle),
    ("Screen-center placement is exact", TestScreenCenter),
    ("Chinese and English resources have matching keys", TestLocalizationResourceParity),
    ("Theme tokens preserve icon contrast", TestThemeContrastTokens),
    ("Theme selection exposes only dark and light modes", TestThemeChoices),
    ("All appearance settings and defaults are bounded", TestAllAppearanceBounds),
    ("Hover scales distinguish current and adjacent items", TestHoverScales),
    ("Icon glow color follows saturated icon pixels", TestIconGlowColor),
    ("Glow safety margin covers maximum icon and Dock scaling", TestGlowSafeMargin),
    ("Disabled hover motion restores every item to 100 percent", TestDisabledHoverScale),
    ("Async commands reject duplicate execution", TestAsyncCommandSingleExecution),
    ("Null item collections are normalized", TestNullItemsNormalization),
    ("Schema 1 hover defaults migrate without overwriting custom values", TestHoverDefaultsMigration),
    ("Invalid enum values are normalized", TestInvalidEnumNormalization),
    ("Future settings schemas are preserved and rejected", TestFutureSchemaGate),
    ("Corrupt settings recover without overwriting the source copy", TestCorruptSettingsRecovery),
    ("Unpreserved corrupt settings remain read-only", TestRecoveryReadOnlyProtection),
    ("Save state reports successful and failed writes", TestSaveStates),
    ("Debounced changes are persisted", TestDebouncedSave),
    ("Changes made during saving remain marked pending", TestSaveGeneration),
    ("Setting subscriber failures cannot block persistence", TestSettingSubscriberIsolation),
    ("Import subscriber failures do not undo committed data", TestImportSubscriberIsolation),
    ("Failed imports restore the previous in-memory settings", TestImportTransactionRollback),
    ("Settings XAML contains interaction safety invariants", TestSettingsXamlInvariants),
    ("Dock and tray visual refresh invariants are present", TestDockVisualInvariants),
    ("Every exit entry uses the asynchronous application exit path", TestUnifiedExitPaths)
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL  {name}: {ex.Message}");
        Console.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"\n{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static void TestPlacementOrientation()
{
    Equal(DockOrientation.Horizontal, DockPositionService.OrientationFor(DockPlacement.TopCenter, DockOrientation.Vertical));
    Equal(DockOrientation.Horizontal, DockPositionService.OrientationFor(DockPlacement.BottomCenter, DockOrientation.Vertical));
}

static void TestVerticalOrientation()
{
    Equal(DockOrientation.Vertical, DockPositionService.OrientationFor(DockPlacement.LeftCenter, DockOrientation.Horizontal));
    Equal(DockOrientation.Vertical, DockPositionService.OrientationFor(DockPlacement.RightCenter, DockOrientation.Horizontal));
}

static void TestBottomCenter()
{
    var workArea = new Rect(0, 0, 1920, 1040);
    var result = DockPositionService.Calculate(DockPlacement.BottomCenter, new Size(600, 100), workArea, 24);
    Equal(660d, result.X);
    Equal(916d, result.Y);
}

static void TestBottomTaskbarOffset()
{
    var workArea = new Rect(0, 0, 1920, 1040);
    const double taskbarHeight = 40;
    var result = DockPositionService.Calculate(
        DockPlacement.BottomCenter,
        new Size(600, 100),
        workArea,
        DockPositionService.EdgeMargin,
        taskbarHeight);
    Equal(660d, result.X);
    Equal(880d, result.Y);
    Equal(taskbarHeight * 2.5, 1080d - (result.Y + 100d));
}

static void TestClamp()
{
    var workArea = new Rect(0, 0, 1920, 1040);
    var result = DockPositionService.Clamp(new Point(-400, 1500), new Size(600, 100), workArea);
    Equal(0d, result.X);
    Equal(940d, result.Y);
}

static void TestSettingsBounds()
{
    var settings = new AppSettings
    {
        BackgroundOpacity = 2,
        IconSize = 4,
        IconSpacing = 99,
        DockScale = 0.1
    };
    Equal(1d, settings.BackgroundOpacity);
    Equal(28d, settings.IconSize);
    Equal(48d, settings.IconSpacing);
    Equal(0.65d, settings.DockScale);
}

static void TestAllAppearanceBounds()
{
    var defaults = new AppSettings();
    Equal(IconVisualMode.Original, defaults.DefaultIconVisualMode);
    Equal(1.50d, defaults.HoverScale);
    Equal(1.16d, defaults.AdjacentHoverScale);

    var settings = new AppSettings
    {
        PanelPadding = -1,
        CornerRadius = 90,
        ReflectionOpacity = 4,
        GlowIntensity = -2,
        HoverScale = 0.5,
        AdjacentHoverScale = 8
    };
    Equal(4d, settings.PanelPadding);
    Equal(36d, settings.CornerRadius);
    Equal(0.7d, settings.ReflectionOpacity);
    Equal(0d, settings.GlowIntensity);
    Equal(1d, settings.HoverScale);
    Equal(1.3d, settings.AdjacentHoverScale);
}

static void TestHoverScales()
{
    var settings = new AppSettings { HoverScale = 1.25, AdjacentHoverScale = 1.08 };
    Equal(1.028d, DockViewModel.CalculateHoverScale(0, 2, settings, true));
    Equal(1.08d, DockViewModel.CalculateHoverScale(1, 2, settings, true));
    Equal(1.25d, DockViewModel.CalculateHoverScale(2, 2, settings, true));
    Equal(1.08d, DockViewModel.CalculateHoverScale(3, 2, settings, true));
    Equal(1.028d, DockViewModel.CalculateHoverScale(4, 2, settings, true));

    var offsetSettings = new AppSettings
    {
        IconSize = 54,
        IconSpacing = 12,
        HoverScale = 1.50,
        AdjacentHoverScale = 1.16
    };
    Equal(-5.82d, Math.Round(DockViewModel.CalculateHoverOffset(1, 2, offsetSettings, true), 3));
    Equal(5.82d, Math.Round(DockViewModel.CalculateHoverOffset(3, 2, offsetSettings, true), 3));
    Equal(2.037d, Math.Round(DockViewModel.CalculateHoverOffset(4, 2, offsetSettings, true), 3));
}

static void TestIconGlowColor()
{
    var pixels = new byte[]
    {
        20, 30, 240, 255,
        30, 40, 230, 255,
        10, 20, 250, 255,
        255, 255, 255, 255
    };
    var fallback = Color.FromRgb(80, 100, 255);
    var result = IconService.CalculateGlowColor(pixels, 2, 2, 8, fallback);
    if (result.R <= result.G || result.R <= result.B || result.R < 168)
    {
        throw new InvalidOperationException($"Expected a bright red-derived glow, got {result}.");
    }
}

static void TestGlowSafeMargin()
{
    var converter = new IconSizeToGlowSafeMarginConverter();
    var margin = (Thickness)converter.Convert(
        new object[] { 112d, 1.65d, 1.8d },
        typeof(Thickness),
        null!,
        System.Globalization.CultureInfo.InvariantCulture);
    var worstCaseGlowExtent = 52d * 1.65d * 1.8d;
    if (margin.Left < worstCaseGlowExtent || margin.Top < worstCaseGlowExtent)
    {
        throw new InvalidOperationException(
            $"Glow safety margin {margin.Left:F2} does not cover the {worstCaseGlowExtent:F2} DIP maximum extent.");
    }
}

static void TestDisabledHoverScale()
{
    var settings = new AppSettings { HoverScale = 1.3, AdjacentHoverScale = 1.1 };
    for (var index = 0; index < 5; index++)
    {
        Equal(1d, DockViewModel.CalculateHoverScale(index, 2, settings, false));
        Equal(1d, DockViewModel.CalculateHoverScale(index, -1, settings, true));
    }
}

static void TestAsyncCommandSingleExecution()
{
    var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    var command = new AsyncRelayCommand(async () =>
    {
        Interlocked.Increment(ref calls);
        await release.Task;
    });

    using var start = new ManualResetEventSlim(false);
    var first = Task.Run(async () =>
    {
        start.Wait();
        await command.ExecuteAsync();
    });
    var second = Task.Run(async () =>
    {
        start.Wait();
        await command.ExecuteAsync();
    });
    start.Set();
    if (!SpinWait.SpinUntil(() => Volatile.Read(ref calls) > 0, TimeSpan.FromSeconds(2)))
    {
        throw new TimeoutException("The asynchronous command did not start.");
    }
    Equal(1, calls);
    Equal(true, command.IsRunning);
    release.SetResult(true);
    Task.WhenAll(first, second).GetAwaiter().GetResult();
    Equal(false, command.IsRunning);
}

static void TestNullItemsNormalization()
{
    WithDataDirectory("null-items", service =>
    {
        File.WriteAllText(service.SettingsPath, """{"SchemaVersion":1,"Items":null}""");
        var settings = service.LoadAsync().GetAwaiter().GetResult();
        Equal(0, settings.Items.Count);
    });
}

static void TestHoverDefaultsMigration()
{
    WithDataDirectory("hover-default-migration", service =>
    {
        File.WriteAllText(
            service.SettingsPath,
            """{"SchemaVersion":1,"HoverScale":1.12,"AdjacentHoverScale":1.04,"Items":[]}""");
        var migrated = service.LoadAsync().GetAwaiter().GetResult();
        Equal(SettingsService.CurrentSchemaVersion, migrated.SchemaVersion);
        Equal(1.50d, migrated.HoverScale);
        Equal(1.16d, migrated.AdjacentHoverScale);
        var backups = Directory.GetFiles(service.DataDirectory, "settings.pre-migration-v1-*.json");
        Equal(1, backups.Length);
        Contains("\"SchemaVersion\":1", File.ReadAllText(backups[0]));
    });

    WithDataDirectory("hover-custom-migration", service =>
    {
        File.WriteAllText(
            service.SettingsPath,
            """{"SchemaVersion":1,"HoverScale":1.25,"AdjacentHoverScale":1.08,"Items":[]}""");
        var migrated = service.LoadAsync().GetAwaiter().GetResult();
        Equal(1.25d, migrated.HoverScale);
        Equal(1.08d, migrated.AdjacentHoverScale);
    });
}

static void TestInvalidEnumNormalization()
{
    WithDataDirectory("invalid-enums", service =>
    {
        File.WriteAllText(service.SettingsPath,
            """{"SchemaVersion":1,"ThemeMode":999,"Language":999,"Orientation":999,"Placement":999,"DefaultIconVisualMode":999,"Items":[]}""");
        var settings = service.LoadAsync().GetAwaiter().GetResult();
        Equal(BudsDock.Models.ThemeMode.Dark, settings.ThemeMode);
        Equal(AppLanguage.System, settings.Language);
        Equal(DockOrientation.Horizontal, settings.Orientation);
        Equal(DockPlacement.BottomCenter, settings.Placement);
        Equal(IconVisualMode.Original, settings.DefaultIconVisualMode);
    });
}

static void TestFutureSchemaGate()
{
    WithDataDirectory("future-schema", service =>
    {
        const string futureJson = """{"SchemaVersion":999,"UnknownFutureField":{"keep":true},"Items":[]}""";
        File.WriteAllText(service.SettingsPath, futureJson);
        try
        {
            service.LoadAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException("A future schema was accepted.");
        }
        catch (InvalidDataException)
        {
            // Expected: the application must stop before saving over unknown fields.
        }

        Equal(futureJson, File.ReadAllText(service.SettingsPath));
        if (Directory.GetFiles(service.DataDirectory, "settings.unsupported-v999-*.json").Length != 1)
        {
            throw new InvalidOperationException("The future-version source was not backed up exactly once.");
        }
    });
}

static void TestCorruptSettingsRecovery()
{
    WithDataDirectory("corrupt-settings", service =>
    {
        File.WriteAllText(service.SettingsPath, "{ definitely not json");
        var settings = service.LoadAsync().GetAwaiter().GetResult();
        Equal(5, settings.Items.Count);
        if (Directory.GetFiles(service.DataDirectory, "settings.recovery-*.json").Length != 1)
        {
            throw new InvalidOperationException("The corrupt source was not preserved as a recovery copy.");
        }
    });
}

static void TestRecoveryReadOnlyProtection()
{
    WithDataDirectory("recovery-read-only", service =>
    {
        const string corruptJson = "{ locked and damaged";
        File.WriteAllText(service.SettingsPath, corruptJson);
        using (var lockedSource = new FileStream(service.SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            service.LoadAsync().GetAwaiter().GetResult();
            Equal(SaveState.Failed, service.SaveState);
            service.Settings.IconSize = 88;
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            Equal(SaveState.Failed, service.SaveState);
        }
        Equal(corruptJson, File.ReadAllText(service.SettingsPath));
    });
}

static void TestSaveStates()
{
    WithDataDirectory("save-state", service =>
    {
        service.LoadAsync().GetAwaiter().GetResult();
        Equal(SaveState.Saved, service.SaveState);
        if (service.LastSavedAt is null)
        {
            throw new InvalidOperationException("A successful save did not publish its completion time.");
        }

        Directory.CreateDirectory(service.SettingsPath + ".tmp");
        try
        {
            service.SaveAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException("A blocked temporary path did not fail saving.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Equal(SaveState.Failed, service.SaveState);
            if (string.IsNullOrWhiteSpace(service.LastSaveError))
            {
                throw new InvalidOperationException("A failed save did not publish an error.");
            }
        }
    });
}

static void TestDebouncedSave()
{
    WithDataDirectory("debounced-save", service =>
    {
        service.LoadAsync().GetAwaiter().GetResult();
        service.Settings.IconSize = 73;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);

        using var document = JsonDocument.Parse(File.ReadAllText(service.SettingsPath));
        Equal(73d, document.RootElement.GetProperty("IconSize").GetDouble());
        Equal(SaveState.Saved, service.SaveState);
    });
}

static void TestSaveGeneration()
{
    WithDataDirectory("save-generation", service =>
    {
        service.LoadAsync().GetAwaiter().GetResult();
        var changedDuringSave = false;
        service.PropertyChanged += (_, e) =>
        {
            if (!changedDuringSave
                && e.PropertyName == nameof(SettingsService.SaveState)
                && service.SaveState == SaveState.Saving)
            {
                changedDuringSave = true;
                service.Settings.IconSize = 79;
            }
        };

        service.SaveAsync().GetAwaiter().GetResult();
        Equal(true, changedDuringSave);
        Equal(SaveState.Idle, service.SaveState);
        PumpDispatcher(TimeSpan.FromMilliseconds(500));
        Equal(SaveState.Saved, service.SaveState);
        using var document = JsonDocument.Parse(File.ReadAllText(service.SettingsPath));
        Equal(79d, document.RootElement.GetProperty("IconSize").GetDouble());
    });
}

static void TestSettingSubscriberIsolation()
{
    WithDataDirectory("setting-subscriber", service =>
    {
        service.LoadAsync().GetAwaiter().GetResult();
        service.SettingChanged += (_, _) => throw new InvalidOperationException("subscriber failure");
        service.Settings.IconSize = 67;
        PumpDispatcher(TimeSpan.FromMilliseconds(500));
        using var document = JsonDocument.Parse(File.ReadAllText(service.SettingsPath));
        Equal(67d, document.RootElement.GetProperty("IconSize").GetDouble());
        Equal(SaveState.Saved, service.SaveState);
        if (!File.Exists(Path.Combine(service.DataDirectory, "service.log")))
        {
            throw new InvalidOperationException("The isolated subscriber failure was not logged.");
        }
    });
}

static void TestImportSubscriberIsolation()
{
    WithDataDirectory("subscriber-isolation", service =>
    {
        service.LoadAsync().GetAwaiter().GetResult();
        var importPath = Path.Combine(service.DataDirectory, "incoming.json");
        File.WriteAllText(importPath, """{"SchemaVersion":1,"IconSize":81,"Items":[]}""");
        var secondSubscriberRan = false;
        service.SettingsReplaced += (_, _) => throw new InvalidOperationException("subscriber failure");
        service.SettingsReplaced += (_, _) => secondSubscriberRan = true;
        service.ImportAsync(importPath).GetAwaiter().GetResult();
        Equal(81d, service.Settings.IconSize);
        Equal(true, secondSubscriberRan);
        using var document = JsonDocument.Parse(File.ReadAllText(service.SettingsPath));
        Equal(81d, document.RootElement.GetProperty("IconSize").GetDouble());
    });
}

static void TestImportTransactionRollback()
{
    WithDataDirectory("import-rollback", service =>
    {
        service.LoadAsync().GetAwaiter().GetResult();
        var previous = service.Settings;
        var previousSize = previous.IconSize;
        var importPath = Path.Combine(service.DataDirectory, "incoming.json");
        File.WriteAllText(importPath, """{"SchemaVersion":1,"IconSize":90,"Items":[]}""");
        Directory.CreateDirectory(service.SettingsPath + ".tmp");
        try
        {
            service.ImportAsync(importPath).GetAwaiter().GetResult();
            throw new InvalidOperationException("Import unexpectedly committed through a blocked save path.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Equal(true, ReferenceEquals(previous, service.Settings));
            Equal(previousSize, service.Settings.IconSize);
        }
    });
}

static void TestSettingsXamlInvariants()
{
    var root = FindRepositoryRoot();
    var window = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Views", "SettingsWindow.xaml"));
    var styles = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Resources", "Styles.xaml"));

    Contains("WindowChrome.IsHitTestVisibleInChrome=\"True\"", window);
    Contains("x:Name=\"MaximizeButton\"", window);
    Contains("Mode=OneWay", window);
    Contains("HorizontalContentAlignment=\"Stretch\"", window);
    DoesNotContain("MaxWidth=\"760\"", window);
    DoesNotContain("Width=\"500\" MaxWidth=\"500\"", window);
    Contains("PART_Popup", styles);
    Contains("Property=\"IsMoveToPointEnabled\" Value=\"True\"", styles);
    Contains("AnimatedScaleBehavior.TargetScale", File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Views", "DockWindow.xaml")));
    Contains("DockContextMenuStyle", styles);
    Contains("IconGlowColorConverter", styles);
    Contains("RenderTransform", File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Behaviors", "AnimatedScaleBehavior.cs")));
    Contains("Icon.ExtractAssociatedIcon", File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Services", "TrayService.cs")));
}

static void TestUnifiedExitPaths()
{
    var root = FindRepositoryRoot();
    var appSource = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "App.xaml.cs"));
    var dockSource = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Views", "DockWindow.xaml.cs"));
    Contains("_trayService.ExitRequested += async (_, _) => await ExitApplicationAsync();", appSource);
    Contains("_dockViewModel.ExitRequested += async (_, _) => await ExitApplicationAsync();", appSource);
    Contains("_viewModel.ExitCommand.Execute(null);", dockSource);
    Contains("SettingsService.StopScheduledSave();", appSource);
    Contains("await SettingsService.SaveAsync();", appSource);
    Contains("_dockWindow.IsEnabled = false;", appSource);
    Contains("_trayService?.SetInteractionEnabled(false);", appSource);
    Contains("SettingsService.SaveState == SaveState.Saved", appSource);
}

static void TestDockVisualInvariants()
{
    var root = FindRepositoryRoot();
    var dockXaml = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Views", "DockWindow.xaml"));
    var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Views", "SettingsWindow.xaml"));
    var traySource = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Services", "TrayService.cs"));
    var dockSource = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Views", "DockWindow.xaml.cs"));
    var iconSource = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Services", "IconService.cs"));
    var styles = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Resources", "Styles.xaml"));

    Contains("<Binding Path=\"VisualMode\"/>", dockXaml);
    Contains("<Binding Path=\"SelectedItem.VisualMode\"/>", settingsXaml);
    Contains("IconSizeToGlowSafeMarginConverter", dockXaml);
    Contains("<Binding Path=\"Settings.HoverScale\"/>", dockXaml);
    Contains("<Binding Path=\"Settings.DockScale\"/>", dockXaml);
    Contains("ShowCheckMargin = true", traySource);
    Contains("_trayIcon.Dispose();", traySource);
    Contains("DispatcherOperationStatus.Pending", dockSource);
    Contains("FindNearestDockItem", dockSource);
    Contains("existing.CloneCurrentValue()", File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Behaviors", "AnimatedScaleBehavior.cs")));
    Contains("AnimatedOpacityBehavior.TargetOpacity", dockXaml);
    Contains("new DrawingImage(group)", iconSource);
    Contains("MenuBackgroundBrush", styles);
    Contains("MenuPalette.ForTheme", traySource);
}

static void TestDefaultItems()
{
    var items = DefaultDockItems.Create();
    Equal(5, items.Count);
    var expected = new[] { "builtin-this-pc", "builtin-control-panel", "builtin-file-explorer", "builtin-edge", "builtin-recycle-bin" };
    Equal(string.Join('|', expected), string.Join('|', items.Select(item => item.Id)));
}

static void TestScreenCenter()
{
    var workArea = new Rect(100, 50, 1600, 900);
    var result = DockPositionService.Calculate(DockPlacement.ScreenCenter, new Size(400, 100), workArea);
    Equal(700d, result.X);
    Equal(450d, result.Y);
}

static void TestLocalizationResourceParity()
{
    var root = FindRepositoryRoot();
    var chinese = ReadResourceKeys(Path.Combine(root, "src", "BudsDock", "Resources", "Strings.zh-CN.xaml"));
    var english = ReadResourceKeys(Path.Combine(root, "src", "BudsDock", "Resources", "Strings.en-US.xaml"));
    Equal(string.Join('|', chinese), string.Join('|', english));
}

static string[] ReadResourceKeys(string path)
{
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    return XDocument.Load(path)
        .Descendants()
        .Select(element => element.Attribute(x + "Key")?.Value)
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .Cast<string>()
        .OrderBy(key => key, StringComparer.Ordinal)
        .ToArray();
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "BudsDock.sln")))
        {
            return directory.FullName;
        }
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate BudsDock.sln from the test output directory.");
}

static void TestThemeContrastTokens()
{
    var root = FindRepositoryRoot();
    var dark = ReadThemeColors(Path.Combine(root, "src", "BudsDock", "Resources", "Theme.Dark.xaml"));
    var light = ReadThemeColors(Path.Combine(root, "src", "BudsDock", "Resources", "Theme.Light.xaml"));

    if (ContrastRatio(light["IconForegroundColor"], light["IconTileColor"]) < 4.5)
    {
        throw new InvalidOperationException("Light theme icon foreground does not meet 4.5:1 contrast against the tile.");
    }
    if (ContrastRatio(dark["IconForegroundColor"], Composite(dark["IconTileColor"], dark["DockSurfaceColor"])) < 4.5)
    {
        throw new InvalidOperationException("Dark theme icon foreground does not meet 4.5:1 contrast against the tile.");
    }
    if (ContrastRatio(light["AccentColor"], light["SurfaceAltColor"]) < 4.5)
    {
        throw new InvalidOperationException("Light theme accent does not meet 4.5:1 contrast against the alternate surface.");
    }
    if (ContrastRatio(light["DangerColor"], light["SurfaceAltColor"]) < 4.5)
    {
        throw new InvalidOperationException("Light theme danger text does not meet 4.5:1 contrast against the alternate surface.");
    }
    if (ContrastRatio(dark["MenuTextColor"], dark["MenuBackgroundColor"]) < 7)
    {
        throw new InvalidOperationException("Dark menu text does not meet 7:1 contrast against its background.");
    }
    if (ContrastRatio(light["MenuTextColor"], light["MenuBackgroundColor"]) < 7)
    {
        throw new InvalidOperationException("Light menu text does not meet 7:1 contrast against its background.");
    }
}

static void TestThemeChoices()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "ViewModels", "SettingsViewModel.cs"));
    var themeService = File.ReadAllText(Path.Combine(root, "src", "BudsDock", "Services", "ThemeService.cs"));
    Contains("ThemeMode.Dark", source);
    Contains("ThemeMode.Light", source);
    DoesNotContain("Enum.GetValues<ThemeMode>()", source);
    DoesNotContain("SystemUsesLightTheme", themeService);
}

static Dictionary<string, Rgba> ReadThemeColors(string path)
{
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    return XDocument.Load(path)
        .Descendants()
        .Where(element => element.Name.LocalName == "Color")
        .Where(element => element.Attribute(x + "Key") is not null)
        .ToDictionary(element => element.Attribute(x + "Key")!.Value, element => ParseColor(element.Value), StringComparer.Ordinal);
}

static Rgba ParseColor(string value)
{
    var hex = value.TrimStart('#');
    return hex.Length switch
    {
        6 => new Rgba(255, Convert.ToByte(hex[0..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)),
        8 => new Rgba(Convert.ToByte(hex[0..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16), Convert.ToByte(hex[6..8], 16)),
        _ => throw new FormatException($"Unsupported color token {value}.")
    };
}

static Rgba Composite(Rgba foreground, Rgba background)
{
    var alpha = foreground.A / 255d;
    return new Rgba(
        255,
        (byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
        (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
        (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
}

static double ContrastRatio(Rgba first, Rgba second)
{
    var firstLuminance = RelativeLuminance(first);
    var secondLuminance = RelativeLuminance(second);
    return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
}

static double RelativeLuminance(Rgba color)
{
    static double Channel(byte value)
    {
        var normalized = value / 255d;
        return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }
    return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
}

static void TestPortableBundle()
{
    var root = Path.Combine(Path.GetTempPath(), $"BudsDock-tests-{Guid.NewGuid():N}");
    var sourceData = Path.Combine(root, "source");
    var targetData = Path.Combine(root, "target");
    Directory.CreateDirectory(root);
    var iconSource = Path.Combine(root, "sample.png");
    File.WriteAllBytes(iconSource, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z4T8AAAAASUVORK5CYII="));

    Environment.SetEnvironmentVariable("BUDSDOCK_DATA_DIR", sourceData);
    var sourceService = new SettingsService();
    sourceService.LoadAsync().GetAwaiter().GetResult();
    var importedIcon = sourceService.ImportIconAsync(iconSource).GetAwaiter().GetResult();
    sourceService.Settings.Items[0].CustomIconPath = importedIcon;
    var bundle = Path.Combine(root, "settings.budsdock");
    sourceService.ExportAsync(bundle).GetAwaiter().GetResult();

    Environment.SetEnvironmentVariable("BUDSDOCK_DATA_DIR", targetData);
    var targetService = new SettingsService();
    targetService.LoadAsync().GetAwaiter().GetResult();
    targetService.ImportAsync(bundle).GetAwaiter().GetResult();
    var restoredIcon = targetService.Settings.Items[0].CustomIconPath;
    if (string.IsNullOrWhiteSpace(restoredIcon) || !File.Exists(restoredIcon) || !restoredIcon.StartsWith(targetData, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("The imported icon was not restored into the target data directory.");
    }

    if (Directory.GetFiles(targetData, "settings.recovery-*.json").Length == 0)
    {
        throw new InvalidOperationException("Import did not create a recovery copy of the previous settings.");
    }

    Environment.SetEnvironmentVariable("BUDSDOCK_DATA_DIR", null);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }
}

static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected source text to contain: {expected}");
    }
}

static void DoesNotContain(string unexpected, string actual)
{
    if (actual.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected source text not to contain: {unexpected}");
    }
}

static void WithDataDirectory(string scenario, Action<SettingsService> action)
{
    var previous = Environment.GetEnvironmentVariable("BUDSDOCK_DATA_DIR");
    var directory = Path.Combine(Path.GetTempPath(), $"BudsDock-tests-{scenario}-{Guid.NewGuid():N}");
    try
    {
        Environment.SetEnvironmentVariable("BUDSDOCK_DATA_DIR", directory);
        action(new SettingsService());
    }
    finally
    {
        Environment.SetEnvironmentVariable("BUDSDOCK_DATA_DIR", previous);
    }
}

static void PumpDispatcher(TimeSpan duration)
{
    var frame = new DispatcherFrame();
    var timer = new DispatcherTimer { Interval = duration };
    timer.Tick += (_, _) =>
    {
        timer.Stop();
        frame.Continue = false;
    };
    timer.Start();
    Dispatcher.PushFrame(frame);
}

readonly record struct Rgba(byte A, byte R, byte G, byte B);
