using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.CompilerServices;
using QuietIsland.Core;

namespace QuietIsland.Controls;

public static class FileDrag
{
    public static bool IsDragging { get; private set; }
    public static void Attach(FrameworkElement element, Func<IDataObject> data)
    {
        Point? origin = null;
        element.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var source = e.OriginalSource as DependencyObject;
            var action = false;
            while (source != null && source != element)
            {
                if (source is ButtonBase or TextBoxBase or ComboBox or Slider) { action = true; break; }
                source = ScrollRouting.Parent(source);
            }
            origin = action ? null : e.GetPosition(element);
        };
        element.PreviewMouseLeftButtonUp += (_, _) => origin = null;
        element.PreviewMouseMove += (_, e) =>
        {
            if (origin is not { } start) return;
            var p = e.GetPosition(element);
            if (!InteractionPolicy.ShouldStartDrag(p.X - start.X, p.Y - start.Y, SystemParameters.MinimumHorizontalDragDistance,
                SystemParameters.MinimumVerticalDragDistance, e.LeftButton == MouseButtonState.Pressed, false)) return;
            origin = null;
            IsDragging = true;
            try { DragDrop.DoDragDrop(element, data(), DragDropEffects.Copy | DragDropEffects.Move); }
            finally { IsDragging = false; }
            e.Handled = true;
        };
    }
}

/// <summary>Pixel wheel routing, including WM_MOUSEHWHEEL from precision touchpads.</summary>
public sealed class ScrollRouting : IDisposable
{
    private sealed class PendingOffsets { public double X = double.NaN, Y = double.NaN; public long XTime, YTime; }
    private static readonly ConditionalWeakTable<ScrollViewer, PendingOffsets> Pending = new();
    static ScrollRouting()
    {
        // Combo-box drop-downs have their own presentation source and do not bubble to a Window.
        EventManager.RegisterClassHandler(typeof(ScrollViewer), Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) =>
        {
            if (!e.Handled) e.Handled = Route(e.OriginalSource as DependencyObject, -e.Delta * .4, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }));
    }
    private readonly Window window;
    private HwndSource? source;
    public ScrollRouting(Window owner)
    {
        window = owner;
        window.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel), true);
        window.SourceInitialized += Initialized;
    }
    private void Initialized(object? sender, EventArgs e)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);
    }
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (window is MainWindow main && !main.IsExpanded) return;
        if (FindAncestor<ComboBox>(e.OriginalSource as DependencyObject) is { IsDropDownOpen: true }) return;
        var horizontal = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Handled = Route(e.OriginalSource as DependencyObject, -e.Delta * .4, horizontal);
        if (FindAncestor<ComboBox>(e.OriginalSource as DependencyObject) != null) e.Handled = true;
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg != 0x020E) return IntPtr.Zero;
        var value = l.ToInt64(); var point = window.PointFromScreen(new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff)));
        var delta = (short)((w.ToInt64() >> 16) & 0xffff);
        handled = Route(window.InputHitTest(point) as DependencyObject, delta * .4, true);
        return IntPtr.Zero;
    }
    internal static bool Route(DependencyObject? hit, double pixels, bool horizontal)
    {
        for (var node = hit; node != null; node = Parent(node))
        {
            if (node is not ScrollViewer scroll) continue;
            var actual = horizontal ? scroll.HorizontalOffset : scroll.VerticalOffset;
            var pending = Pending.GetOrCreateValue(scroll); var stamp = Environment.TickCount64;
            var requested = horizontal ? pending.X : pending.Y; var time = horizontal ? pending.XTime : pending.YTime;
            var offset = double.IsFinite(requested) && stamp - time < 150 && Math.Abs(requested - actual) > .1 ? requested : actual;
            var max = horizontal ? scroll.ScrollableWidth : scroll.ScrollableHeight;
            if (!InteractionPolicy.CanScroll(offset, max, pixels)) continue;
            var target = Math.Clamp(offset + pixels, 0, max);
            if (horizontal) { pending.X = target; pending.XTime = stamp; scroll.ScrollToHorizontalOffset(target); }
            else { pending.Y = target; pending.YTime = stamp; scroll.ScrollToVerticalOffset(target); }
            return true;
        }
        return false;
    }
    internal static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    { for (; node != null; node = Parent(node)) if (node is T match) return match; return null; }
    internal static DependencyObject? Parent(DependencyObject node) => node is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    public void Dispose() { source?.RemoveHook(Hook); window.SourceInitialized -= Initialized; }
}
