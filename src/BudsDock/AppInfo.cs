using System.Reflection;

namespace BudsDock;

/// <summary>
/// Statically bound brand/version info so XAML can render the footer without
/// needing to round-trip through any service or resource lookup.  Source of
/// truth is the assembly's <see cref="AssemblyInformationalVersionAttribute"/>,
/// with a sensible fallback.
/// </summary>
public static class AppInfo
{
    public static string Version { get; } = ResolveVersion();

    /// <summary>
    /// Default hover magnification.  Exposed as a static so XAML animations
    /// can use it via x:Static — Storyboard timelines cannot be frozen when
    /// their targets come from RelativeSource bindings, but they can be frozen
    /// when they reference compile-time constants.
    /// </summary>
    public const double HoverScale = 1.50;

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip git metadata after '+' (semver build metadata).
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString(3);
    }
}
