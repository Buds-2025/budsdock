using BudsDock.ViewModels;

namespace BudsDock.Models;

public sealed class DockItem : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _nameEn = string.Empty;
    private string? _builtInNameKey;
    private string _targetPath = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private LaunchTargetKind _kind = LaunchTargetKind.Executable;
    private string? _customIconPath;
    private IconVisualMode _visualMode = IconVisualMode.Original;
    private bool _runAsAdministrator;
    private bool _isBuiltIn;
    private double _hoverScale = 1.0;
    private double _hoverOffsetX;
    private double _hoverOffsetY;
    private bool _isHovered;
    private int _iconRevision;

    [System.Text.Json.Serialization.JsonIgnore]
    public int IconRevision { get => _iconRevision; set => SetProperty(ref _iconRevision, value); }

    public string Id { get => _id; set => SetProperty(ref _id, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string NameEn { get => _nameEn; set => SetProperty(ref _nameEn, value); }
    public string? BuiltInNameKey { get => _builtInNameKey; set => SetProperty(ref _builtInNameKey, value); }
    public string TargetPath { get => _targetPath; set => SetProperty(ref _targetPath, value); }
    public string Arguments { get => _arguments; set => SetProperty(ref _arguments, value); }
    public string WorkingDirectory { get => _workingDirectory; set => SetProperty(ref _workingDirectory, value); }
    public LaunchTargetKind Kind { get => _kind; set => SetProperty(ref _kind, value); }
    public string? CustomIconPath { get => _customIconPath; set => SetProperty(ref _customIconPath, value); }
    // Legacy field is read for compatibility and omitted from new default configurations.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public IconVisualMode VisualMode { get => _visualMode; set => SetProperty(ref _visualMode, value); }
    public bool RunAsAdministrator { get => _runAsAdministrator; set => SetProperty(ref _runAsAdministrator, value); }
    public bool IsBuiltIn { get => _isBuiltIn; set => SetProperty(ref _isBuiltIn, value); }

    [System.Text.Json.Serialization.JsonIgnore]
    public double HoverScale { get => _hoverScale; set => SetProperty(ref _hoverScale, value); }

    [System.Text.Json.Serialization.JsonIgnore]
    public double HoverOffsetX { get => _hoverOffsetX; set => SetProperty(ref _hoverOffsetX, value); }

    [System.Text.Json.Serialization.JsonIgnore]
    public double HoverOffsetY { get => _hoverOffsetY; set => SetProperty(ref _hoverOffsetY, value); }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsHovered { get => _isHovered; set => SetProperty(ref _isHovered, value); }
}
