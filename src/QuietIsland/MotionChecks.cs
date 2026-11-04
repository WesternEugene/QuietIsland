using System.Diagnostics;
using System.Windows.Media;
using QuietIsland.Services;

namespace QuietIsland;
internal static class MotionChecks
{
    public static async Task<object> RunAsync(string directory)
    {
        using var c = new IslandContext(directory);
        var window = new MainWindow(c, true) { NativeMotionProbe = true };
        var preparation = Stopwatch.StartNew(); window.PrepareHome(); var preparationMilliseconds = preparation.Elapsed.TotalMilliseconds;
        var intervals = new List<double>(); var layouts = 0; var frames = 0; var last = TimeSpan.Zero;
        void Frame(object? _, EventArgs args)
        {
            if (args is not RenderingEventArgs e || e.RenderingTime == last) return;
            if (last != TimeSpan.Zero) intervals.Add((e.RenderingTime - last).TotalMilliseconds);
            last = e.RenderingTime; frames++;
        }
        window.Show(); window.ApplySettings(); Native.ConfigureOverlay(window, false, false, true); await Task.Delay(120);
        var before = window.NativePlacementChanges;
        var root = (Controls.RenderFrame)window.RenderRoot; var measureBefore = root.MeasurePasses; var arrangeBefore = root.ArrangePasses;
        window.RenderRoot.LayoutUpdated += (_, _) => layouts++;
        CompositionTarget.Rendering += Frame;
        var watch = Stopwatch.StartNew(); window.ShowPanel("home", false);
        var openAfterPreparationMilliseconds = watch.Elapsed.TotalMilliseconds;
        await Task.Delay(650);
        window.Collapse(); await Task.Delay(650);
        CompositionTarget.Rendering -= Frame;
        var nativePlacementChanges = window.NativePlacementChanges - before;
        var layoutSignals = layouts; var measurePasses = root.MeasurePasses - measureBefore; var arrangePasses = root.ArrangePasses - arrangeBefore;
        Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var rectangle);
        var transparentBoundsPassThrough = !OutsidePointer.IsOwnWindowAt(new((rectangle.Left + rectangle.Right) / 2, rectangle.Bottom - 5));
        var visibleNotchReceivesInput = OutsidePointer.IsOwnWindowAt(new((rectangle.Left + rectangle.Right) / 2, rectangle.Top + 15));
        c.Notify("Длинное уведомление должно оставаться внутри плавно расширяющейся чёлки");
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        window.RenderRoot.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(744, 430, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window.RenderRoot);
        var pixels = new byte[744 * 430 * 4]; bitmap.CopyPixels(pixels, 744 * 4, 0); var outsidePixels = 0;
        for (var y = 0; y < 52; y++) for (var x = 0; x < 744; x++)
        {
            var i = (y * 744 + x) * 4;
            if (pixels[i + 3] > 40 && Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) > 70 && !window.ContainsSurfaceForChecks(new(x + .5, y + .5))) outsidePixels++;
        }
        var compactTextStaysInsideSurface = outsidePixels == 0;
        c.Notify(""); await Task.Delay(650);
        window.ShowPanel("home", false); await Task.Delay(650); window.Collapse(); await Task.Delay(25);
        var beforeReversal = window.PanelVisualForChecks; window.ShowPanel("home", false); var afterReversal = window.PanelVisualForChecks;
        var reversalKeepsPanelContent = afterReversal.Opacity >= beforeReversal.Opacity - .05 && Math.Abs(afterReversal.Y - beforeReversal.Y) < .1;
        await Task.Delay(650); window.Collapse(); await Task.Delay(650);
        typeof(ActivityService).GetProperty(nameof(ActivityService.Layout))!.SetValue(c.Activity, "EN");
        window.RefreshCompactForChecks(); await Task.Delay(25); var badge = (System.Windows.Controls.Border)window.FindName("InputBadge");
        var layoutChangeAnimates = badge.Opacity < .98 && ((TranslateTransform)badge.RenderTransform).Y < -.1;
        await Task.Delay(240);
        typeof(ActivityService).GetProperty(nameof(ActivityService.CapsLock))!.SetValue(c.Activity, true); window.RefreshCompactForChecks(); await Task.Delay(25);
        var capsOnAnimates = badge.Opacity < .98 && ((TranslateTransform)badge.RenderTransform).Y < -.1; await Task.Delay(240);
        typeof(ActivityService).GetProperty(nameof(ActivityService.CapsLock))!.SetValue(c.Activity, false); window.RefreshCompactForChecks(); await Task.Delay(25);
        var capsOffAnimates = badge.Opacity < .98 && ((TranslateTransform)badge.RenderTransform).Y < -.1; await Task.Delay(240);
        var inputAnimationsSettle = badge.Opacity > .99 && Math.Abs(((TranslateTransform)badge.RenderTransform).Y) < .01;
        var ordered = intervals.OrderBy(x => x).ToArray();
        var result = new { nativePlacementChanges, layoutSignals, measurePasses, arrangePasses, frames, preparationMilliseconds, openAfterPreparationMilliseconds, frameMedianMs = ordered.Length > 0 ? ordered[ordered.Length / 2] : 0,
            frameP95Ms = ordered.Length > 0 ? ordered[(int)((ordered.Length - 1) * .95)] : 0,
            stableNativeBounds = nativePlacementChanges <= 1, noLayoutPerAnimationFrame = measurePasses < 8 && arrangePasses < 8, opensWithoutConstructionStall = openAfterPreparationMilliseconds < 50, transparentBoundsPassThrough, visibleNotchReceivesInput, compactTextStaysInsideSurface, outsidePixels, reversalKeepsPanelContent, layoutChangeAnimates, capsOnAnimates, capsOffAnimates, inputAnimationsSettle, fixture = "Own transparent WPF window; no desktop/audio capture or input injection" };
        window.Close(); return result;
    }
}
