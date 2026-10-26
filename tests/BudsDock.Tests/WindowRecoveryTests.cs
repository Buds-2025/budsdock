using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BudsDock;
using BudsDock.Services;
using BudsDock.ViewModels;
using BudsDock.Views;

internal static class WindowRecoveryTests
{
    public static int RunDesktop() => OnStaThread(() =>
    {
        var app = new TestApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var resource in new[] { "Styles.xaml", "Theme.Dark.xaml", "Strings.zh-CN.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/BudsDock;component/Resources/{resource}", UriKind.Relative) });
        var data = Path.Combine(RepositoryRoot(), "artifacts", "desktop-tests", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Environment.SetEnvironmentVariable("BUDSDOCK_DATA_DIR", data);
        var service = new SettingsService();
        Await(service.LoadAsync());
        service.Settings.AutoStart = false;
        service.Settings.HideOnFullscreen = false;
        var dock = new DockWindow(new DockViewModel(service, new LauncherService()), service, new NativeWindowService());
        dock.Show();
        var ordinary = new Window { Title = "BudsDock Desktop Test Control", Width = 320, Height = 160,
            Left = 20, Top = 20, WindowStartupLocation = WindowStartupLocation.Manual };
        ordinary.Show();
        Pump(600);
        var handle = new WindowInteropHelper(dock).Handle;
        var root = (FrameworkElement)dock.FindName("DockRoot");
        var output = new List<string>();
        var failures = 0;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        void Snapshot(string label)
        {
            var point = root.PointToScreen(new Point(root.ActualWidth / 2, root.ActualHeight / 2));
            var hit = WindowFromPoint(new NativePoint((int)point.X, (int)point.Y));
            GetWindowThreadProcessId(hit, out var hitProcess);
            DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int));
            var className = new System.Text.StringBuilder(128);
            GetClassName(hit, className, className.Capacity);
            var foregroundClass = new System.Text.StringBuilder(128);
            GetClassName(GetForegroundWindow(), foregroundClass, foregroundClass.Capacity);
            output.Add($"{label}: visible={IsWindowVisible(handle)} iconic={IsIconic(handle)} cloaked={cloaked} "
                + $"topmost={(GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0} hitClass={className} hitSelf={hitProcess == Environment.ProcessId} "
                + $"foreground={foregroundClass} controlMinimized={IsIconic(new WindowInteropHelper(ordinary).Handle)}");
            Console.WriteLine(output[^1]);
        }
        try
        {
            Snapshot("Before Show Desktop");
            for (var cycle = 0; cycle < 20; cycle++)
            {
                service.Settings.Topmost = cycle % 2 == 0;
                service.Settings.HideOnFullscreen = cycle % 4 >= 2;
                ordinary.WindowState = WindowState.Normal;
                ordinary.Activate();
                Pump(100);
                if (!service.Settings.Topmost)
                    SetWindowPos(handle, GetShellWindow(), 0, 0, 0, 0, 0x13);
                shell.ToggleDesktop();
                try
                {
                    Pump(1000);
                    Snapshot($"Show Desktop {cycle} (topmost={service.Settings.Topmost}, avoidance={service.Settings.HideOnFullscreen})");
                    var topmost = (GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0;
                    if (!IsAboveWindow(handle, GetShellWindow()) || IsIconic(handle)
                        || !IsWindowVisible(handle) || topmost != service.Settings.Topmost
                        || GetForegroundWindow() == handle)
                    {
                        failures++;
                        output.Add($"FAIL  Show Desktop {cycle} covered the Dock");
                    }
                    else output.Add($"PASS  Show Desktop {cycle} kept the Dock visible");
                }
                finally { shell.ToggleDesktop(); Pump(600); }
            }
            service.Settings.Topmost = false;
            service.Settings.HideOnFullscreen = false;
            ordinary.WindowState = WindowState.Normal;
            var ordinaryHandle = new WindowInteropHelper(ordinary).Handle;
            SetWindowPos(ordinaryHandle, IntPtr.Zero, 0, 0, 0, 0, 0x13);
            SetWindowPos(handle, GetShellWindow(), 0, 0, 0, 0, 0x13);
            Pump(800);
            var staysBelowApp = IsAboveWindow(ordinaryHandle, handle) && IsAboveWindow(handle, GetShellWindow());
            output.Add(staysBelowApp ? "PASS  Desktop repair keeps ordinary apps above a non-topmost Dock"
                : "FAIL  Desktop repair changed the ordinary-app stacking order");
            if (!staysBelowApp) failures++;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(data, "results.txt"), output);
            Marshal.FinalReleaseComObject(shell);
            service.StopScheduledSave();
            ordinary.Close();
            app.Shutdown();
            Environment.SetEnvironmentVariable("BUDSDOCK_DATA_DIR", null);
        }
        Console.WriteLine($"Desktop failures: {failures}; Evidence: {data}");
        return failures == 0 ? 0 : 1;
    });

    public static int Run() => OnStaThread(() =>
    {
        var app = new TestApp();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        foreach (var resource in new[] { "Styles.xaml", "Theme.Dark.xaml", "Strings.zh-CN.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/BudsDock;component/Resources/{resource}", UriKind.Relative) });
        var data = Path.Combine(RepositoryRoot(), "artifacts", "window-tests", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("BUDSDOCK_DATA_DIR", data);
        var service = new SettingsService();
        Await(service.LoadAsync());
        service.Settings.AutoStart = false;
        service.Settings.HideOnFullscreen = true;
        var dock = new DockWindow(new DockViewModel(service, new LauncherService()), service, new NativeWindowService());
        dock.Show();
        PumpUntil(() => dock.IsLoaded, 2000);
        var handle = new WindowInteropHelper(dock).Handle;
        var foreground = new Window { Title = "BudsDock Window Recovery Test", Width = 640, Height = 400 };
        foreground.Show();
        foreground.Activate();
        Require(PumpUntil(() => GetForegroundWindow() == new WindowInteropHelper(foreground).Handle, 20000),
            "The window test must receive foreground focus before it can validate app switching");
        var results = new List<string>();
        var failures = 0;
        void Check(string name, Action action)
        {
            try { action(); results.Add($"PASS  {name}"); }
            catch (Exception ex)
            {
                failures++;
                GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
                results.Add($"FAIL  {name}: {ex.Message}; foregroundProcess={processId}; self={Environment.ProcessId}; "
                    + $"fullscreen={new NativeWindowService().IsForegroundFullscreen(service.Settings.MonitorId)}; "
                    + $"visible={dock.IsVisible}; iconic={IsIconic(handle)}");
            }
            Console.WriteLine(results[^1]);
        }
        void Reset(bool fullscreenAvoidance = true)
        {
            service.Settings.HideOnFullscreen = fullscreenAvoidance;
            dock.WindowState = WindowState.Normal;
            dock.Show();
            foreground.Activate();
            Pump(100);
        }
        try
        {
            Check("System minimization recovers without a fullscreen-hide flag", () =>
            {
                Reset();
                dock.WindowState = WindowState.Minimized;
                Require(PumpUntil(() => !IsIconic(handle) && dock.IsVisible, 1200), "Dock remained minimized");
                Require(GetForegroundWindow() == new WindowInteropHelper(foreground).Handle, "Recovery stole focus");
            });
            Check("System minimization recovers with fullscreen avoidance disabled", () =>
            {
                Reset(false);
                dock.WindowState = WindowState.Minimized;
                Require(PumpUntil(() => !IsIconic(handle) && dock.IsVisible, 1200), "Dock remained minimized");
            });
            Check("A hidden native window recovers when WPF still considers it visible", () =>
            {
                Reset(false);
                ShowWindow(handle, 0);
                Require(PumpUntil(() => IsWindowVisible(handle), 1200), "Native window remained hidden");
            });
            Check("Lost native topmost state is repaired", () =>
            {
                Reset();
                service.Settings.Topmost = true;
                SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x13);
                Require(PumpUntil(() => (GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0, 1200), "Topmost state was not repaired");
            });
            Check("Non-topmost preference survives minimization recovery", () =>
            {
                Reset(false);
                service.Settings.Topmost = false;
                dock.WindowState = WindowState.Minimized;
                Require(PumpUntil(() => !IsIconic(handle), 1200), "Dock remained minimized");
                Require((GetWindowLongPtr(handle, -20).ToInt64() & 8) == 0, "Recovery enabled topmost");
                service.Settings.Topmost = true;
            });
            Check("Recovery hotkey path restores a minimized Dock", () =>
            {
                Reset();
                dock.WindowState = WindowState.Minimized;
                dock.RestoreInteraction();
                Require(!IsIconic(handle) && IsWindowVisible(handle), "Manual recovery did not restore the native window");
            });
            Reset();
            using var probe = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { "--fullscreen-probe", Environment.ProcessId.ToString(),
                    new WindowInteropHelper(foreground).Handle.ToInt64().ToString() },
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Could not start fullscreen probe");
            void Command(string command)
            {
                AllowSetForegroundWindow((uint)probe.Id);
                probe.StandardInput.WriteLine(command);
                probe.StandardInput.Flush();
                var reply = probe.StandardOutput.ReadLineAsync();
                Require(PumpUntil(() => reply.IsCompleted, 5000), "Probe command timed out");
                Require(reply.GetAwaiter().GetResult() == command, "Unexpected probe reply");
            }
            try
            {
                Check("Foreground fullscreen hides Dock before the fallback timer", () =>
                {
                    Command("fullscreen");
                    Require(PumpUntil(() => !dock.IsVisible, 400), "Fullscreen entry waited for polling");
                });
                Check("Leaving fullscreen restores Dock without taking focus", () =>
                {
                    Command("normal");
                    Require(PumpUntil(() => dock.IsVisible && !IsIconic(handle), 1200), "Dock failed to recover");
                    Require(GetForegroundWindow() != handle, "Dock stole fullscreen app focus");
                });
                Check("50 repeated fullscreen-to-desktop transitions recover promptly", () =>
                {
                    var recoveryTimes = new List<long>();
                    for (var i = 0; i < 50; i++)
                    {
                        Command("fullscreen");
                        Require(PumpUntil(() => !dock.IsVisible, 1200), $"Entry {i} did not hide");
                        var watch = Stopwatch.StartNew();
                        Command("minimize");
                        foreground.Activate();
                        Require(PumpUntil(() => dock.IsVisible && !IsIconic(handle), 400), $"Exit {i} did not restore promptly");
                        recoveryTimes.Add(watch.ElapsedMilliseconds);
                    }
                    File.WriteAllText(Path.Combine(data, "recovery-times.json"),
                        System.Text.Json.JsonSerializer.Serialize(recoveryTimes));
                });
                Check("A busy UI thread recovers after pending foreground events are delivered", () =>
                {
                    Command("fullscreen");
                    Require(PumpUntil(() => !dock.IsVisible, 1200), "Dock did not hide");
                    // Queue the exit while the Dock dispatcher is unable to process messages.
                    AllowSetForegroundWindow((uint)probe.Id);
                    probe.StandardInput.WriteLine("minimize");
                    probe.StandardInput.Flush();
                    var reply = probe.StandardOutput.ReadLineAsync();
                    Thread.Sleep(1800);
                    Require(PumpUntil(() => reply.IsCompleted, 2000) && reply.Result == "minimize", "Probe did not minimize");
                    foreground.Activate();
                    Require(PumpUntil(() => dock.IsVisible && !IsIconic(handle), 600), "Recovery was lost under load");
                });
                Check("Minimization during fullscreen stays hidden until fullscreen exits", () =>
                {
                    Command("fullscreen");
                    Require(PumpUntil(() => !dock.IsVisible, 1200), "Dock did not hide");
                    dock.WindowState = WindowState.Minimized;
                    Pump(700);
                    Require(!dock.IsVisible, "Dock appeared over fullscreen");
                    Command("minimize");
                    foreground.Activate();
                    Require(PumpUntil(() => dock.IsVisible && !IsIconic(handle), 1200), "Dock stayed minimized after fullscreen");
                });
                Check("Disabling fullscreen avoidance restores a hidden Dock immediately", () =>
                {
                    Command("fullscreen");
                    Require(PumpUntil(() => !dock.IsVisible, 1200), "Dock did not hide");
                    service.Settings.HideOnFullscreen = false;
                    Require(PumpUntil(() => dock.IsVisible && !IsIconic(handle), 400), "Setting change did not restore Dock");
                    Command("minimize");
                });
            }
            finally
            {
                if (!probe.HasExited)
                {
                    try { Command("exit"); }
                    finally { if (!probe.WaitForExit(3000)) probe.Kill(); }
                }
            }
        }
        finally
        {
            File.WriteAllLines(Path.Combine(data, "results.txt"), results);
            service.StopScheduledSave();
            foreground.Close();
            app.Shutdown();
            Environment.SetEnvironmentVariable("BUDSDOCK_DATA_DIR", null);
        }
        Console.WriteLine($"{results.Count - failures}/{results.Count} window tests passed. Evidence: {data}");
        return failures == 0 ? 0 : 1;
    });

    public static int RunProbe() => OnStaThread(() =>
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Title = "BudsDock Fullscreen Test Probe", WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Width = 640, Height = 400 };
        _ = Task.Run(async () =>
        {
            while (await Console.In.ReadLineAsync() is { } command)
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    if (command == "exit") { Console.WriteLine(command); app.Shutdown(); return; }
                    if (command == "fullscreen")
                    {
                        window.WindowState = WindowState.Normal;
                        window.Topmost = true;
                        window.Left = 0;
                        window.Top = 0;
                        window.Width = SystemParameters.PrimaryScreenWidth;
                        window.Height = SystemParameters.PrimaryScreenHeight;
                        window.Show();
                        window.Activate();
                        if (int.TryParse(Environment.GetCommandLineArgs().ElementAtOrDefault(2), out var parentId))
                            AllowSetForegroundWindow((uint)parentId);
                    }
                    else if (command == "normal")
                    {
                        window.Topmost = false;
                        window.WindowState = WindowState.Normal;
                        window.Width = 640;
                        window.Height = 400;
                        window.Activate();
                    }
                    else if (command == "minimize")
                    {
                        // Return to the test's ordinary window, since minimizing
                        // alone may expose an unrelated fullscreen app behind it.
                        if (long.TryParse(Environment.GetCommandLineArgs().ElementAtOrDefault(3), out var parentWindow))
                            SetForegroundWindow(new IntPtr(parentWindow));
                        window.WindowState = WindowState.Minimized;
                    }
                    Console.WriteLine(command);
                });
                if (command == "exit") break;
            }
            if (!app.Dispatcher.HasShutdownStarted) _ = app.Dispatcher.BeginInvoke(app.Shutdown);
        });
        app.Run();
        return 0;
    });

    private static int OnStaThread(Func<int> action)
    {
        var result = 1;
        var thread = new Thread(() => { try { result = action(); } catch (Exception ex) { Console.Error.WriteLine(ex); } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static void Await(Task task)
    {
        Require(PumpUntil(() => task.IsCompleted, 10000), "Task timed out");
        task.GetAwaiter().GetResult();
    }

    private static bool PumpUntil(Func<bool> condition, int timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeout) Pump(10);
        return condition();
    }

    private static bool IsAboveWindow(IntPtr window, IntPtr other)
    {
        var previous = GetWindow(other, 3);
        for (var count = 0; previous != IntPtr.Zero && count < 512; count++)
        {
            if (previous == window) return true;
            previous = GetWindow(previous, 3);
        }
        return false;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BudsDock.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found");
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);

    private sealed class TestApp : App
    {
        protected override void OnStartup(StartupEventArgs e) { }
    }
}
