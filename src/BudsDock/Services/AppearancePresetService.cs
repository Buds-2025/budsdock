using BudsDock.Models;

namespace BudsDock.Services;

public enum AppearancePreset { Studio, Minimal, Classic }

public static class AppearancePresetService
{
    public static void Apply(AppSettings settings, AppearancePreset preset)
    {
        settings.DockScale = 1;
        settings.CornerRadius = preset == AppearancePreset.Classic ? 18 : 24;
        settings.IconSize = preset == AppearancePreset.Minimal ? 44 : 54;
        settings.IconSpacing = preset == AppearancePreset.Minimal ? 8 : 12;
        settings.PanelPadding = preset == AppearancePreset.Minimal ? 8 : 12;
        settings.BackgroundOpacity = preset == AppearancePreset.Classic ? .88 : .78;
        settings.ShowReflection = preset == AppearancePreset.Classic;
        settings.ReflectionOpacity = .24;
        settings.GlowIntensity = preset == AppearancePreset.Minimal ? 0 : .28;
        settings.EnableHoverAnimation = preset != AppearancePreset.Minimal;
        settings.HoverScale = preset == AppearancePreset.Classic ? 1.5 : 1.35;
        settings.AdjacentHoverScale = 1.12;
    }
}
