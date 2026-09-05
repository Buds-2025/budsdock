using System.Diagnostics;
using BudsDock.Models;

namespace BudsDock.Services;

public sealed class LauncherService
{
    public event EventHandler<string>? LaunchFailed;

    public void Launch(DockItem item)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.TargetPath))
            {
                throw new InvalidOperationException("No launch target is configured.");
            }

            if (item.Kind is LaunchTargetKind.Executable or LaunchTargetKind.Shortcut && Path.IsPathRooted(item.TargetPath) && !File.Exists(item.TargetPath))
            {
                throw new FileNotFoundException("The configured application no longer exists.", item.TargetPath);
            }

            if (item.Kind == LaunchTargetKind.Folder && !Directory.Exists(item.TargetPath))
                throw new FileNotFoundException("The configured folder no longer exists.", item.TargetPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = item.TargetPath,
                Arguments = item.Arguments,
                WorkingDirectory = ResolveWorkingDirectory(item),
                UseShellExecute = true,
                Verb = item.RunAsAdministrator ? "runas" : string.Empty
            };
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            LaunchFailed?.Invoke(this, ex.Message);
        }
    }

    private static string ResolveWorkingDirectory(DockItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.WorkingDirectory) && Directory.Exists(item.WorkingDirectory))
        {
            return item.WorkingDirectory;
        }
        if (Path.IsPathRooted(item.TargetPath))
        {
            return Path.GetDirectoryName(item.TargetPath) ?? string.Empty;
        }
        return string.Empty;
    }
}
