using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using BudsDock.Models;

namespace BudsDock.Services;

public sealed class IconService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Color> _glowColorCache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource GetImage(DockItem item)
    {
        var key = CreateCacheKey(item);
        return _cache.GetOrAdd(key, _ => LoadImage(item));
    }

    public Color GetGlowColor(DockItem item)
    {
        var key = CreateCacheKey(item);
        return _glowColorCache.GetOrAdd(key, _ =>
        {
            var fallback = Application.Current?.TryFindResource("GlowColor") is Color color
                ? color
                : Color.FromRgb(112, 142, 255);
            return GetImage(item) is BitmapSource bitmap
                ? CalculateGlowColor(bitmap, fallback)
                : fallback;
        });
    }

    public void ClearCache()
    {
        _cache.Clear();
        _glowColorCache.Clear();
    }

    public static Color CalculateGlowColor(BitmapSource source, Color fallback)
    {
        BitmapSource bitmap = source;
        if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32)
        {
            bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }

        var stride = checked(bitmap.PixelWidth * 4);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);
        return CalculateGlowColor(pixels, bitmap.PixelWidth, bitmap.PixelHeight, stride, fallback);
    }

    public static Color CalculateGlowColor(
        ReadOnlySpan<byte> bgraPixels,
        int width,
        int height,
        int stride,
        Color fallback)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || bgraPixels.Length < stride * height)
        {
            return fallback;
        }

        var sampleStep = Math.Max(1, Math.Max(width, height) / 40);
        double red = 0;
        double green = 0;
        double blue = 0;
        double totalWeight = 0;

        for (var y = 0; y < height; y += sampleStep)
        {
            for (var x = 0; x < width; x += sampleStep)
            {
                var offset = (y * stride) + (x * 4);
                var b = bgraPixels[offset];
                var g = bgraPixels[offset + 1];
                var r = bgraPixels[offset + 2];
                var alpha = bgraPixels[offset + 3] / 255d;
                if (alpha < 0.12)
                {
                    continue;
                }

                var maximum = Math.Max(r, Math.Max(g, b));
                var minimum = Math.Min(r, Math.Min(g, b));
                var brightness = maximum / 255d;
                var saturation = maximum == 0 ? 0 : (maximum - minimum) / (double)maximum;
                if (brightness < 0.10 || saturation < 0.10)
                {
                    continue;
                }

                var weight = alpha * saturation * saturation * (0.35 + brightness);
                red += r * weight;
                green += g * weight;
                blue += b * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight < 0.01)
        {
            return fallback;
        }

        var outputRed = red / totalWeight;
        var outputGreen = green / totalWeight;
        var outputBlue = blue / totalWeight;
        var brightest = Math.Max(outputRed, Math.Max(outputGreen, outputBlue));
        if (brightest < 168)
        {
            var boost = 168 / Math.Max(1, brightest);
            outputRed *= boost;
            outputGreen *= boost;
            outputBlue *= boost;
        }

        return Color.FromRgb(
            (byte)Math.Clamp(Math.Round(outputRed), 0, 255),
            (byte)Math.Clamp(Math.Round(outputGreen), 0, 255),
            (byte)Math.Clamp(Math.Round(outputBlue), 0, 255));
    }

    private static string CreateCacheKey(DockItem item)
        => $"{item.Id}|{item.CustomIconPath}|{item.TargetPath}|{item.VisualMode}|{((App)Application.Current).ThemeService.Revision}";

    private ImageSource LoadImage(DockItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.CustomIconPath) && File.Exists(item.CustomIconPath))
        {
            var custom = LoadBitmap(item.CustomIconPath);
            if (custom is not null)
            {
                return custom;
            }
        }

        if (item.Id == "builtin-edge" && item.VisualMode != IconVisualMode.Monochrome)
        {
            var edge = ResolveEdgePath();
            var edgeIcon = edge is null ? null : ExtractAssociatedIcon(edge);
            if (edgeIcon is not null)
            {
                return edgeIcon;
            }
        }

        if (item.Id.StartsWith("builtin-", StringComparison.Ordinal))
        {
            return CreateBuiltInIcon(item.Id, item.VisualMode == IconVisualMode.Monochrome);
        }

        var target = ResolveExecutablePath(item.TargetPath);
        return ExtractAssociatedIcon(target) ?? CreateBuiltInIcon("builtin-generic", item.VisualMode == IconVisualMode.Monochrome);
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? ExtractAssociatedIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var result = SHGetFileInfo(path, 0, out var fileInfo, (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon | ShgfiLargeIcon);
        if (result == IntPtr.Zero || fileInfo.IconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(fileInfo.IconHandle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(96, 96));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(fileInfo.IconHandle);
        }
    }

    private static string ResolveExecutablePath(string target)
    {
        if (Path.IsPathRooted(target))
        {
            return target;
        }

        var systemCandidate = Path.Combine(Environment.SystemDirectory, target);
        return File.Exists(systemCandidate) ? systemCandidate : target;
    }

    private static string? ResolveEdgePath()
    {
        var registryLocations = new[]
        {
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"),
            (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe")
        };

        foreach (var (root, subKey) in registryLocations)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key?.GetValue(null) is string value && File.Exists(value))
                {
                    return value;
                }
            }
            catch
            {
                // Fall through to conventional paths.
            }
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static ImageSource CreateBuiltInIcon(string id, bool glyphOnly)
    {
        var glyph = id switch
        {
            "builtin-this-pc" => "\uE7F4",
            "builtin-control-panel" => "\uE713",
            "builtin-file-explorer" => "\uEC50",
            "builtin-recycle-bin" => "\uE74D",
            "builtin-edge" => "\uE774",
            _ => "\uE8B7"
        };
        var background = GetBuiltInBackground(id);

        var foreground = (Application.Current?.TryFindResource("IconForegroundBrush") as SolidColorBrush)?.Color ?? Colors.White;
        var typeface = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol, Microsoft YaHei UI, Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        const double size = 96;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (!glyphOnly)
            {
                drawing.DrawRoundedRectangle(new SolidColorBrush(background), null, new Rect(3, 3, 90, 90), 18, 18);
            }
            var text = new FormattedText(
                glyph,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                46,
                new SolidColorBrush(foreground),
                1.0);
            drawing.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap((int)size, (int)size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color GetBuiltInBackground(string id)
    {
        var isDark = ((App)Application.Current).ThemeService.IsDark;
        return id switch
        {
            "builtin-this-pc" => isDark ? Color.FromRgb(54, 91, 178) : Color.FromRgb(86, 132, 255),
            "builtin-control-panel" => isDark ? Color.FromRgb(112, 78, 184) : Color.FromRgb(164, 118, 255),
            "builtin-file-explorer" => isDark ? Color.FromRgb(177, 116, 36) : Color.FromRgb(242, 177, 74),
            "builtin-recycle-bin" => isDark ? Color.FromRgb(36, 126, 97) : Color.FromRgb(68, 184, 145),
            "builtin-edge" => isDark ? Color.FromRgb(42, 114, 166) : Color.FromRgb(60, 162, 225),
            _ => isDark ? Color.FromRgb(86, 105, 190) : Color.FromRgb(126, 153, 255)
        };
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, out ShellFileInfo fileInfo, uint fileInfoSize, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
}
