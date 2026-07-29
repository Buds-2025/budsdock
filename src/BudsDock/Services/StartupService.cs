using System.Reflection;
using Microsoft.Win32;

namespace BudsDock.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BudsDock";

    public bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, BuildLaunchCommand(), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetEnabled(out bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            enabled = key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            return true;
        }
        catch
        {
            enabled = false;
            return false;
        }
    }

    private static string BuildLaunchCommand()
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
        if (!Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return $"\"{processPath}\"";
        }

        var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "BudsDock";
        var entryAssembly = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
        if (!File.Exists(entryAssembly))
        {
            throw new InvalidOperationException("The managed application path is unavailable.");
        }
        return $"\"{processPath}\" \"{entryAssembly}\"";
    }
}
