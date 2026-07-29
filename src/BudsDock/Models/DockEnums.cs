namespace BudsDock.Models;

public enum ThemeMode
{
    System,
    Dark,
    Light
}

public enum AppLanguage
{
    System,
    ChineseSimplified,
    English
}

public enum DockOrientation
{
    Horizontal,
    Vertical
}

public enum DockPlacement
{
    Free,
    TopCenter,
    BottomCenter,
    LeftCenter,
    RightCenter,
    ScreenCenter
}

public enum IconVisualMode
{
    Original,
    Tile,
    Monochrome
}

public enum LaunchTargetKind
{
    Executable,
    Shortcut,
    ShellUri,
    SystemCommand
}
