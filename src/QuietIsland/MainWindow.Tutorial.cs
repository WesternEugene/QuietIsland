using System.Windows;
using QuietIsland.Services;
using QuietIsland.Windows;

namespace QuietIsland;

public partial class MainWindow
{
    private TutorialWindow? tutorialWindow;

    internal void ShowTutorialIfPending()
    {
        if (!c.State.Data.TutorialDismissed) ShowTutorial();
    }

    private void ShowTutorial()
    {
        if (IsClosing) return;
        if (tutorialWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var shortcut = shortcutErrors.ContainsKey("search") ? null : GlobalShortcuts.Bindings(c.Settings).GetValueOrDefault("search");
        var tutorial = new TutorialWindow(shortcut, tab => ShowPanel(tab, true));
        tutorialWindow = tutorial;
        tutorial.Closed += (_, _) =>
        {
            tutorialWindow = null;
            if (IsClosing || c.State.Data.TutorialDismissed) return;
            c.State.Data.TutorialDismissed = true;
            c.State.Save();
        };
        tutorial.Show();
    }
}
