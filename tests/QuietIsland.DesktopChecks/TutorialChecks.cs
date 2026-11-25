using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using QuietIsland;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class TutorialChecks
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("tutorial_first_launch_is_remembered_and_replayable", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "quiet-island-tutorial-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "state.json"), """{"Notes":[{"Title":"Сохранённая заметка"}]}""");
            using (var context = new IslandContext(path))
            {
                var main = new MainWindow(context, forSnapshot: true);
                try
                {
                    Invoke(main, "ShowTutorialIfPending");
                    var tutorial = OpenTutorials().Single();
                    Invoke(main, "ShowTutorialIfPending");
                    Require(OpenTutorials().Single() == tutorial, "The pending tutorial opened a second window.");
                    Click(Find<Button>(tutorial, "SkipButton"));
                    Require(!tutorial.IsVisible, "Skipping did not close the tutorial.");
                }
                finally { main.Close(); }
            }
            using (var context = new IslandContext(path))
            {
                var main = new MainWindow(context, forSnapshot: true);
                try
                {
                    Invoke(main, "ShowTutorialIfPending");
                    Require(!OpenTutorials().Any(), "The tutorial opened again after a restart.");
                    Require(context.State.Data.Notes.Single().Title == "Сохранённая заметка", "Remembering the tutorial changed existing notes.");
                    Invoke(main, "ShowTutorial");
                    Require(OpenTutorials().Single().IsVisible, "The tutorial could not be opened again from the menu.");
                }
                finally { main.Close(); }
            }
            return Task.CompletedTask;
        });

        await check("tutorial_step_actions_open_the_real_panel", () =>
        {
            using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "quiet-island-tutorial-" + Guid.NewGuid().ToString("N")));
            var main = new MainWindow(context, forSnapshot: true);
            try
            {
                Invoke(main, "ShowTutorialIfPending");
                var tutorial = OpenTutorials().Single();
                var next = Find<Button>(tutorial, "NextButton");
                var back = Find<Button>(tutorial, "BackButton");
                var actions = Find<WrapPanel>(tutorial, "StepActions");
                var body = Find<ContentControl>(main, "Body");

                Click(next);
                Click(actions.Children.OfType<Button>().Single());
                Require(body.Content is SearchView, "The search step did not open the search panel.");

                Click(next);
                var fileActions = actions.Children.OfType<Button>().ToArray();
                Click(fileActions[0]);
                Require(body.Content is ClipboardView, "The clipboard action did not open the clipboard.");
                Click(fileActions[1]);
                Require(body.Content is ShelfView, "The shelf action did not open the shelf.");

                Click(back);
                Click(actions.Children.OfType<Button>().Single());
                Require(body.Content is SearchView, "Back did not restore the previous step's action.");

                for (var i = 0; i < 8 && !Equals(next.Content, "Готово"); i++) Click(next);
                Require(Equals(next.Content, "Готово"), "The last step could not be reached.");
                Click(next);
                Require(!tutorial.IsVisible, "Finishing did not close the tutorial.");
            }
            finally { main.Close(); }
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<Window> OpenTutorials() => Application.Current.Windows.OfType<Window>()
        .Where(window => window.GetType().Name == "TutorialWindow" && window.IsVisible);

    private static T Find<T>(Window window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"Could not find {name}.");

    private static void Invoke(MainWindow window, string name)
    {
        var method = typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Require(method != null, $"The window is missing the {name} entry point.");
        method!.Invoke(window, null);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
