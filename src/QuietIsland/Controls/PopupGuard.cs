using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace QuietIsland.Controls;

/// <summary>WPF popups have separate HWNDs; they still belong to the current island interaction.</summary>
internal sealed class PopupGuard
{
    private readonly HashSet<ContextMenu> menus = [];
    private readonly List<WeakReference<ComboBox>> combos = [];
    public bool IsOpen => menus.Count > 0 || combos.Any(x => x.TryGetTarget(out var combo) && combo.IsDropDownOpen);
    public PopupGuard(FrameworkElement owner)
    {
        owner.AddHandler(ContextMenuService.ContextMenuOpeningEvent, new ContextMenuEventHandler((_, e) =>
        {
            for (var node = e.OriginalSource as DependencyObject; node != null; node = ScrollRouting.Parent(node))
            {
                if (node is not FrameworkElement { ContextMenu: { } menu }) continue;
                menus.Add(menu);
                menu.Closed -= Closed; menu.Closed += Closed;
                // ContextMenuOpening precedes window deactivation. Drop cancelled requests after that turn.
                owner.Dispatcher.BeginInvoke(() => { if (!menu.IsOpen) menus.Remove(menu); }, DispatcherPriority.ContextIdle);
                break;
            }
        }), true);
    }
    private void Closed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu) { menus.Remove(menu); menu.Closed -= Closed; }
    }
    public void Observe(DependencyObject? source)
    {
        for (var node = source; node != null; node = ScrollRouting.Parent(node))
            if (node is ComboBox combo)
            {
                combos.RemoveAll(x => !x.TryGetTarget(out _));
                if (!combos.Any(x => x.TryGetTarget(out var old) && ReferenceEquals(old, combo))) combos.Add(new(combo));
                break;
            }
    }
}
