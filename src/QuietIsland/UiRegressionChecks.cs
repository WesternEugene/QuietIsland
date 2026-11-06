using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using QuietIsland.Services;

namespace QuietIsland;
internal static class UiRegressionChecks
{
    public static async Task<object> RunAsync(IslandContext c)
    {
        var window = new MainWindow(c, true);
        window.ShowPanel("ai", false);
        window.HandleDragEnter(new DataObject(DataFormats.FileDrop, new[] { "C:\\fixture.txt" }));
        var aiDropPreserved = window.SelectedTab == "ai";
        window.ShowPanel("search", false);
        window.HandleDragEnter(new DataObject("QuietIsland.Launcher", new Models.LaunchItem { Name = "Fixture" }));
        var launcherDropPreserved = window.SelectedTab == "search";
        var collapsed = new MainWindow(c, true);
        collapsed.HandleDragEnter(new DataObject(DataFormats.FileDrop, new[] { "C:\\fixture.txt" }));
        var collapsedOpensShelf = collapsed.SelectedTab == "shelf";

        c.State.Data.TimerEnd = DateTimeOffset.Now.AddSeconds(-1);
        var reminder = new QuietIsland.Core.Reminder { Title = "Hidden reminder fixture", Due = DateTimeOffset.Now.AddSeconds(-1) };
        c.State.Data.Reminders.Add(reminder); window.SetIslandHidden(true);
        await window.TickAsync();
        var alarm = (FrameworkElement)window.FindName("CompactAlarm"); var alarmTitle = (TextBlock)window.FindName("CompactAlarmTitle");
        var hiddenReminderFires = reminder.Completed && alarm.Visibility == Visibility.Visible && alarmTitle.Text == reminder.Title;
        ((Button)window.FindName("CompactTimerDismiss")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var hiddenTimerFires = c.State.Data.TimerEnd == null && alarm.Visibility == Visibility.Visible && alarmTitle.Text == "Таймер";
        ((Button)window.FindName("CompactTimerDismiss")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        c.State.Data.Reminders.Remove(reminder); c.State.Data.TimerEnd = null;

        var pillWindow = new MainWindow(c, true); var root = pillWindow.RenderRoot; pillWindow.Content = null;
        using var source = new HwndSource(new HwndSourceParameters("QuietIsland.HitCheck") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 744, Height = 430 });
        source.RootVisual = root; root.Measure(new Size(744, 430)); root.Arrange(new Rect(0, 0, 744, 430)); root.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(744, 430, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(root);
        var x0 = (744 - pillWindow.NotchWidth) / 2;
        var pillHitArea = new[] { new Point(x0+25,20), new Point(744-x0-25,20), new Point(372,30), new Point(372,1) }.All(point => root.InputHitTest(point) != null);
        var transparentAreaPassesThrough = root.InputHitTest(new Point(x0+2,12)) == null && root.InputHitTest(new Point(372,51)) == null && root.InputHitTest(new Point(372,420)) == null;
        window.SetIslandHidden(false); window.ShowPanel("notes", true); window.HandleOutsidePress();
        var outsideClickClosesFocusedPanel = !window.IsExpanded;
        var child = new StackPanel { Width = 600, Height = 600 }; var scroll = new ScrollViewer { Content = child, Width = 180, Height = 180, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false };
        scroll.Measure(new Size(180, 180)); scroll.Arrange(new Rect(0, 0, 180, 180)); scroll.UpdateLayout();
        var horizontalHandled = Controls.ScrollRouting.Route(child, 60, true); scroll.UpdateLayout();
        var horizontalScrollMoves = horizontalHandled && scroll.HorizontalOffset > 0;
        var verticalHandled = Controls.ScrollRouting.Route(child, 80, false); scroll.UpdateLayout();
        var verticalScrollMoves = verticalHandled && scroll.VerticalOffset > 0;
        for (var i = 0; i < 100; i++) Controls.ScrollRouting.Route(child, .4, true);
        scroll.UpdateLayout(); var highResolutionWheelAccumulates = Math.Abs(scroll.HorizontalOffset - 100) < 1;
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        { for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++) { var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var item in Descendants(child)) yield return item; } }
        var horizontalBar = Descendants(scroll).OfType<System.Windows.Controls.Primitives.ScrollBar>().First(x => x.Orientation == Orientation.Horizontal);
        var pageForward = Descendants(horizontalBar).OfType<System.Windows.Controls.Primitives.RepeatButton>().Last();
        var beforeX = scroll.HorizontalOffset; var beforeY = scroll.VerticalOffset;
        ((System.Windows.Input.RoutedCommand)pageForward.Command).Execute(null, pageForward); scroll.UpdateLayout();
        var horizontalTrackPagesHorizontally = scroll.HorizontalOffset > beforeX && Math.Abs(scroll.VerticalOffset - beforeY) < .1;
        var motion = new MainWindow(c, true); motion.ShowPanel("home", false); motion.Collapse(); var motionRoot = motion.RenderRoot; motion.Content = null;
        using var motionSource = new HwndSource(new HwndSourceParameters("QuietIsland.MotionCheck") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 744, Height = 430 }); motionSource.RootVisual = motionRoot;
        var initialWidth = motion.NotchWidth; motion.PreviewTransition(true); await Task.Delay(95); var intermediate = motion.NotchWidth;
        await Task.Delay(540); var expansionAnimates = intermediate > initialWidth && intermediate < 744 && Math.Abs(motion.NotchWidth - 744) < .1;
        motion.PreviewTransition(false); await Task.Delay(75); var closingWidth = motion.NotchWidth;
        motion.PreviewTransition(true); await Task.Delay(600); var transitionReversalSettles = closingWidth > initialWidth && closingWidth < 744 && Math.Abs(motion.NotchWidth - 744) < .1;
        var forecast = new WeatherForecast(10, 8, 3, 5, 50, Enumerable.Range(0, 24).Select(i => new WeatherHour(DateTime.Today.AddHours(i).ToString("s"), 10, 0, 3)).ToList(), [], DateTimeOffset.Now);
        typeof(WeatherService).GetProperty(nameof(WeatherService.Current))!.SetValue(c.Weather, forecast);
        using var weather = new Views.WeatherView(c); weather.Measure(new Size(680, 340)); weather.Arrange(new Rect(0, 0, 680, 340)); weather.UpdateLayout();
        var hoursBefore = Descendants(weather).OfType<ScrollViewer>().First(x => x.Content is StackPanel p && p.Orientation == Orientation.Horizontal);
        hoursBefore.ScrollToHorizontalOffset(180); weather.UpdateLayout(); weather.Refresh(); weather.UpdateLayout();
        var hoursAfter = Descendants(weather).OfType<ScrollViewer>().First(x => x.Content is StackPanel p && p.Orientation == Orientation.Horizontal);
        var weatherPollPreservesHorizontalPosition = ReferenceEquals(hoursBefore, hoursAfter) && hoursAfter.HorizontalOffset >= 179;
        var selector = new TaskCompletionSource<CaptureResult?>(); var selectionCount = 0;
        var recordingWindow = new MainWindow(c, true) { SelectionForChecks = _ => { selectionCount++; return selector.Task; } };
        var firstStart = recordingWindow.ToggleRecordingAsync(); await recordingWindow.ToggleRecordingAsync();
        var recordingSelectionCannotReenter = selectionCount == 1 && !firstStart.IsCompleted;
        selector.SetResult(null); await firstStart;
        var originalMedia = c.Media.Current; var compactWindow = new MainWindow(c, true);
        typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(c.Media, new Models.MediaSnapshot("Трек", "", "", false, "fixture", 0, 0)); compactWindow.RefreshCompactForChecks(); var shortWidth = compactWindow.NotchWidth;
        typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(c.Media, new Models.MediaSnapshot("Длинное название композиции, которое должно расширить чёлку по содержимому", "Исполнитель", "", false, "fixture", 0, 0)); compactWindow.RefreshCompactForChecks();
        var compactGrowsToFitText = compactWindow.NotchWidth > shortWidth + 100;
        typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(c.Media, originalMedia);
        var outputs = new List<(string Id, string Name)> { ("fixture-speakers", "Динамики") }; var previousOutput = c.Settings.AudioOutputId;
        c.Settings.AudioOutputId = "fixture-speakers";
        using var home = new Views.HomeView(c, new MainWindow(c, true), () => outputs.ToArray());
        var beforeOutputCount = home.OutputChoiceCount; outputs.Add(("fixture-headphones", "Наушники")); home.RefreshOutputChoices();
        var cachedAudioChoicesRefresh = home.OutputChoiceCount == beforeOutputCount + 1 && home.OutputSelection == "fixture-speakers";
        c.Settings.AudioOutputId = previousOutput;
        var composition = new MainWindow(c, true); var compositionRoot = composition.RenderRoot; composition.Content = null;
        using var compositionSource = new HwndSource(new HwndSourceParameters("QuietIsland.CompositionCheck") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 744, Height = 430 }); compositionSource.RootVisual = compositionRoot;
        c.StartTimer(TimeSpan.FromMinutes(5));
        var fixtureCover = Diagnostics.Render(Controls.Icons.Tile("music", 80), 80, 80);
        typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(c.Media, new Models.MediaSnapshot("Композиция", "Исполнитель", "", true, "fixture", 20, 180, fixtureCover)); composition.RefreshCompactForChecks();
        Diagnostics.Render(compositionRoot, 744, 430);
        var clock = (TextBlock)composition.FindName("ClockText"); var compactTimer = (FrameworkElement)composition.FindName("CompactTimer"); var compactCover = (System.Windows.Controls.Image)composition.FindName("CompactCover");
        var clockRemainsWithTimerAndMusic = clock.Text.StartsWith(DateTime.Now.ToString("HH:mm")) && clock.Visibility == Visibility.Visible && compactTimer.Visibility == Visibility.Visible && ReferenceEquals(compactCover.Source, fixtureCover);
        var timerAppearsRightOfClock = compactTimer.TranslatePoint(new Point(), compositionRoot).X > clock.TranslatePoint(new Point(clock.ActualWidth, 0), compositionRoot).X;
        var compactHeightStableWithMusic = Math.Abs(composition.NotchHeight - c.Settings.CompactHeight) < .1;
        using var notes = new Views.NotesView(c); Diagnostics.Render(notes, 680, 340);
        var notesHaveCircularTimer = Descendants(notes).OfType<Controls.TimerRing>().Any(x => x.ActualWidth >= 60 && x.Progress > .9);
        c.State.Data.TimerEnd = null; typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(c.Media, originalMedia);
        var ownFile = Path.Combine(c.State.DirectoryPath, "menu-fixture.txt"); Directory.CreateDirectory(c.State.DirectoryPath); File.WriteAllText(ownFile, "Own UI fixture"); c.State.Data.Shelf.Add(ownFile);
        composition.ShowPanel("shelf", false); Diagnostics.Render(compositionRoot, 744, 430);
        var fileCard = Descendants(compositionRoot).OfType<FrameworkElement>().First(x => x.ContextMenu != null && Equals(x.Tag, "QuietIsland.FileTile")); var fileMenu = fileCard.ContextMenu!;
        var fileMenusHaveIcons = fileMenu.Items.OfType<MenuItem>().All(x => x.Icon is FrameworkElement);
        var menuEvent = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [fileCard, true], null)!;
        menuEvent.Handled = true; fileCard.RaiseEvent(menuEvent); fileMenu.PlacementTarget = fileCard; fileMenu.Opacity = 0; fileMenu.IsOpen = true;
        await Task.Delay(300); composition.PointerLeaveForChecks(3000); var fileMenuKeepsIslandOpen = composition.IsExpanded;
        fileMenu.IsOpen = false; await Task.Delay(250); composition.PointerLeaveForChecks(c.Settings.HoverCloseDelayMs + 10); var fasterHoverDismissal = !composition.IsExpanded;
        composition.ShowPanel("search", true); composition.PointerLeaveForChecks(3000); var keyboardSearchKeepsIslandOpen = composition.IsExpanded;
        Diagnostics.Render(compositionRoot, 744, 430); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var query = Descendants(compositionRoot).OfType<TextBox>().First(x => Equals(x.Tag, "Файлы, программы и прочее")); query.ApplyTemplate();
        var placeholder = (TextBlock)query.Template.FindName("Hint", query); var caret = query.GetRectFromCharacterIndex(0);
        var searchHintRightOfCaret = !caret.IsEmpty && placeholder.TranslatePoint(new Point(), query).X > caret.X && placeholder.TranslatePoint(new Point(), query).X - caret.X <= 8;
        var inputPanel = new StackPanel();
        var inputs = new[] { Controls.Ui.Input("Обычное поле"), Controls.Ui.Input("Поле с другим отступом"), Controls.Ui.Input("Крупный текст"), Controls.Ui.Input("Многострочное поле", multiline: true) };
        inputs[1].Padding = new Thickness(20, 8, 16, 8); inputs[2].FontSize = 18; inputs[3].Height = 90;
        foreach (var input in inputs) inputPanel.Children.Add(input);
        using var inputSource = new HwndSource(new HwndSourceParameters("QuietIsland.InputHintsCheck") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 500, Height = 300 });
        inputSource.RootVisual = inputPanel; Diagnostics.Render(inputPanel, 500, 300);
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var allInputHintsRightOfCaret = true; var inputHintsFollowText = true;
        foreach (var input in inputs)
        {
            var hint = (TextBlock)input.Template.FindName("Hint", input); var insertion = input.GetRectFromCharacterIndex(0);
            var gap = hint.TranslatePoint(new Point(), input).X - insertion.Right;
            allInputHintsRightOfCaret &= !insertion.IsEmpty && gap >= 2 && gap <= 8;
            input.Text = "Текст"; input.UpdateLayout(); inputHintsFollowText &= hint.Visibility == Visibility.Collapsed;
            input.Clear(); input.UpdateLayout(); inputHintsFollowText &= hint.Visibility == Visibility.Visible;
        }
        CaptureService.SavePng(Diagnostics.Render(inputPanel, 500, 300), Path.Combine(c.State.DirectoryPath, "input-placeholders.png"));
        using var generalSettings = new Views.SettingsView(c, composition);
        inputSource.RootVisual = generalSettings;
        generalSettings.SelectCategory("Общие"); Diagnostics.Render(generalSettings, 600, 500);
        var startupCheckboxes = Descendants(generalSettings).OfType<CheckBox>().Where(x => Equals(x.Content, "Запускать Quiet Island при входе в Windows")).ToArray();
        var startupInGeneralSettings = startupCheckboxes.Length == 1 && startupCheckboxes[0].IsVisible && startupCheckboxes[0].IsChecked == SystemService.AutoStartEnabled();
        CaptureService.SavePng(Diagnostics.Render(generalSettings, 600, 500), Path.Combine(c.State.DirectoryPath, "general-startup.png"));
        startupCheckboxes.Single().IsChecked = !SystemService.AutoStartEnabled();
        generalSettings.SelectCategory("Буфер и клавиши"); generalSettings.SelectCategory("Общие");
        var startupRefreshesFromWindows = startupCheckboxes.Single().IsChecked == SystemService.AutoStartEnabled();
        var editorMenusHaveOwnIcons = query.ContextMenu != null && query.ContextMenu.Items.OfType<MenuItem>().All(x => x.Icon is FrameworkElement) && Descendants(notes).OfType<RichTextBox>().All(x => x.ContextMenu?.Items.OfType<MenuItem>().All(item => item.Icon is FrameworkElement) == true);
        c.State.Data.Shelf.Remove(ownFile); File.Delete(ownFile); composition.Collapse();
        using var narrowContext = new IslandContext(Path.Combine(c.State.DirectoryPath, "appearance-bounds"));
        narrowContext.Settings.CompactHeight = 44; narrowContext.Settings.CompactFontSize = 16; narrowContext.Settings.MusicAmplitude = 12;
        typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(narrowContext.Media, new Models.MediaSnapshot("Композиция", "Исполнитель", "", true, "fixture", 20, 180, fixtureCover));
        var narrowWindow = new MainWindow(narrowContext, true); narrowWindow.RefreshCompactForChecks();
        var narrowRoot = narrowWindow.RenderRoot; narrowWindow.Content = null;
        using var narrowSource = new HwndSource(new HwndSourceParameters("QuietIsland.AppearanceBounds") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 744, Height = 430 }); narrowSource.RootVisual = narrowRoot;
        var fluid = (Controls.LiquidSurface)((Grid)narrowRoot).Children[0]; fluid.Update(narrowContext.Media);
        var envelope = (QuietIsland.Core.SpectrumEnvelope)typeof(Controls.LiquidSurface).GetField("envelope", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(fluid)!;
        for (var i = 0; i < 20; i++) envelope.Advance(Enumerable.Repeat(1f, 32).ToArray(), .1);
        typeof(Controls.LiquidSurface).GetField("level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(fluid, 1d);
        Diagnostics.Render(narrowRoot, 744, 430); fluid.Opacity = 0;
        var contentBitmap = Diagnostics.Render(narrowRoot, 744, 430); var contentPixels = new byte[744 * 430 * 4]; contentBitmap.CopyPixels(contentPixels, 744 * 4, 0); var outsideContent = 0;
        for (var y = 0; y < 72; y++) for (var x = 0; x < 744; x++)
        {
            var offset = (y * 744 + x) * 4;
            if (contentPixels[offset + 3] > 40 && Math.Max(contentPixels[offset], Math.Max(contentPixels[offset + 1], contentPixels[offset + 2])) > 70 && !fluid.ContainsSurface(new(x + .5, y + .5))) outsideContent++;
        }
        var appearanceExtremesKeepContentInside = outsideContent == 0;
        return new { aiDropPreserved, launcherDropPreserved, collapsedOpensShelf, hiddenTimerFires, hiddenReminderFires, pillHitArea, transparentAreaPassesThrough, outsideClickClosesFocusedPanel, horizontalScrollMoves, verticalScrollMoves, highResolutionWheelAccumulates, horizontalTrackPagesHorizontally, expansionAnimates, transitionReversalSettles, weatherPollPreservesHorizontalPosition, recordingSelectionCannotReenter, compactGrowsToFitText, cachedAudioChoicesRefresh, clockRemainsWithTimerAndMusic, timerAppearsRightOfClock, compactHeightStableWithMusic, notesHaveCircularTimer, fileMenusHaveIcons, fileMenuKeepsIslandOpen, fasterHoverDismissal, keyboardSearchKeepsIslandOpen, searchHintRightOfCaret, allInputHintsRightOfCaret, inputHintsFollowText, startupInGeneralSettings, startupRefreshesFromWindows, editorMenusHaveOwnIcons, appearanceExtremesKeepContentInside };
    }
}

