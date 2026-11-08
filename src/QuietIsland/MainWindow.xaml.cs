using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuietIsland.Controls;
using QuietIsland.Services;
using QuietIsland.Views;

namespace QuietIsland;

public partial class MainWindow : Window
{
    private readonly IslandContext c;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer hover = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly DispatcherTimer interaction = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly PopupGuard popupGuard;
    private bool keyboardHold;
    private HwndSource? hwnd;
    private bool expanded, pinned, pollBusy, starting, servicesStarted;
    private QuietIsland.Core.OverlayPlacement? placement;
    private bool? clipped;
    private string selected = "home";
    private DateTime lastInside = DateTime.Now, lastPoll, lastActivity, lastWeather;
    private DateTime lastCollapsed = DateTime.MinValue;
    private string toast = "";
    private DateTime toastUntil;
    private IslandView? view;
    private HomeView? homeCache;
    private string homeCacheKey = "";
    private readonly Dictionary<string, Button> navigation = new();
    private readonly List<int> registered = [];
    private System.Windows.Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayArtwork;
    private readonly bool snapshot;
    private bool hiddenByUser;
    private readonly ScrollRouting scrollRouting;
    private readonly NotchMotion motion;
    private readonly RectangleGeometry panelClip = new();
    private readonly TranslateTransform panelShift = new();
    private readonly RectangleGeometry compactClip = new(), compactTextClip = new();
    private readonly TranslateTransform compactShift = new(), compactMetaShift = new();
    private readonly OverflowLabel compactTitleOverflow, compactSubtitleOverflow;
    private double compactMetaWidth, compactPrefixWidth;
    private double compactWidth = 190;
    private double compactHeight = 52;
    private string compactMeasureKey = "", compactIcon = "";
    private int? compactWeatherCode;
    private string inputState = "";
    private readonly TranslateTransform inputShift = new();
    private OutsidePointer? outsidePointer;
    private Windows.SettingsWindow? settingsWindow;
    private int transition;
    private string? lastRecordingError;
    private bool recordCommandBusy, selectionBusy;
    internal Func<bool, Task<CaptureResult?>>? SelectionForChecks { get; set; }
    private readonly Dictionary<string, string> navTitles = new();
    internal bool IsExpanded => expanded;
    internal bool NativeMotionProbe { get; set; }
    internal int NativePlacementChanges { get; private set; }
    internal double NotchWidth => Fluid.VisualWidth;
    internal double NotchHeight => Fluid.VisualHeight;
    internal void RefreshCompactForChecks() => UpdateCompact();
    internal (double Opacity, double Y) PanelVisualForChecks => (Panel.Opacity, panelShift.Y);
    internal bool ContainsSurfaceForChecks(Point point) => Fluid.ContainsSurface(point);
    public MainWindow(IslandContext context, bool forSnapshot = false)
    {
        c = context; snapshot = forSnapshot; InitializeComponent();
        CompactLines.Children.Clear();
        compactTitleOverflow = new(CompactTitle); compactSubtitleOverflow = new(CompactSubtitle);
        compactTitleOverflow.HorizontalAlignment = compactSubtitleOverflow.HorizontalAlignment = HorizontalAlignment.Left;
        CompactLines.Children.Add(compactTitleOverflow); CompactLines.Children.Add(compactSubtitleOverflow);
        motion = new(Fluid); Panel.Clip = panelClip; Panel.RenderTransform = panelShift;
        Compact.Clip = compactClip; Compact.RenderTransform = compactShift; CompactLines.Clip = compactTextClip; CompactMeta.RenderTransform = compactMetaShift;
        popupGuard = new(Root); InputBadge.RenderTransform = inputShift; CompactCoverFallback.Content = Icons.Colored("music", 17);
        Fluid.DimensionsChanged += UpdatePanelClip;
        scrollRouting = new(this);
        foreach (var (key, title) in new[] { ("home", "Главная"), ("clipboard", "Буфер"), ("shelf", "Полка"), ("tools", "Текст"), ("ai", "Чат"), ("notes", "Заметки"), ("weather", "Погода"), ("control", "Управление") })
        {
            navTitles[key] = title;
            var button = Ui.Button("", () => ShowPanel(key, false), tip: title); button.Content = Icons.Make(key); button.Padding = new Thickness(8, 7, 8, 7); button.Margin = new Thickness(0, 0, 3, 0); button.Background = Brushes.Transparent;
            navigation[key] = button; Nav.Children.Add(button);
        }
        PinButton.Content = Icons.Make("pin"); CloseButton.Content = Icons.Make("close"); DictationButton.Content = Icons.Make("mic");
        InitializeTimerNotice(); ConfigureHeader();
        Root.MouseEnter += (_, _) => { if (!expanded && !HasAlarm && !overlayBlocked && !DragHeld) hover.Start(); };
        Root.MouseLeave += (_, _) => hover.Stop();
        Compact.MouseLeftButtonUp += async (_, e) => { e.Handled = true; if (c.Recording.IsRecording) await ToggleRecordingAsync(); else ReopenPanel(); };
        Fluid.MouseLeftButtonUp += async (_, _) => { if (!expanded) { if (c.Recording.IsRecording) await ToggleRecordingAsync(); else ReopenPanel(); } };
        Compact.MouseRightButtonUp += (_, _) => ShowTrayMenu();
        Fluid.MouseRightButtonUp += (_, _) => { if (!expanded) ShowTrayMenu(); };
        Root.MouseWheel += (_, e) => { if (expanded) return; try { c.System.Volume += e.Delta / 120f * .03f; c.Notify($"Громкость {(int)(c.System.Volume * 100)}%"); e.Handled = true; } catch (Exception error) { c.Error(error); } };
        hover.Tick += (_, _) => { hover.Stop(); if (Root.IsMouseOver && !HasAlarm && !overlayBlocked && !DragHeld) ReopenPanel(); };
        CloseButton.Click += (_, _) => Collapse();
        PinButton.Click += (_, _) => { pinned = !pinned; PinButton.Background = pinned ? Ui.Brush("Hover") : Brushes.Transparent; };
        DictationButton.Click += async (_, _) => await DictateAsync();
        PreviewKeyDown += (_, e) => { popupGuard.Observe(e.OriginalSource as DependencyObject); if (e.Key == Key.Escape) { Collapse(); e.Handled = true; } else if (!Root.IsMouseOver && Keyboard.FocusedElement is TextBoxBase) keyboardHold = true; };
        PreviewMouseDown += (_, e) =>
        {
            var element = e.OriginalSource as DependencyObject;
            popupGuard.Observe(element); keyboardHold = false;
            while (element != null && element is not TextBoxBase && element is not PasswordBox && element is not ComboBox && element is not ListBoxItem && element is not FrameworkElement { Tag: "QuietIsland.FileTile" }) element = ScrollRouting.Parent(element);
            if (element != null && (!snapshot || mirrorOwner != null)) { Native.ConfigureOverlay(this, true, false, c.Settings.HideInCapture); Activate(); }
        };
        AllowDrop = true;
        InitializeDragRetention();
        SubscribeContext();
        SourceInitialized += (_, _) => InitializeNative();
        timer.Tick += async (_, _) => { if (mirrorOwner != null) MirrorTick(); else await TickAsync(); };
        interaction.Tick += (_, _) => TickInteraction();
        Loaded += async (_, _) =>
        {
            if (mirrorOwner != null) { ApplySettings(); timer.Start(); interaction.Start(); return; }
            if (snapshot || servicesStarted) return;
            servicesStarted = true;
            ApplySettings(); timer.Start(); interaction.Start();
            starting = true;
            try { await c.StartAsync(); } catch (Exception e) { c.Error(e); }
            finally { starting = false; view?.Refresh(); }
        };
        Closing += (_, e) =>
        {
            if (mirrorOwner == null && (c.Recording.IsStarting || c.Recording.IsRecording || c.Recording.IsFinalizing)) { e.Cancel = true; c.Notify("Сначала остановите запись: " + GlobalShortcuts.Label(c.Settings, "recording")); return; }
            IsClosing = true; printScreen?.Dispose();
            UnsubscribeContext(); CloseMirrors();
            c.Updates.Changed -= UpdatesChanged;
            c.State.Changed -= AppStateChanged;
            timerSound.Dispose();
            timer.Stop(); interaction.Stop(); hover.Stop(); motion.Dispose(); ReleaseView(); homeCache?.Dispose(); tray?.Dispose(); trayArtwork?.Dispose(); outsidePointer?.Dispose(); scrollRouting.Dispose(); settingsWindow?.Close();
            if (hwnd != null) { foreach (var id in registered) Native.UnregisterHotKey(hwnd.Handle, id); Native.RemoveClipboardFormatListener(hwnd.Handle); }
            if (!snapshot) c.Dispose();
        };
        Closed += (_, _) => { if (!snapshot) System.Windows.Application.Current.Shutdown(); };
        ApplyTheme(); Fluid.ApplyAppearance(c.Settings); Compact.Height = c.Settings.CompactHeight - 8; ClockText.FontSize = c.Settings.CompactFontSize; CompactTitle.FontSize = c.Settings.CompactFontSize - 1; UpdateCompact(); ApplyVisualOpacity(); if (!snapshot) PrepareHome();
    }
    private void InitializeNative()
    {
        if (snapshot && mirrorOwner == null) return;
        hwnd = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); hwnd.AddHook(Hook);
        try
        {
            outsidePointer = new();
            outsidePointer.Pressed += point => Dispatcher.BeginInvoke(() =>
            {
                if (expanded && !DragHeld && !OutsidePointer.IsOwnWindowAt(point)) HandleOutsidePress();
            });
        }
        catch (Exception e) { Log.Write("Outside pointer", e); }
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var process);
            if (expanded && !DragHeld && !popupGuard.IsOpen && !Windows.Dialogs.IsOpen && process != Environment.ProcessId) HandleOutsidePress();
        });
        if (mirrorOwner != null) { Native.ConfigureOverlay(this, false, c.Settings.ClickThrough, c.Settings.HideInCapture); return; }
        Native.AddClipboardFormatListener(hwnd.Handle);
        try { printScreen = new(); printScreen.Requested += id => Dispatcher.BeginInvoke(() => InvokeShortcut(id)); } catch (Exception e) { Log.Write("Print Screen", e); }
        RegisterHotkeys();
        Native.ConfigureOverlay(this, false, c.Settings.ClickThrough, c.Settings.HideInCapture);
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/island.ico"))!.Stream) trayArtwork = new System.Drawing.Icon(stream);
        tray = new System.Windows.Forms.NotifyIcon { Icon = trayArtwork, Visible = true, Text = "Quiet Island" };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettings);
        tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(() => ShowSettingsPage("Обновления"));
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Открыть поиск", null, (_, _) => Dispatcher.Invoke(() => { hiddenByUser = false; Show(); ShowPanel("search", true); }));
        menu.Items.Add("Настройки…", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        menu.Items.Add("Проверить обновления…", null, (_, _) => Dispatcher.InvokeAsync(async () => await CheckForUpdatesAsync()));
        menu.Items.Add("Запись экрана / остановить", null, (_, _) => Dispatcher.InvokeAsync(async () => await ToggleRecordingAsync()));
        menu.Items.Add("Показать / скрыть", null, (_, _) => Dispatcher.Invoke(() => SetIslandHidden(!hiddenByUser)));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(() => { if (c.Recording.IsStarting || c.Recording.IsRecording || c.Recording.IsFinalizing) c.Notify("Сначала остановите запись"); else { Close(); System.Windows.Application.Current.Shutdown(); } }));
        tray.ContextMenuStrip = menu;
    }
    private void ShowTrayMenu() { if (tray?.ContextMenuStrip is { } menu) menu.Show(System.Windows.Forms.Cursor.Position); }
    internal void HandleDragEnter(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && !data.GetDataPresent("QuietIsland.Launcher"))
        { incomingFileDrag = true; lastDragOver = lastInside = DateTime.Now; if (!expanded) ShowPanel("shelf", false); }
    }
    internal string SelectedTab => selected;
    public void SetIslandHidden(bool hidden)
    {
        hiddenByUser = hidden;
        if (!snapshot) { if (hidden) Hide(); else Show(); }
        foreach (var mirror in mirrors.Values) { mirror.hiddenByUser = hidden; if (hidden) mirror.Hide(); else mirror.Show(); }
        if (!snapshot) UpdateRuntimeVisibility(DateTime.Now);
    }
    private IntPtr Hook(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == Native.WM_CLIPBOARDUPDATE) c.Clipboard.Capture();
        if (message != Native.WM_HOTKEY) return IntPtr.Zero;
        handled = true;
        InvokeShortcut(w.ToInt32());
        return IntPtr.Zero;
    }
    private void InvokeShortcut(int id)
    {
        switch (id)
        {
            case 1: hiddenByUser = false; Show(); ShowPanel("search", true); break;
            case 2: ShowPanel("clipboard", false); break;
            case 3: ShowPanel("notes", true); break;
            case 4: _ = ScreenshotAsync("shot"); break;
            case 5: _ = ToggleRecordingAsync(); break;
            case 6: _ = DictateAsync(); break;
            case 7: _ = ScreenshotDesktopAsync(); break;
            case 8: _ = ScreenshotAsync("shot"); break;
            case 9: _ = ScreenshotAsync("color"); break;
            case 10: _ = ScreenshotAsync("ocr"); break;
            case 11: _ = ToggleRecordingAsync(fullScreen: true); break;
            default:
                var index = id - 100;
                if (index >= 0 && index < c.State.Data.Launcher.Count) { try { SystemService.Open(c.State.Data.Launcher[index].Target); } catch (Exception e) { c.Error(e); } }
                break;
        }
    }
    private readonly Dictionary<string, string> shortcutErrors = new();
    public IReadOnlyDictionary<string, string> ShortcutErrors => shortcutErrors;
    internal void InvalidateHotkeys() => hotkeyKey = "";
    private void RegisterHotkeys()
    {
        if (hwnd == null || mirrorOwner != null) return;
        var bindings = GlobalShortcuts.Bindings(c.Settings, c.State.Data.Launcher);
        var signature = string.Join(";", GlobalShortcuts.Definitions.Select(x => x.Key + "=" + bindings.GetValueOrDefault(x.Key, ""))) + "|" + string.Join(";", c.State.Data.Launcher.Select(x => x.Target + ":" + x.Hotkey));
        if (signature == hotkeyKey) return; hotkeyKey = signature;
        foreach (var id in registered) Native.UnregisterHotKey(hwnd.Handle, id); registered.Clear();
        shortcutErrors.Clear(); var seen = new HashSet<ShortcutChord>(); var printBindings = new Dictionary<uint, int>();
        void Register(int id, string key, string label, string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) return;
            if (!ShortcutChord.TryParse(shortcut, out var chord)) { shortcutErrors[key] = label + ": проверьте формат сочетания"; return; }
            if (!seen.Add(chord)) { shortcutErrors[key] = label + ": сочетание уже назначено другому действию"; return; }
            if (chord.VirtualKey == 0x2C && printScreen != null) printBindings.Add(chord.Modifiers, id);
            else if (Native.RegisterHotKey(hwnd.Handle, id, chord.Modifiers | 0x4000, chord.VirtualKey)) registered.Add(id);
            else shortcutErrors[key] = label + ": " + shortcut + " занято или недоступно. Выберите другое сочетание в настройках.";
        }
        foreach (var definition in GlobalShortcuts.Definitions)
            Register(definition.Id, definition.Key, definition.Title, bindings.GetValueOrDefault(definition.Key, ""));
        for (var i = 0; i < c.State.Data.Launcher.Count; i++)
        {
            var item = c.State.Data.Launcher[i]; Register(100 + i, "launcher:" + i, item.Name, item.Hotkey);
        }
        printScreen?.SetBindings(printBindings);
        HotkeyLabel.Text = GlobalShortcuts.Label(c.Settings, "search");
        if (shortcutErrors.Count > 0) c.Notify(string.Join("\n", shortcutErrors.Values));
    }
    public void ShowPanel(string tab, bool focus)
    {
        if (tab == "media") tab = "home";
        if (tab.StartsWith("settings", StringComparison.Ordinal))
        {
            ShowSettingsPage(tab switch { "settings:ai" => "Искусственный интеллект", "settings:recording" => "Запись и звук", "settings:speech" => "Диктовка", _ => null }); return;
        }
        if (HiddenForCapture)
        {
            if (focus && c.Recording.IsRecording)
                tray?.ShowBalloonTip(4000, "Чёлка скрыта во время записи", "Остановка: " + GlobalShortcuts.Label(c.Settings, "recording") + " или меню значка в трее", System.Windows.Forms.ToolTipIcon.Info);
            return;
        }
        if (!snapshot && overlayBlocked && !focus && !DragHeld) return;
        hiddenByUser = false;
        if (focus) { fullscreenOverride = true; overlayBlocked = false; }
        if (focus) keyboardHold = !Root.IsMouseOver;
        if (!snapshot)
        {
            var foreground = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(foreground, out var pid);
            if (pid != Environment.ProcessId && foreground != IntPtr.Zero) c.PasteTarget = foreground;
        }
        if (selected != tab || view == null)
        {
            ReleaseView(); selected = tab;
            view = tab switch
            {
                "home" => PrepareHome(), "media" => new MediaView(c), "clipboard" => new ClipboardView(c), "shelf" => new ShelfView(c), "notes" => new NotesView(c),
                "tools" => new ToolsView(c, this), "weather" => new WeatherView(c), "ai" => new AiView(c), "control" => new ControlView(c),
                "activity" => new ActivityView(c), _ => new SearchView(c)
            };
            Body.Content = view;
            if (!snapshot && IsLoaded) Body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160 / c.Settings.AnimationSpeed)));
        }
        var wasExpanded = expanded;
        var reversingContent = !wasExpanded && motion.Active && Panel.Visibility == Visibility.Visible;
        expanded = true; Panel.Visibility = Visibility.Visible; Panel.IsHitTestVisible = true;
        UpdateNavigation();
        if (!wasExpanded && !reversingContent) { Panel.BeginAnimation(OpacityProperty, null); Panel.Opacity = 0; }
        else Panel.Opacity = Math.Clamp(c.Settings.PanelOpacity, .45, 1);
        lastInside = DateTime.Now;
        var height = ExpandedShellHeight;
        Root.Height = Math.Max(560, height + 12);
        Panel.Height = height - 44;
        if (!wasExpanded || Math.Abs(Fluid.VisualHeight - height) > .1) AnimateShell(true, preserveContent: reversingContent);
        if (!snapshot || mirrorOwner != null)
        {
            Show(); ResizeAndPosition(); var visibleFocus = focus && !HiddenForCapture;
            Native.ConfigureOverlay(this, visibleFocus, false, c.Settings.HideInCapture);
            if (visibleFocus) { Activate(); Dispatcher.BeginInvoke(() => { if (!IsClosing && !HiddenForCapture) view?.FocusInput(); }, DispatcherPriority.Input); }
        }
        view.Refresh();
        lastPoll = DateTime.MinValue; RefreshRuntime(false);
    }
    public void Collapse()
    {
        if (!expanded) return;
        lastCollapsed = DateTime.UtcNow;
        expanded = pinned = keyboardHold = fullscreenOverride = false; PinButton.Background = Brushes.Transparent; Panel.IsHitTestVisible = false;
        Compact.Visibility = Visibility.Visible; Compact.Opacity = Math.Clamp(c.Settings.PillOpacity, .45, 1);
        AnimateShell(false);
        if (!snapshot || mirrorOwner != null)
        {
            Native.ConfigureOverlay(this, false, c.Settings.ClickThrough && !HasAlarm, c.Settings.HideInCapture);
            if (Native.GetForegroundWindow() == new WindowInteropHelper(this).Handle && Native.IsWindow(c.PasteTarget)) Native.SetForegroundWindow(c.PasteTarget);
        }
        UpdateCompact();
    }
    internal void HandleOutsidePress() => Collapse();
    private void ReopenPanel() => ShowPanel(HasAlarm ? "notes" : !expanded && (DateTime.UtcNow - lastCollapsed).TotalSeconds >= 5 ? "home" : selected, false);
    private double ExpandedShellHeight => (selected == "home" ? (homeCache?.PreferredBodyHeight ?? 298) + 90 : 430) + (HasAlarm ? 50 : 0);
    private void ResizeExpandedContent()
    {
        if (!expanded) return;
        var height = ExpandedShellHeight;
        Root.Height = Math.Max(560, height + 12);
        if (Math.Abs(Panel.Height - (height - 44)) < .1) return;
        Panel.Height = height - 44; AnimateShell(true, preserveContent: true);
    }
    public void ShowSettings() => ShowSettingsPage();
    private void ShowSettingsPage(string? category = null)
    {
        if (mirrorOwner != null) { mirrorOwner.ShowSettingsPage(category); return; }
        Collapse();
        if (settingsWindow == null)
        {
            settingsWindow = new Windows.SettingsWindow(c, this);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
        }
        if (category != null) settingsWindow.SelectCategory(category);
        settingsWindow.Show(); settingsWindow.Activate();
    }
    internal void PreviewTransition(bool open) { expanded = open; AnimateShell(open, true); }
    private void AnimateShell(bool open, bool force = false, bool preserveContent = false)
    {
        TimeSpan Duration(double ms) => TimeSpan.FromMilliseconds(ms / c.Settings.AnimationSpeed);
        var revision = ++transition;
        var width = open ? 744d : compactWidth; var height = open ? ExpandedShellHeight : compactHeight;
        void Finished()
        {
            if (revision != transition) return;
            if (!open) { Panel.Visibility = Visibility.Collapsed; ReleaseView(); Body.Content = null; }
            else Compact.Visibility = Visibility.Collapsed;
        }
        var animate = force || (!snapshot || mirrorOwner != null || NativeMotionProbe) && IsLoaded && !overlayBlocked;
        motion.To(width, height, animate, Finished);
        if (!animate)
        {
            Panel.BeginAnimation(OpacityProperty, null); Compact.BeginAnimation(OpacityProperty, null); Fluid.BeginAnimation(OpacityProperty, null); panelShift.BeginAnimation(TranslateTransform.YProperty, null);
            Panel.Opacity = open ? c.Settings.PanelOpacity : 0; Compact.Opacity = open ? 0 : c.Settings.PillOpacity; Fluid.Opacity = open ? c.Settings.PanelOpacity : c.Settings.PillOpacity; panelShift.Y = 0; return;
        }
        Compact.Visibility = Visibility.Visible;
        Fluid.BeginAnimation(OpacityProperty, new DoubleAnimation(Fluid.Opacity, open ? c.Settings.PanelOpacity : c.Settings.PillOpacity, Duration(180)));
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(Panel.Opacity, open ? c.Settings.PanelOpacity : 0, Duration(open ? 240 : 100)) { BeginTime = Duration(open && !preserveContent ? 65 : 0) });
        Compact.BeginAnimation(OpacityProperty, new DoubleAnimation(Compact.Opacity, open ? 0 : c.Settings.PillOpacity, Duration(open ? 70 : 170)) { BeginTime = Duration(open ? 0 : 100) });
        panelShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(open && !preserveContent ? 9 : panelShift.Y, open ? 0 : -5, Duration(open ? 300 : 100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    private void UpdatePanelClip()
    {
        var width = Math.Max(0, Fluid.VisualWidth - 34); var height = Math.Max(0, Fluid.VisualHeight - 44);
        panelClip.Rect = new Rect((Panel.Width - width) / 2, 0, width, height);
        compactShift.X = (compactWidth - Fluid.VisualWidth) / 2;
        compactMetaShift.X = Fluid.VisualWidth - compactWidth;
        compactClip.Rect = new Rect(0, 0, width, Math.Min(Compact.Height, Math.Max(0, Fluid.VisualHeight - 8)));
        compactTextClip.Rect = new Rect(0, 0, Math.Max(0, width - 28 - compactPrefixWidth - compactMetaWidth), Compact.Height);
    }
    private void ReleaseView() { if (!ReferenceEquals(view, homeCache)) view?.Dispose(); view = null; }
    internal HomeView PrepareHome()
    {
        var key = c.Settings.Theme + "|" + c.Settings.AccentColor + "|" + c.Settings.SurfaceColor + "|" + c.Settings.AudioOutputId + "|" + string.Join(";", c.State.Data.Launcher.Select(x => x.Name + "=" + x.Target));
        if (homeCache != null && key == homeCacheKey) return homeCache;
        var replacingActive = ReferenceEquals(view, homeCache) && homeCache != null;
        homeCache?.Dispose(); homeCacheKey = key; homeCache = new(c, this);
        homeCache.PreferredBodyHeightChanged += ResizeExpandedContent;
        // Prepare templates before a pointer-triggered transition, once per changed configuration.
        homeCache.Measure(new Size(680, homeCache.PreferredBodyHeight)); homeCache.Arrange(new Rect(0, 0, 680, homeCache.PreferredBodyHeight)); homeCache.UpdateLayout();
        if (replacingActive) { view = homeCache; Body.Content = homeCache; ResizeExpandedContent(); }
        return homeCache;
    }
    public void ApplySettings()
    {
        c.Settings.NormalizeAppearance(); hover.Interval = TimeSpan.FromMilliseconds(Math.Max(1, c.Settings.HoverOpenDelayMs)); ApplyTheme(); Scale.ScaleX = Scale.ScaleY = c.Settings.Scale; RegisterHotkeys(); ConfigureHeader();
        motion.Speed = c.Settings.AnimationSpeed; Fluid.ApplyAppearance(c.Settings);
        Compact.Height = c.Settings.CompactHeight - 8; ClockText.FontSize = c.Settings.CompactFontSize; CompactTitle.FontSize = c.Settings.CompactFontSize - 1;
        compactMeasureKey = ""; UpdateCompact();
        ApplyVisualOpacity();
        if (!expanded) motion.To(compactWidth, compactHeight, (!snapshot || NativeMotionProbe) && IsLoaded, keepCompletion: true);
        if (!snapshot || mirrorOwner != null) PrepareHome();
        if (!snapshot || mirrorOwner != null || NativeMotionProbe) { Native.ConfigureOverlay(this, expanded, !expanded && c.Settings.ClickThrough && !HasAlarm, c.Settings.HideInCapture); if (snapshot && mirrorOwner == null) ResizeAndPosition(); else UpdateRuntimeVisibility(DateTime.Now); }
        if (!snapshot) ReconcileMirrors();
    }
    private void ApplyTheme()
    {
        var light = c.Settings.Theme == "Light" || c.Settings.Theme == "System" && SystemService.IsSystemLight();
        var nextKey = light + "|" + c.Settings.SurfaceColor + "|" + c.Settings.AccentColor;
        if (nextKey == themeKey) return; themeKey = nextKey;
        var colors = light ? new Dictionary<string, string> { ["Surface"]="#F6F7F8", ["Card"]="#E9ECEF", ["Hover"]="#DCE2E6", ["Line"]="#D4DADE", ["Text"]="#14191F", ["Muted"]="#5D6874", ["Accent"]="#1F765A", ["AccentText"]="#FFFFFF" }
            : new Dictionary<string, string> { ["Surface"]="#000000", ["Card"]="#151619", ["Hover"]="#282A30", ["Line"]="#303239", ["Text"]="#F5F5F7", ["Muted"]="#93949D", ["Accent"]="#FF8B81", ["AccentText"]="#171319" };
        if (!light) colors["Surface"] = c.Settings.SurfaceColor;
        colors["Accent"] = c.Settings.AccentColor;
        var accent = (Color)ColorConverter.ConvertFromString(c.Settings.AccentColor);
        colors["AccentText"] = accent.R * .299 + accent.G * .587 + accent.B * .114 > 150 ? "#171319" : "#FFFFFF";
        foreach (var (key, value) in colors) System.Windows.Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        System.Windows.Application.Current.Resources["SettingsSurface"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#F6F7F8" : "#101010"));
        System.Windows.Application.Current.Resources["SettingsSidebar"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#E9ECEF" : "#0A0A0A"));
        settingsWindow?.ApplyTheme();
        Fluid.InvalidateVisual();
    }
    private System.Windows.Forms.Screen Screen => ResolveScreen();
    private void ResizeAndPosition(bool hidden = false)
    {
        hidden |= hiddenByUser || HiddenForCapture;
        var bounds = Screen.Bounds; var dpi = Native.MonitorDpi(Screen);
        var next = QuietIsland.Core.OverlayLayout.CalculateSize(bounds.Left, bounds.Top, bounds.Width, bounds.Height, dpi.X, dpi.Y, c.Settings.Scale, c.Settings.TopOffset, 744, Root.Height);
        Scale.ScaleX = Scale.ScaleY = next.Scale;
        Width = next.Width / dpi.X; Height = next.Height / dpi.Y;
        var handle = new WindowInteropHelper(this).Handle;
        if (placement != next) { Native.SetWindowPos(handle, new IntPtr(-1), next.X, next.Y, next.Width, next.Height, 0x0010 | 0x0040); placement = next; NativePlacementChanges++; }
        // Clipping keeps a hidden island off adjacent monitors and removes its input area entirely.
        if (clipped != hidden) { Native.ClipOverlay(handle, hidden); clipped = hidden; }
    }
    internal async Task TickAsync()
    {
        var now = DateTime.Now;
        UpdateRuntimeVisibility(now);
        var foreground = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(foreground, out var pid);
        if (pid != Environment.ProcessId && foreground != IntPtr.Zero) c.PasteTarget = foreground;
        UpdateCompact();
        if (c.State.Data.TimerEnd is { } end && end <= DateTimeOffset.Now && !c.State.Data.TimerPaused)
        { c.State.Data.TimerEnd = null; c.State.Touch(); Alarm("Таймер завершён", null); }
        foreach (var reminder in c.State.Data.Reminders.Where(x => !x.Completed && x.Due <= DateTimeOffset.Now).ToArray())
        { var next = reminder.NextAfter(DateTimeOffset.Now); if (next == DateTimeOffset.MaxValue) reminder.Completed = true; else reminder.Due = next; c.State.Touch(); Alarm(reminder.Title, reminder); }
        if ((now - lastActivity).TotalSeconds >= (economyMode || overlayBlocked ? 20 : 4))
        { lastActivity = now; try { c.Activity.Poll(); await c.Notifications.PollAsync(); } catch (Exception e) { Log.Write("Activity", e); } if (view is ActivityView or ControlView) view.Refresh(); }
        if ((now - lastWeather).TotalMinutes >= 15) { lastWeather = now; _ = c.Weather.RefreshAsync(); }
        if ((now - lastPoll).TotalSeconds < (runtimeHidden ? 5 : economyMode && !expanded ? 3 : 1) || pollBusy || starting) return;
        pollBusy = true; lastPoll = now;
        try { await c.Media.PollAsync(); if (view is not HomeView) view?.Refresh(); } catch (Exception e) { Log.Write("Refresh", e); } finally { pollBusy = false; }
    }
    private void TickInteraction()
    {
        if (incomingFileDrag && (DateTime.Now - lastDragOver).TotalMilliseconds > 400 && (Native.GetAsyncKeyState(1) & 0x8000) == 0 && (Native.GetAsyncKeyState(2) & 0x8000) == 0) { incomingFileDrag = false; lastInside = DateTime.Now; }
        var oldInput = (c.Activity.Layout, c.Activity.CapsLock);
        c.Activity.PollInputState();
        var inside = Root.IsMouseOver;
        if (inside) { lastInside = DateTime.Now; keyboardHold = false; }
        if (popupGuard.IsOpen) lastInside = DateTime.Now;
        DismissAfterLeave(inside, (DateTime.Now - lastInside).TotalMilliseconds);
        if (oldInput != (c.Activity.Layout, c.Activity.CapsLock) || (DateTime.Now - lastCompactSecond).TotalSeconds >= 1) { lastCompactSecond = DateTime.Now; UpdateCompact(); }
    }
    private void DismissAfterLeave(bool inside, double elapsedMs)
    {
        if (Windows.Dialogs.IsOpen) lastInside = DateTime.Now;
        if (QuietIsland.Core.OverlayBehavior.ShouldDismiss(expanded, pinned, inside, popupGuard.IsOpen || DragHeld || Windows.Dialogs.IsOpen, keyboardHold, elapsedMs, c.Settings.HoverCloseDelayMs)) Collapse();
    }
    internal void PointerLeaveForChecks(double elapsedMs) => DismissAfterLeave(false, elapsedMs);
    private void Alarm(string title, QuietIsland.Core.Reminder? reminder)
    {
        if (reminder == null) { TimerFinished(title); return; }
        ReminderFinished(reminder);
    }
    private void UpdateCompact()
    {
        var media = c.Media.Current;
        var timerEnd = c.State.Data.TimerEnd;
        var message = DateTime.Now < toastUntil ? toast : "";
        var icon = c.Speech.IsListening ? "mic" : c.Recording.IsRecording ? "record" : c.Agents.Broker.Pending.Count > 0 ? "activity" : "";
        if (compactIcon != icon) { compactIcon = icon; CompactGlyph.Content = icon.Length > 0 ? Icons.Colored(icon, 15) : null; CompactGlyph.Visibility = icon.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }
        ClockText.Text = ClockLabel(DateTime.Now);
        PanelClock.Text = ClockText.Text;
        var remaining = timerEnd is { } end ? Math.Max(0, c.State.Data.TimerPaused ? c.State.Data.TimerRemaining : (end - DateTimeOffset.Now).TotalSeconds) : 0;
        CompactTimer.Visibility = timerEnd != null ? Visibility.Visible : Visibility.Collapsed;
        CompactTimerText.Text = Ui.Time(remaining); CompactTimerRing.Paused = c.State.Data.TimerPaused;
        CompactTimerRing.Progress = remaining / Math.Max(1, c.State.Data.TimerDuration > 0 ? c.State.Data.TimerDuration : remaining);
        var coverVisible = media.HasMedia && c.Settings.ShowMusicCover;
        CompactCoverHost.Visibility = coverVisible ? Visibility.Visible : Visibility.Collapsed;
        if (!ReferenceEquals(CompactCover.Source, media.Cover)) CompactCover.Source = media.Cover;
        CompactCoverFallback.Visibility = media.Cover == null ? Visibility.Visible : Visibility.Collapsed;
        var mediaTitleVisible = media.HasMedia && c.Settings.ShowSongTitle && message.Length == 0 && !timerAlarm
            && !c.Recording.IsStarting && !c.Recording.IsFinalizing && !c.Recording.IsRecording && !c.Speech.IsListening;
        CompactTitle.Text = message.Length > 0 ? message : timerAlarm ? "Таймер завершён" : c.Recording.IsStarting ? "Запускаю запись…" : c.Recording.IsFinalizing ? "Сохраняю запись…" : c.Recording.IsRecording ? "Запись " + Ui.Time((DateTimeOffset.Now - c.Recording.Started).TotalSeconds) : c.Speech.IsListening ? "Слушаю…" : mediaTitleVisible ? media.Title : "";
        CompactSubtitle.Text = timerAlarm && message.Length == 0 ? "Нажмите, чтобы открыть" : mediaTitleVisible ? media.Artist : "";
        CompactSubtitle.Visibility = CompactSubtitle.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        compactSubtitleOverflow.Visibility = CompactSubtitle.Visibility;
        CompactLines.Visibility = CompactTitle.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CompactWeather.Text = c.Settings.ShowWeather && c.Weather.Current is { } weather ? $"{weather.Temperature:0}°" : "";
        CompactWeatherGroup.Visibility = CompactWeather.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (CompactWeather.Text.Length > 0 && c.Weather.Current is { } forecast && compactWeatherCode != forecast.Code)
        {
            compactWeatherCode = forecast.Code; CompactWeatherGlyph.Content = Icons.Weather(forecast.Code, 18);
        }
        InputBadge.Visibility = c.Settings.ShowInputStatus ? Visibility.Visible : Visibility.Collapsed;
        var nextInput = c.Activity.Layout + (c.Activity.CapsLock ? " CAPS" : "");
        if (nextInput != inputState)
        {
            var animateInput = inputState.Length > 0 && c.Settings.ShowInputStatus && (!snapshot || NativeMotionProbe) && IsLoaded;
            inputState = nextInput; InputLayout.Text = c.Activity.Layout; InputCaps.Visibility = c.Activity.CapsLock ? Visibility.Visible : Visibility.Collapsed;
            if (animateInput)
            {
                InputBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(.2, 1, TimeSpan.FromMilliseconds(180)));
                inputShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-5, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }
        ToastBanner.Visibility = expanded && message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DictationButton.ToolTip = c.Speech.IsTranscribing ? "Распознаю…" : c.Speech.IsListening ? "Остановить диктовку" : c.Speech.IsConfigured ? "Диктовка · " + GlobalShortcuts.Label(c.Settings, "dictation") : "Диктовка отключена · настройте API или включите локальный движок в настройках";
        DictationButton.Background = c.Speech.IsListening ? Ui.Brush("Hover") : Brushes.Transparent;
        DictationButton.IsEnabled = !c.Speech.IsTranscribing && (c.Speech.IsListening || c.Speech.IsConfigured);
        CompactNormal.Visibility = HasAlarm ? Visibility.Collapsed : Visibility.Visible;
        CompactAlarm.Visibility = HasAlarm ? Visibility.Visible : Visibility.Collapsed;
        if (HasAlarm)
        {
            compactMeasureKey = ""; Compact.ToolTip = reminderAlarms.Count > 0 ? reminderAlarms.Peek().Title + " · напомнить через 10 минут или закрыть" : "Таймер завершён · повторить или закрыть";
            CompactAlarmTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var alarmWidth = reminderAlarms.Count > 0 ? Math.Clamp(CompactAlarmTitle.DesiredSize.Width + 280, 400, 700) : 330;
            SetCompactSize(Math.Max(alarmWidth, c.Settings.CompactMinWidth), Math.Max(72, c.Settings.CompactHeight + 16));
            return;
        }
        var measureKey = icon + "|" + ClockText.Text + "|" + CompactTimer.Visibility + CompactTimerText.Text + coverVisible + mediaTitleVisible + CompactTitle.Text + "\n" + CompactSubtitle.Text + "\n" + CompactWeather.Text + inputState;
        if (measureKey != compactMeasureKey)
        {
            compactMeasureKey = measureKey;
            double Measure(TextBlock text, int maxCharacters = 0)
            {
                var value = text.Text;
                if (maxCharacters > 0)
                {
                    var characters = System.Globalization.StringInfo.ParseCombiningCharacters(value);
                    if (characters.Length > maxCharacters) value = value[..characters[maxCharacters]];
                }
                return new FormattedText(value, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
            }
            compactPrefixWidth = Math.Ceiling((icon.Length > 0 ? 22 : 0) + Measure(ClockText)) + (timerEnd != null ? Math.Ceiling(40 + Measure(CompactTimerText)) : 0) + (coverVisible ? 40 : 0);
            compactMetaWidth = (c.Settings.ShowInputStatus ? Math.Ceiling(Measure(InputLayout) + (c.Activity.CapsLock ? Measure(InputCaps) + 5 : 0) + 10) : 0) + (CompactWeather.Text.Length > 0 ? Math.Ceiling(Measure(CompactWeather) + 33) : 0);
            if (compactMetaWidth > 0) compactMetaWidth += 12;
            CompactMeta.Visibility = compactMetaWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
            var titleWidth = Math.Ceiling(Measure(CompactTitle, mediaTitleVisible ? 10 : 0));
            var subtitleWidth = Math.Ceiling(Measure(CompactSubtitle, mediaTitleVisible ? 12 : 0));
            var details = CompactLines.Visibility == Visibility.Visible ? Math.Max(titleWidth, subtitleWidth) + 12 : 0;
            var targetWidth = Math.Clamp(34 + 28 + compactPrefixWidth + details + compactMetaWidth + 2, c.Settings.CompactMinWidth, c.Settings.CompactMaxWidth);
            var availableTextWidth = Math.Max(0, targetWidth - 34 - 28 - compactPrefixWidth - compactMetaWidth - CompactLines.Margin.Left - CompactLines.Margin.Right);
            compactTitleOverflow.Width = Math.Min(titleWidth, availableTextWidth); compactSubtitleOverflow.Width = Math.Min(subtitleWidth, availableTextWidth);
            compactTitleOverflow.Refresh(); compactSubtitleOverflow.Refresh();
            Compact.ToolTip = ClockText.Text + (timerEnd != null ? " · Таймер " + CompactTimerText.Text : "") + "\n" + CompactTitle.Text + (CompactSubtitle.Text.Length > 0 ? "\n" + CompactSubtitle.Text : "");
            SetCompactSize(targetWidth, c.Settings.CompactHeight);
        }
    }
    private void SetCompactSize(double width, double height)
    {
        var changed = Math.Abs(width - compactWidth) > 1 || Math.Abs(height - compactHeight) > .1;
        compactWidth = width; compactHeight = height; Compact.Width = width - 34; Compact.Height = height - 8;
        UpdatePanelClip();
        if (changed && !expanded) motion.To(width, height, (!snapshot || mirrorOwner != null || NativeMotionProbe) && IsLoaded, keepCompletion: true);
    }
    public async Task ScreenshotAsync(string mode)
    {
        try
        {
            var result = await SelectRegionAsync(mode == "color", mode == "shot"); if (result == null) return;
            if (mode == "ocr") { var text = await c.Capture.OcrAsync(result.Image); if (text.Length == 0) c.Notify("Текст не найден"); else { System.Windows.Clipboard.SetText(text); c.Notify("Распознанный текст скопирован"); } }
            else if (mode == "qr") { var text = CaptureService.DecodeQr(result.Image); if (text == null) c.Notify("QR-код не найден"); else { System.Windows.Clipboard.SetText(text); c.Notify("QR-код скопирован: " + text); } }
            else if (mode == "color")
            {
                var pixel = new byte[4]; new System.Windows.Media.Imaging.FormatConvertedBitmap(result.Image, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
                var hex = $"#{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}"; System.Windows.Clipboard.SetText(hex); c.State.Data.Colors.Remove(hex); c.State.Data.Colors.Insert(0, hex); if (c.State.Data.Colors.Count > 20) c.State.Data.Colors.RemoveAt(20); c.State.Touch(); c.Notify(hex + " скопирован");
            }
            else await SaveScreenshotAsync(result.Image);
        }
        catch (Exception e) { c.Error(e); }
    }
    private async Task<CaptureResult?> SelectRegionAsync(bool color = false, bool annotate = false)
    {
        if (selectionBusy) throw new InvalidOperationException("Сначала завершите выбор области. Esc отменяет выделение.");
        selectionBusy = true;
        try
        {
            using var capture = SuppressForCapture();
            Collapse();
            if (snapshot && SelectionForChecks != null) return await SelectionForChecks(color);
            using var cancellation = new CancellationTokenSource();
            using var escape = new CaptureEscape(() => cancellation.Cancel());
            try { await Task.Delay(270, cancellation.Token); return await c.Capture.SelectAsync(color, annotate, cancellation.Token); }
            catch (OperationCanceledException) { return null; }
        }
        finally { selectionBusy = false; }
    }
    public async Task ScreenshotFullAsync()
    {
        if (selectionBusy) { c.Notify("Сначала завершите выбор области. Esc отменяет выделение."); return; }
        selectionBusy = true;
        try { using var capture = SuppressForCapture(); Collapse(); await Task.Delay(300); await SaveScreenshotAsync(CaptureService.Screen(Screen.Bounds)); }
        catch (Exception e) { c.Error(e); }
        finally { selectionBusy = false; }
    }
    private async Task SaveScreenshotAsync(System.Windows.Media.Imaging.BitmapSource image)
    {
        var path = await c.Capture.SaveAndCopyAsync(image);
        c.Notify("Снимок сохранён и скопирован: " + Path.GetFileName(path));
    }
    public async Task ToggleRecordingAsync(bool gif = false, bool fullScreen = false, bool allScreens = false)
    {
        if (recordCommandBusy) { c.Notify("Запись уже запускается или сохраняется. Завершите выбор области; Esc — отмена."); return; }
        recordCommandBusy = true;
        try
        {
            if (c.Recording.IsStarting || c.Recording.IsFinalizing) { c.Notify(c.Recording.IsStarting ? "Запись запускается…" : "Запись сохраняется…"); return; }
            if (c.Recording.IsRecording) { var path = await c.Recording.StopAsync(); if (path != null) { var files = new System.Collections.Specialized.StringCollection { path }; System.Windows.Clipboard.SetFileDropList(files); c.Notify("Запись сохранена и скопирована как файл"); ShowPanel("home", false); } return; }
            using var capture = SuppressForCapture();
            System.Drawing.Rectangle? bounds;
            if (fullScreen) { Collapse(); await Task.Delay(300); bounds = allScreens ? System.Windows.Forms.SystemInformation.VirtualScreen : Screen.Bounds; }
            else bounds = (await SelectRegionAsync())?.Bounds;
            if (bounds is { } area)
            {
                await c.Recording.StartAsync(area, gif);
                var stopShortcut = GlobalShortcuts.Label(c.Settings, fullScreen ? "recording-fullscreen" : "recording");
                var stop = c.Settings.HideInCapture ? "Остановка: " + stopShortcut + " или меню значка в трее"
                    : "Нажмите чёлку или " + stopShortcut + " для остановки";
                c.Notify((c.Recording.Warning ?? "Запись началась") + " · " + stop);
            }
        }
        catch (Exception e) { c.Error(e); }
        finally { recordCommandBusy = false; }
    }
    public async Task DictateAsync()
    {
        if (c.Speech.IsTranscribing) { c.Notify("Предыдущая диктовка ещё распознаётся"); return; }
        try
        {
            if (!c.Speech.IsListening) { c.Speech.Start(); c.Notify("Слушаю · " + GlobalShortcuts.Label(c.Settings, "dictation") + " — закончить"); }
            else { c.Notify("Распознавание…"); var text = await c.Speech.StopAsync(); if (text.Length > 0) { System.Windows.Clipboard.SetText(text); c.Notify("Диктовка скопирована"); } else c.Notify("Речь не распознана"); }
            UpdateCompact();
        }
        catch (Exception e) { c.Error(e); }
    }
    public FrameworkElement RenderRoot => Root;
}
