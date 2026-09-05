using BudsDock.Models;

namespace BudsDock.Services;

public static class LaunchTargetService
{
    public static bool IsSupported(string path)
        => Directory.Exists(path) || (File.Exists(path)
            && Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".lnk");

    public static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static DockItem Create(string path, IconVisualMode mode)
    {
        path = NormalizePath(path);
        var isFolder = Directory.Exists(path);
        var name = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) name = path;
        return new DockItem
        {
            Name = name, NameEn = name, TargetPath = path,
            WorkingDirectory = isFolder ? path : Path.GetDirectoryName(path) ?? string.Empty,
            Kind = isFolder ? LaunchTargetKind.Folder
                : Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    ? LaunchTargetKind.Shortcut : LaunchTargetKind.Executable,
            VisualMode = IconVisualMode.Original
        };
    }
}
