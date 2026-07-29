namespace BudsDock.Models;

public static class DefaultDockItems
{
    public static IReadOnlyList<DockItem> Create() =>
    [
        new DockItem
        {
            Id = "builtin-this-pc",
            Name = "我的电脑",
            NameEn = "This PC",
            BuiltInNameKey = "Item.ThisPC",
            Kind = LaunchTargetKind.SystemCommand,
            TargetPath = "explorer.exe",
            Arguments = "shell:MyComputerFolder",
            IsBuiltIn = true,
            VisualMode = IconVisualMode.Original
        },
        new DockItem
        {
            Id = "builtin-control-panel",
            Name = "控制面板",
            NameEn = "Control Panel",
            BuiltInNameKey = "Item.ControlPanel",
            Kind = LaunchTargetKind.SystemCommand,
            TargetPath = "control.exe",
            IsBuiltIn = true,
            VisualMode = IconVisualMode.Original
        },
        new DockItem
        {
            Id = "builtin-file-explorer",
            Name = "文件资源管理器",
            NameEn = "File Explorer",
            BuiltInNameKey = "Item.FileExplorer",
            Kind = LaunchTargetKind.Executable,
            TargetPath = "explorer.exe",
            IsBuiltIn = true,
            VisualMode = IconVisualMode.Original
        },
        new DockItem
        {
            Id = "builtin-edge",
            Name = "Microsoft Edge",
            NameEn = "Microsoft Edge",
            BuiltInNameKey = "Item.Edge",
            Kind = LaunchTargetKind.ShellUri,
            TargetPath = "microsoft-edge:",
            IsBuiltIn = true,
            VisualMode = IconVisualMode.Original
        },
        new DockItem
        {
            Id = "builtin-recycle-bin",
            Name = "回收站",
            NameEn = "Recycle Bin",
            BuiltInNameKey = "Item.RecycleBin",
            Kind = LaunchTargetKind.SystemCommand,
            TargetPath = "explorer.exe",
            Arguments = "shell:RecycleBinFolder",
            IsBuiltIn = true,
            VisualMode = IconVisualMode.Original
        }
    ];
}
