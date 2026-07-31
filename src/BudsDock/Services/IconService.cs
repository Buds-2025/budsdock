using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            if (item.Id.StartsWith("builtin-", StringComparison.Ordinal))
            {
                return GetBuiltInGlowColor(item.Id);
            }

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

        if (item.Id.StartsWith("builtin-", StringComparison.Ordinal))
        {
            return CreateBuiltInIcon(item.Id, item.VisualMode != IconVisualMode.Original);
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
        var (gradientStart, gradientEnd) = GetBuiltInPalette(id);
        var foreground = Colors.White;
        var typeface = new Typeface(
            new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);
        const double size = 256;
        var group = new DrawingGroup();
        using (var drawing = group.Open())
        {
            if (!glyphOnly)
            {
                var surface = new LinearGradientBrush(
                    gradientStart,
                    gradientEnd,
                    new Point(0.18, 0),
                    new Point(0.82, 1));
                var border = new Pen(new SolidColorBrush(Color.FromArgb(82, 255, 255, 255)), 2.2);
                drawing.DrawRoundedRectangle(surface, border, new Rect(7, 7, 242, 242), 54, 54);

                var sheen = new LinearGradientBrush(
                    Color.FromArgb(54, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    new Point(0, 0),
                    new Point(0, 1));
                drawing.PushClip(new RectangleGeometry(new Rect(8, 8, 240, 112), 53, 53));
                drawing.DrawRectangle(sheen, null, new Rect(8, 8, 240, 112));
                drawing.Pop();
            }

            var text = new FormattedText(
                glyph,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                glyphOnly ? 142 : 118,
                new SolidColorBrush(foreground),
                1.0);
            var origin = new Point(
                (size - text.WidthIncludingTrailingWhitespace) / 2,
                (size - text.Height) / 2);
            if (!glyphOnly)
            {
                var shadowText = new FormattedText(
                    glyph,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    118,
                    new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                    1.0);
                drawing.DrawText(shadowText, new Point(origin.X, origin.Y + 5));
            }
            drawing.DrawText(text, origin);
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static (Color Start, Color End) GetBuiltInPalette(string id)
    {
        var isDark = ((App)Application.Current).ThemeService.IsDark;
        return id switch
        {
            "builtin-this-pc" => isDark
                ? (Color.FromRgb(70, 111, 220), Color.FromRgb(38, 67, 151))
                : (Color.FromRgb(103, 148, 255), Color.FromRgb(54, 91, 201)),
            "builtin-control-panel" => isDark
                ? (Color.FromRgb(146, 98, 220), Color.FromRgb(86, 55, 154))
                : (Color.FromRgb(178, 126, 255), Color.FromRgb(116, 73, 193)),
            "builtin-file-explorer" => isDark
                ? (Color.FromRgb(226, 160, 57), Color.FromRgb(154, 91, 25))
                : (Color.FromRgb(255, 194, 76), Color.FromRgb(214, 126, 29)),
            "builtin-recycle-bin" => isDark
                ? (Color.FromRgb(58, 169, 126), Color.FromRgb(24, 105, 79))
                : (Color.FromRgb(85, 199, 155), Color.FromRgb(35, 137, 100)),
            "builtin-edge" => isDark
                ? (Color.FromRgb(47, 176, 205), Color.FromRgb(35, 97, 189))
                : (Color.FromRgb(65, 201, 221), Color.FromRgb(43, 119, 215)),
            _ => isDark
                ? (Color.FromRgb(104, 128, 225), Color.FromRgb(59, 75, 158))
                : (Color.FromRgb(132, 159, 255), Color.FromRgb(74, 100, 202))
        };
    }

    private static Color GetBuiltInGlowColor(string id)
        => id switch
        {
            "builtin-this-pc" => Color.FromRgb(105, 146, 255),
            "builtin-control-panel" => Color.FromRgb(184, 126, 255),
            "builtin-file-explorer" => Color.FromRgb(255, 184, 72),
            "builtin-recycle-bin" => Color.FromRgb(77, 205, 155),
            "builtin-edge" => Color.FromRgb(61, 189, 229),
            _ => Color.FromRgb(124, 156, 255)
        };

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
