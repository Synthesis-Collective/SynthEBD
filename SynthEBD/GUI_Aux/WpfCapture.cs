using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SynthEBD;

/// <summary>
/// Captures a shown WPF window to a PNG file. Used by the CLI <c>ui-screenshot</c> verb (the
/// automated visual-QA harness for the UI); the SynthEBD GUI itself does not call this.
/// </summary>
public static class WpfCapture
{
    /// <summary>
    /// Renders <paramref name="window"/> into a PNG at <paramref name="filePath"/> (parent folders
    /// are created). Must be called on the window's dispatcher thread: pending layout/render work
    /// is drained at ContextIdle priority (twice, since idle callbacks can queue further work),
    /// then <paramref name="settleMs"/> gives asynchronously-loaded content (preview images,
    /// GL surfaces) a chance to appear before the frame is captured at the window's current DPI.
    /// </summary>
    public static async Task SaveWindowPngAsync(Window window, string filePath, int settleMs = 250)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
        if (settleMs > 0)
        {
            // The dispatcher SynchronizationContext resumes this continuation on the UI thread,
            // which keeps pumping (and thus rendering) during the delay.
            await Task.Delay(settleMs);
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        // Window.ActualHeight includes the native title bar and borders, which are not part of the
        // window's VISUAL (they are non-client chrome) - sizing by it leaves a dead strip in the
        // capture. The descendant bounds cover exactly the rendered client content.
        var bounds = VisualTreeHelper.GetDescendantBounds(window);
        int pixelWidth = (int)Math.Ceiling(bounds.Right * dpi.DpiScaleX);
        int pixelHeight = (int)Math.Ceiling(bounds.Bottom * dpi.DpiScaleY);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            throw new InvalidOperationException("Window has no rendered size to capture (is it shown and laid out?).");
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }
}
