using System.Windows.Controls;
using QuietIsland.Models;
using QuietIsland.Services;

namespace QuietIsland.Controls;

internal static class LauncherActions
{
    public static void Add(IslandContext context, Action changed)
    {
        if (Windows.Dialogs.Launcher("Добавить ярлык") is not { } pin) return;
        context.State.Data.Launcher.Add(pin); context.State.Touch(); changed();
    }

    public static ContextMenu Menu(IslandContext context, LaunchItem pin, Action changed)
    {
        var menu = new ContextMenu();
        void AddItem(string label, string icon, Action action, bool changes = true)
        {
            var item = new MenuItem { Header = label, Icon = Icons.Colored(icon, 16) };
            item.Click += (_, _) =>
            {
                try { action(); if (changes) { context.State.Touch(); changed(); } }
                catch (Exception error) { context.Error(error); }
            };
            menu.Items.Add(item);
        }
        AddItem("Открыть", "play", () => { SystemService.Open(pin.Target); context.Collapse(); }, false);
        AddItem("Изменить ярлык", "file", () =>
        {
            if (Windows.Dialogs.Launcher("Изменить ярлык", pin) is not { } edited) return;
            pin.Name = edited.Name; pin.Target = edited.Target;
        });
        AddItem("Группа", "folder", () => { if (Windows.Dialogs.Ask("Группа", "Пусто — отдельный ярлык", pin.Group) is { } group) pin.Group = group.Trim(); });
        AddItem("Горячая клавиша", "command", () => { if (Windows.Dialogs.Ask("Горячая клавиша", "Например Ctrl+Alt+1; пусто — убрать", pin.Hotkey) is { } hotkey) pin.Hotkey = hotkey.Trim(); });
        AddItem("Убрать ярлык", "trash", () => context.State.Data.Launcher.Remove(pin));
        return menu;
    }
}
