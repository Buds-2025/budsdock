using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BudsDock.Models;
using BudsDock.ViewModels;
using BudsDock.Views;

namespace BudsDock.Services;

internal static class DiagnosticService
{
    internal static async Task ValidateAndCaptureAsync(App app, DockViewModel dock, SettingsViewModel settings,
        DockWindow dockWindow, SettingsWindow window)
    {
        var passed = new List<string>();
        var originals = dock.Items.ToArray();
        var first = originals.FirstOrDefault();
        if (first is not null)
        {
            settings.SearchText = "__no_match_diagnostic__";
            Require(settings.ItemsView.IsEmpty && dock.Items.Count == originals.Length, "Search isolation", passed);
            settings.SearchText = string.Empty;
            if (originals.Length > 1)
            {
                dock.MoveItem(first, originals[1]);
                Require(dock.Items[1] == first, "Dock reorder", passed);
                dock.MoveItem(first, originals[1]);
            }
        }
        var imagePath = Path.Combine(app.SettingsService.DataDirectory, "diagnostic-icon.png");
        var pixels = Enumerable.Repeat((byte)255, 64 * 64 * 4).ToArray();
        var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(imagePath)) encoder.Save(output);
        var batch = Enumerable.Range(0, 300).Select(i => new DockItem
        { Name = $"Diagnostic {i}", CustomIconPath = imagePath }).ToArray();
        foreach (var item in batch) app.IconService.GetImage(item);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (batch.Any(item => item.IconRevision == 0) && DateTime.UtcNow < deadline) await Task.Delay(50);
        Require(batch.All(item => item.IconRevision > 0), "Async icon completion", passed);
        Require(app.IconService.CachedImageCount <= 256, "Bounded concurrent icon cache", passed);
        app.IconService.ClearCache();
        settings.SelectedItem = first;
        for (var page = 0; page < 4; page++)
        {
            settings.SelectedPageIndex = page;
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
            await Task.Delay(100);
            Capture(window, Path.Combine(app.SettingsService.DataDirectory, $"settings-{page}.png"));
        }
        dockWindow.ApplyPlacement(dock.Settings.Placement);
        await Task.Delay(100);
        Capture(dockWindow, Path.Combine(app.SettingsService.DataDirectory, "dock.png"), backdrop: true);
        if (dock.Items.Count > 0)
        {
            dock.SetHover(dock.Items[dock.Items.Count / 2]);
            await Task.Delay(380);
            Capture(dockWindow, Path.Combine(app.SettingsService.DataDirectory, "dock-hover.png"), backdrop: true);
            dock.SetHover(null);
        }
        File.WriteAllText(Path.Combine(app.SettingsService.DataDirectory, "integration-results.json"),
            System.Text.Json.JsonSerializer.Serialize(passed));
    }

    private static void Require(bool condition, string name, List<string> passed)
    {
        if (!condition) throw new InvalidOperationException($"Integration check failed: {name}");
        passed.Add(name);
    }

    private static void Capture(FrameworkElement element, string path, bool backdrop = false)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        if (backdrop)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle((Brush)Application.Current.FindResource("WindowBackgroundBrush"), null,
                    new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            }
            var composite = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            composite.Render(visual);
            bitmap = composite;
        }
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
