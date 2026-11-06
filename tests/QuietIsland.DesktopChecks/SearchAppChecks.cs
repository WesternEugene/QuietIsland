using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietIsland.Controls;
using QuietIsland.Models;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class SearchAppChecks
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "quiet-island-search-" + Guid.NewGuid().ToString("N"));
        using var context = new IslandContext(directory);
        context.State.Data.Launcher.Add(new LaunchItem { Name = "Home-only launcher fixture", Target = "notepad.exe" });
        using var view = new SearchView(context);
        view.Measure(new Size(680, 330)); view.Arrange(new Rect(0, 0, 680, 330)); view.UpdateLayout();
        await check("search_keeps_pinned_launchers_only_on_home", async () =>
        {
            await SearchAsync(view, "");
            Require(!Descendants(view).OfType<Button>().Any(button => button.Content is string text &&
                (text == "+ Ярлык" || text.Contains("Home-only launcher fixture"))), "Search still has a pinned launcher panel or its add button.");
        });
        foreach (var (query, title) in new[] { ("s экран", "Экран"), ("s display", "Экран"), ("s звук", "Звук"), ("s bluetooth", "Bluetooth и устройства"), ("s windows update", "Обновление Windows") })
            await check("settings_search_" + query, async () =>
            {
                await SearchAsync(view, query);
                var list = Field<ListBox>(view, "list");
                var page = QuietIsland.Core.WindowsSettingsCatalog.Search(query[2..]).First();
                var localizer = typeof(IslandContext).Assembly.GetType("QuietIsland.Services.SettingsLanguage")!.GetMethod("Name", BindingFlags.Static | BindingFlags.NonPublic)!;
                var expected = (string)localizer.Invoke(null, [page])!;
                Require(list.SelectedItem is ListBoxItem row && RowName(row) == expected, "The settings query did not select the system-language title " + expected + ".");
                Require(!list.Items.OfType<ListBoxItem>().Any(row => RowName(row).StartsWith("Искать «")), "Settings search leaked into the file/web search.");
            });
        await check("settings_search_without_query_offers_pages", async () =>
        {
            await SearchAsync(view, "s");
            var list = Field<ListBox>(view, "list");
            Require(list.Items.Count >= 10 && list.SelectedItem is ListBoxItem row && !string.IsNullOrWhiteSpace(RowName(row)), "The s shortcut has no Windows settings pages.");
        });
        await check("packaged_applications_are_indexed_by_shell_identity", async () =>
        {
            var expected = PackagedApplications();
            Require(expected.Count > 0, "Windows AppsFolder has no packaged applications to verify on this desktop.");
            var discovered = await Task.Run(SystemService.DiscoverApplications);
            foreach (var application in expected)
                Require(discovered.Any(result => result.Name == application.Name && result.Path.Equals(application.Path, StringComparison.OrdinalIgnoreCase)),
                    "App discovery omitted packaged application " + application.Name + " (" + application.Path + ").");
            var selected = expected[0];
            typeof(IslandContext).GetProperty(nameof(IslandContext.Applications))!.SetValue(context, discovered);
            await SearchAsync(view, selected.Name);
            Require(Field<ListBox>(view, "list").Items.OfType<ListBoxItem>().Any(row => RowName(row) == selected.Name), "A packaged app is indexed but absent from the search results.");
            await WaitForImageAsync((ContentControl)ShellIcons.File(selected.Path, 38));
        });
        await check("classic_start_menu_shortcut_keeps_filesystem_launch_actions", async () =>
        {
            var folders = new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) };
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            var shortcuts = folders.SelectMany(folder => Directory.EnumerateFiles(folder, "*.lnk", options)).ToArray();
            var shortcut = shortcuts.FirstOrDefault(path => Path.GetFileNameWithoutExtension(path) == "Visual Studio Code")
                ?? shortcuts.FirstOrDefault(path => Path.GetFileNameWithoutExtension(path) == "Windows PowerShell");
            Require(shortcut != null, "The desktop has no known classic Start Menu shortcut to verify.");
            var name = Path.GetFileNameWithoutExtension(shortcut!);
            var discovered = await Task.Run(SystemService.DiscoverApplications);
            Require(discovered.Any(result => result.Name == name && result.Path.Equals(shortcut, StringComparison.OrdinalIgnoreCase)),
                "AppsFolder replaced the classic " + name + " .lnk target, disabling run-as-admin and reveal modifiers.");
            typeof(IslandContext).GetProperty(nameof(IslandContext.Applications))!.SetValue(context, discovered);
            await SearchAsync(view, name);
            var selected = Field<ListBox>(view, "list").SelectedItem as ListBoxItem;
            Require(selected != null && RowName(selected) == name && selected.ContextMenu?.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Показать в папке")) == true,
                "The classic application result no longer offers filesystem actions.");
        });
        foreach (var target in new[] { "notepad.exe", "explorer.exe", "powershell.exe", "calc.exe", "ms-settings:" })
            await check("launcher_uses_windows_icon_" + target, async () =>
        {
            var launcher = Icons.Launcher(target, target, 38);
            Require(launcher is ContentControl, "Pinned launchers still use a synthetic vector badge.");
            await WaitForImageAsync((ContentControl)launcher);
        });
        await check("shortcut_icon_has_no_arrow_overlay", async () =>
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "shortcut-icon.lnk");
            var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            object? shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                shortcut = ((dynamic)shell!).CreateShortcut(path);
                ((dynamic)shortcut).TargetPath = target;
                ((dynamic)shortcut).IconLocation = target + ",0";
                ((dynamic)shortcut).Save();
                var source = (ContentControl)ShellIcons.File(target, 38);
                var link = (ContentControl)ShellIcons.File(path, 38);
                var expected = await WaitForImageAsync(source);
                var actual = await WaitForImageAsync(link);
                Require(expected.PixelWidth == actual.PixelWidth && expected.PixelHeight == actual.PixelHeight && Pixels(expected).SequenceEqual(Pixels(actual)),
                    "The shortcut icon differs from its explicit application icon, usually because a link-arrow overlay was added.");
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    private static List<(string Name, string Path)> PackagedApplications()
    {
        object? shell = null, folder = null, items = null;
        var result = new List<(string, string)>();
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            items = ((dynamic)folder!).Items();
            for (var i = 0; i < (int)((dynamic)items).Count; i++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(i);
                    var id = (string?)((dynamic)item!).ExtendedProperty("System.AppUserModel.ID");
                    if (id?.Contains('!') == true) result.Add(((string)((dynamic)item).Name, "shell:AppsFolder\\" + id));
                }
                finally { if (item != null) Marshal.FinalReleaseComObject(item); }
            }
        }
        finally
        {
            if (items != null) Marshal.FinalReleaseComObject(items);
            if (folder != null) Marshal.FinalReleaseComObject(folder);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
        return result;
    }

    private static async Task<BitmapSource> WaitForImageAsync(ContentControl host)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (host.Content is Image { Source: BitmapSource bitmap }) return bitmap;
            await Task.Delay(20);
        }
        throw new InvalidOperationException("The Windows shell icon did not load.");
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels;
    }
    private static async Task SearchAsync(SearchView view, string input)
    {
        Field<TextBox>(view, "query").Text = input;
        await (Task)typeof(SearchView).GetMethod("SearchAsync", Fields)!.Invoke(view, null)!;
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static string RowName(ListBoxItem row) => row.Content is TextBlock text ? text.Text : ((Grid)row.Content).Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().First().Text;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
