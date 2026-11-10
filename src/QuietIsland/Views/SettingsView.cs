using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class SettingsView : IslandView
{
    private readonly List<(DispatcherTimer Timer, Action Flush)> pending = [];
    private readonly TextBlock saveStatus = Ui.Text("Изменения применяются и сохраняются автоматически", 11, "Muted");
    private readonly MainWindow window;
    private readonly Dictionary<string, UIElement> sections = new();
    private readonly TextBlock heading = Ui.Heading("Общие");
    private readonly ScrollViewer scroll;
    private readonly UpdatesView updates;
    private readonly CheckBox autoStart;
    public IReadOnlyList<string> Categories => sections.Keys.ToArray();
    public SettingsView(IslandContext c, MainWindow main) : base(c)
    {
        window = main; var s = c.Settings; var stack = Ui.Stack();
        autoStart = new CheckBox { Content = "Запускать Quiet Island при входе в Windows" };
        autoStart.Click += (_, _) => { Change(() => SystemService.AutoStart(autoStart.IsChecked == true)); RefreshAutoStart(); };
        stack.Children.Add(Section("ВИД ОСТРОВА",
            Ui.Text("Размер и форма", 14, bold: true),
            Range("Минимальная ширина", s.CompactMinWidth, 160, 360, 1, " px", x => s.CompactMinWidth = x),
            Range("Максимальная ширина · подстраивается под текст", s.CompactMaxWidth, 360, 700, 1, " px", x => s.CompactMaxWidth = x),
            Range("Высота свёрнутой чёлки", s.CompactHeight, 44, 72, 1, " px", x => s.CompactHeight = x),
            Range("Размер текста в чёлке", s.CompactFontSize, 10, 16, .5, " pt", x => s.CompactFontSize = x),
            Range("Скругление в свёрнутом виде", s.CompactCornerRadius, 0, 24, 1, " px", x => s.CompactCornerRadius = x),
            Range("Скругление открытой панели", s.ExpandedCornerRadius, 0, 36, 1, " px", x => s.ExpandedCornerRadius = x),
            Range("Плечики у верхнего края", s.ShoulderRadius, 8, 28, 1, " px", x => s.ShoulderRadius = x),
            ColorField("Цвет тёмного острова", s.SurfaceColor, ["#000000", "#121318", "#17122A", "#092321"], x => s.SurfaceColor = x),
            ColorField("Акцент интерфейса", s.AccentColor, ["#FF8B81", "#FFC56C", "#B4A4FF", "#80CDF5", "#7CDEBA"], x => s.AccentColor = x),
            Ui.Text("Движение и поведение", 14, bold: true),
            Range("Скорость раскрытия и сворачивания", s.AnimationSpeed, .6, 1.8, .05, "×", x => s.AnimationSpeed = x),
            Range("Открывать после наведения курсора", s.HoverOpenDelayMs, 0, 5000, 50, " мс", x => s.HoverOpenDelayMs = x),
            Range("Сворачивать после ухода курсора", s.HoverCloseDelayMs, 80, 2000, 20, " мс", x => s.HoverCloseDelayMs = x),
            Ui.Text("Открытое меню, закрепление и поиск с клавиатуры удерживают панель. Цвет и форма не меняются при запуске музыки.", 11, "Muted")));
        stack.Children.Add(Section("СОСТАВ И МУЗЫКА",
            Ui.Text("Часы всегда остаются видны. Таймер появляется справа от них.", 12, "Muted"),
            Combo("Формат времени", ["24 часа", "12 часов · AM/PM"], s.Clock12Hour ? "12 часов · AM/PM" : "24 часа", x => s.Clock12Hour = x.StartsWith("12")),
            Check("Показывать секунды на часах", s.ClockSeconds, x => s.ClockSeconds = x),
            Check("Показывать раскладку и Caps Lock", s.ShowInputStatus, x => s.ShowInputStatus = x),
            Check("Показывать погоду в чёлке", s.ShowWeather, x => s.ShowWeather = x),
            Check("Показывать обложку композиции", s.ShowMusicCover, x => s.ShowMusicCover = x),
            Check("Показывать название и исполнителя", s.ShowSongTitle, x => s.ShowSongTitle = x),
            Check("Жидкая кромка при музыке", s.MusicAnimationEnabled, x => s.MusicAnimationEnabled = x),
            Check("Брать цвет музыкальной кромки из обложки", s.UseAlbumAccent, x => s.UseAlbumAccent = x),
            Range("Чувствительность к звуку", s.MusicSensitivity, .25, 3, .05, "×", x => s.MusicSensitivity = x),
            Range("Амплитуда нижнего контура", s.MusicAmplitude, 0, 12, .5, " px", x => s.MusicAmplitude = x),
            Check("Приостанавливать медиа на время сигнала таймера", s.PauseMediaForTimer, x => s.PauseMediaForTimer = x),
            Check("Приостанавливать медиа на время напоминания", s.PauseMediaForReminder, x => s.PauseMediaForReminder = x),
            Check("Приостанавливать музыку при другом медиа · плееры, браузеры и Telegram", s.PauseMediaForOtherAudio, x => s.PauseMediaForOtherAudio = x),
            Text("Ограничить медиаисточники · пусто — все поддерживаемые; список через запятую", s.AudioInterruptApps, x => s.AudioInterruptApps = x),
            Check("Возобновлять медиа после прерывания", s.ResumeInterruptedMedia, x => s.ResumeInterruptedMedia = x),
            Range("Задержка возобновления медиа", s.MediaResumeDelayMs, 0, 10000, 100, " мс", x => s.MediaResumeDelayMs = x),
            Ui.Text("Учитывается воспроизведение в поддерживаемых плеерах и браузерах. В Telegram — только голосовые сообщения, музыка и видео через системный медиаплеер; звуки уведомлений игнорируются. Discord, игры и OBS не прерывают музыку. Просто открытое окно не вызывает паузу. Возобновляется только воспроизведение, приостановленное островом; ручное управление отменяет автовозобновление.", 11, "Muted")));
        stack.Children.Add(Section("ВЕРХНЯЯ ПАНЕЛЬ", Ui.Text("Выберите вкладки и их порядок. Стрелки перемещают включённые вкладки.", 12, "Muted"), HeaderEditor(), Check("Название активной вкладки", s.HeaderLabels, x => s.HeaderLabels = x), Check("Часы", s.HeaderClock, x => s.HeaderClock = x), Check("Диктовка", s.HeaderDictation, x => s.HeaderDictation = x), Check("Закрепление", s.HeaderPin, x => s.HeaderPin = x), Check("Кнопка сворачивания", s.HeaderClose, x => s.HeaderClose = x)));
        stack.Children.Add(Section("BLUETOOTH · ЗАРЯД",
            Check("Показывать заряд Bluetooth-устройств", s.ShowBluetoothBattery, x => s.ShowBluetoothBattery = x),
            Check("Получать заряд AirPods по Bluetooth LE", s.AirPodsBatteryEnabled, x => s.AirPodsBatteryEnabled = x),
            Ui.Text("AirPods: один общий заряд наушников — среднее доступных показаний, без кейса. Знак ≈ означает приблизительные данные с шагом 10%. Индикатор появляется только у подключённых наушников при получении данных; устаревшие показания скрываются.", 11, "Muted"),
            Check("В свёрнутой чёлке · справа от погоды", s.BluetoothBatteryCompact, x => s.BluetoothBatteryCompact = x),
            Check("В развёрнутой чёлке · слева от часов", s.BluetoothBatteryExpanded, x => s.BluetoothBatteryExpanded = x),
            Check("Уведомление при подключении · 2 секунды", s.BluetoothConnectionNotifications, x => s.BluetoothConnectionNotifications = x),
            Check("Название устройства в уведомлении", s.BluetoothBatteryShowNames, x => s.BluetoothBatteryShowNames = x),
            Ui.Text("Постоянно видны только кольцо заряда и иконка. При подключении свёрнутая чёлка немного увеличивается по ширине и высоте и на 2 секунды показывает только устройство: кольцо, процент, имя и иконку. Переходы используют ease-out. Затем возвращаются прежнее содержимое и размер с добавленным индикатором устройства. Кольцо: зелёный — 51–100%, жёлтый — 31–50%, красный — до 30%. Отключённые устройства и устройства без данных о заряде скрываются. Полные данные доступны при наведении.", 12, "Muted")));
        stack.Children.Add(Section("РЕСУРСЫ", Check("Автоматически снижать нагрузку при занятом компьютере", s.AutoReduceResources, x => s.AutoReduceResources = x), Text("Всегда экономить в программах · имена через запятую", s.ResourceHeavyApps, x => s.ResourceHeavyApps = x), Ui.Text("При высокой загрузке CPU или активном ресурсоёмком приложении снижается частота фоновых обновлений, анализа звука и визуализатора. Скрытая чёлка не рисует музыку и не анализирует звук. Таймер и горячие клавиши продолжают работать.", 12, "Muted")));
        stack.Children.Add(Section("ОСТРОВ", autoStart, Ui.Text("Автоматически открывать остров после входа в учётную запись Windows. Эта же опция доступна в установщике.", 11, "Muted"), Combo("Тема", ["Dark", "Light", "System"], s.Theme, x => s.Theme = x), Number("Масштаб · 80–200%", (s.Scale * 100).ToString("0"), x => s.Scale = Math.Clamp(x, 80, 200) / 100), Number("Прозрачность пилюли · 45–100%", (s.PillOpacity * 100).ToString("0"), x => s.PillOpacity = Math.Clamp(x, 45, 100) / 100), Number("Прозрачность панели · 45–100%", (s.PanelOpacity * 100).ToString("0"), x => s.PanelOpacity = Math.Clamp(x, 45, 100) / 100), DisplayPicker(), Number("Отступ сверху · пиксели", s.TopOffset.ToString("0"), x => s.TopOffset = Math.Clamp(x, 0, 200)), Check("Автоскрытие за верхний край", s.AutoHide, x => s.AutoHide = x), Check("Прятать в полноэкранных программах", s.HideFullscreen, x => s.HideFullscreen = x), Check("Сквозные клики в свёрнутом виде · открытие горячей клавишей", s.ClickThrough, x => s.ClickThrough = x), Text("Прятать в программах · имена через запятую", s.ExcludedApps, x => s.ExcludedApps = x), Ui.Text("Контур статичен. Жидкая анимация включается автоматически только при музыке.", 11, "Muted")));
        stack.Children.Add(Section("БУФЕР И КЛАВИШИ", Check("История буфера обмена", s.ClipboardEnabled, x => s.ClipboardEnabled = x), Check("Вставлять запись в предыдущее окно при клике", s.ClipboardAutoPaste, x => s.ClipboardAutoPaste = x), Number("Число незакреплённых записей · 20–1000", s.ClipboardLimit.ToString(), x => s.ClipboardLimit = Math.Clamp((int)x, 20, 1000)), ShortcutEditor(), Ui.Text("Введите сочетание и нажмите «Назначить». Пустое поле отключает действие. Занятое сочетание не заменяется автоматически. Клавиши ярлыков задаются через их контекстное меню на главной.", 11, "Muted")));
        var endpoint = Text("API URL · OpenAI-совместимый, включая /v1", s.AiEndpoint, x => s.AiEndpoint = x.Trim()); var modelBox = Ui.Input("Модель", s.AiModel); BindText(modelBox, () => s.AiModel = modelBox.Text.Trim());
        stack.Children.Add(Section("ИИ", endpoint, Ui.Field("Модель", modelBox), Secret("API ключ · пусто — сохранить прежний", x => s.AiKeyEncrypted = DesktopState.Protect(x)), Ui.AsyncButton("Получить модели", async () => { Save(); var models = await C.Ai.ModelsAsync(); if (models.Length == 0) C.Notify("У сервиса пока нет моделей"); else { var selected = Windows.Dialogs.Choose("Выберите модель", models); if (selected != null) { modelBox.Text = selected; s.AiModel = selected; C.State.Touch(); } } }, C.Error), Text("Системная инструкция", s.AiSystemPrompt, x => s.AiSystemPrompt = x, true), Ui.Button("Удалить API ключ", () => { Change(() => s.AiKeyEncrypted = ""); C.Notify("Ключ удалён · ИИ отключён"); }), Ui.Text("Без API ключа чат и ИИ-действия отключены. Укажите URL совместимого API, модель и ключ; это требуется и для локального сервиса. ИИ-модель не встроена в приложение. Ключи защищены DPAPI текущего пользователя Windows.", 11, "Muted")));
        stack.Children.Add(Section("ДИКТОВКА", Check("Использовать установленный локальный речевой движок Windows", s.SpeechPreferLocal, x => s.SpeechPreferLocal = x), Text("Whisper-совместимый API URL · включая /v1", s.SpeechEndpoint, x => s.SpeechEndpoint = x.Trim()), Text("Модель распознавания", s.SpeechModel, x => s.SpeechModel = x.Trim()), Secret("Ключ речевого сервиса", x => s.SpeechKeyEncrypted = DesktopState.Protect(x)), Ui.Button("Удалить ключ речевого сервиса", () => { Change(() => s.SpeechKeyEncrypted = ""); C.Notify("Ключ речевого сервиса удалён"); }), Ui.Text("Локальный речевой движок по умолчанию выключен и запускается только при диктовке. Для облачной диктовки нужны API URL и ключ. До настройки микрофон не включается. Временный WAV удаляется после запроса.", 11, "Muted")));
        var microphoneId = Ui.Input("ID микрофона · пусто — по умолчанию Windows", s.MicrophoneId);
        BindText(microphoneId, () => s.MicrophoneId = microphoneId.Text.Trim());
        stack.Children.Add(Section("ЗАПИСЬ И ЗВУК", Check("Скрывать чёлку на снимках и в записи", s.HideInCapture, x => s.HideInCapture = x), Ui.Text("Применяется до снимка и до первого кадра записи на всех выбранных экранах. Во время записи чёлка скрыта; остановка — горячей клавишей или через значок в трее. Опция также доступна правой кнопкой на кнопках снимка и записи.", 11, "Muted"), Text("Папка снимков и записей", s.CaptureFolder, x => s.CaptureFolder = Path.GetFullPath(x)), Text("FFmpeg · команда или полный путь", s.FfmpegPath, x => s.FfmpegPath = x.Trim()), Combo("Кадров в секунду", ["30", "60"], s.RecordingFps.ToString(), x => s.RecordingFps = int.Parse(x)), Check("Высокое качество видео", s.RecordingHighQuality, x => s.RecordingHighQuality = x), Check("Записывать системный звук в MP4", s.RecordSystemAudio, x => s.RecordSystemAudio = x), Check("Записывать курсор", s.RecordCursor, x => s.RecordCursor = x), Ui.Field("ID микрофона · пусто — по умолчанию Windows", microphoneId), Ui.AsyncButton("Список микрофонов", () => { var devices = C.System.AudioDevices(NAudio.CoreAudioApi.DataFlow.Capture); var choice = Windows.Dialogs.Choose("Выберите микрофон", devices.Select(x => x.Name).Prepend("По умолчанию").ToArray()); if (choice != null) { microphoneId.Text = devices.FirstOrDefault(x => x.Name == choice).Id ?? ""; C.Notify("Микрофон выбран."); } return Task.CompletedTask; }, C.Error)));
        stack.Children.Add(Section("CLAUDE CODE / CODEX", Check("Локальный мост агентов", s.BridgeEnabled, x => s.BridgeEnabled = x), Number("Порт localhost", s.BridgePort.ToString(), x => s.BridgePort = Port(x)), Ui.Text("При отсутствии ответа запрос автоматически отклоняется. Подключение хуков и app-server описано в integrations/README.md. Настройки агентов приложение самостоятельно не меняет.", 11, "Muted"), Ui.Button("Путь к токену моста", () => { System.Windows.Clipboard.SetText(C.Agents.TokenFile); C.Notify("Путь к файлу токена скопирован"); })));
        stack.Children.Add(Section("OBS WEBSOCKET 5", Check("Подключаться к OBS на этом компьютере", s.ObsEnabled, x => s.ObsEnabled = x), Number("Порт OBS", s.ObsPort.ToString(), x => s.ObsPort = Port(x)), Secret("Пароль OBS WebSocket", x => s.ObsPasswordEncrypted = DesktopState.Protect(x)), Ui.Text("В OBS: Инструменты → Настройки сервера WebSocket. Пароль нужен только при включённой авторизации.", 11, "Muted")));
        updates = new(c, main);
        stack.Children.Add(Section("ОБНОВЛЕНИЯ", Check("Автоматически проверять обновления", s.AutomaticUpdateChecks, x => s.AutomaticUpdateChecks = x), updates));
        scroll = Ui.Scroll(stack);
        var root = new DockPanel(); var header = Ui.Stack(heading, Ui.Text("", 8)); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = Ui.Stack(saveStatus, Ui.Button("Папка данных", () => { Directory.CreateDirectory(DesktopState.DataDirectory); SystemService.Open(DesktopState.DataDirectory); })); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(scroll);
        Content = root; SelectCategory(Categories[0]);
    }
    private static int Port(double number) => number is >= 1024 and <= 65535 && number == Math.Truncate(number) ? (int)number : throw new InvalidOperationException("Порт должен быть целым числом от 1024 до 65535");
    private UIElement DisplayPicker()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var selected = C.Settings.IslandDisplays.Count == 0
            ? new HashSet<string>([screens[Math.Clamp(C.Settings.Monitor, 0, screens.Length - 1)].DeviceName], StringComparer.OrdinalIgnoreCase)
            : C.Settings.IslandDisplays.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var panel = Ui.Stack(Ui.Text("Экраны для чёлки", 13, bold: true), Ui.Text("Отметьте один или несколько экранов. Номера и разрешения помогают их различить.", 11, "Muted"));
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var check = new CheckBox { Content = $"Экран {i + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · основной" : ""), IsChecked = selected.Contains(screen.DeviceName) };
            check.Click += (_, _) =>
            {
                if (check.IsChecked == true) selected.Add(screen.DeviceName);
                else { if (selected.Count(id => screens.Any(x => x.DeviceName == id)) <= 1) { check.IsChecked = true; return; } selected.Remove(screen.DeviceName); }
                Change(() => { C.Settings.IslandDisplays = screens.Where(x => selected.Contains(x.DeviceName)).Select(x => x.DeviceName).ToList(); C.Settings.Monitor = Array.FindIndex(screens, x => x.DeviceName == C.Settings.IslandDisplays[0]); });
            };
            panel.Children.Add(check);
        }
        return panel;
    }
    private UIElement ShortcutEditor()
    {
        var panel = Ui.Stack(); var bindings = GlobalShortcuts.Bindings(C.Settings, C.State.Data.Launcher);
        foreach (var definition in GlobalShortcuts.Definitions)
        {
            var input = Ui.Input("Например Ctrl+Shift+S", bindings.GetValueOrDefault(definition.Key, ""));
            input.Margin = new Thickness(0, 0, 8, 0);
            System.Windows.Automation.AutomationProperties.SetName(input, definition.Title);
            var feedback = Ui.Text(window.ShortcutErrors.GetValueOrDefault(definition.Key) ?? "", 11, "Muted"); feedback.TextWrapping = TextWrapping.Wrap;
            var row = new DockPanel();
            var assign = Ui.Button("Назначить", () =>
            {
                var value = input.Text.Trim();
                var error = GlobalShortcuts.ValidationError(C.Settings, definition.Key, value, C.State.Data.Launcher);
                if (error != null) { feedback.Text = error; return; }
                Change(() => { bindings[definition.Key] = value; window.InvalidateHotkeys(); });
                feedback.Text = window.ShortcutErrors.GetValueOrDefault(definition.Key) ?? (value.Length == 0 ? "Сочетание отключено" : "Назначено: " + value);
            });
            assign.Margin = new Thickness(0); DockPanel.SetDock(assign, Dock.Right); row.Children.Add(assign); row.Children.Add(input);
            panel.Children.Add(Ui.Field(definition.Title, Ui.Stack(row, feedback)));
        }
        return panel;
    }
    private UIElement Section(string title, params UIElement[] content)
    {
        var name = title switch { "ВИД ОСТРОВА" => "Вид острова", "СОСТАВ И МУЗЫКА" => "Состав и музыка", "ВЕРХНЯЯ ПАНЕЛЬ" => "Верхняя панель", "BLUETOOTH · ЗАРЯД" => "Bluetooth · заряд", "РЕСУРСЫ" => "Ресурсы", "ОСТРОВ" => "Общие", "БУФЕР И КЛАВИШИ" => "Буфер и клавиши", "ИИ" => "Искусственный интеллект", "ДИКТОВКА" => "Диктовка", "ЗАПИСЬ И ЗВУК" => "Запись и звук", "CLAUDE CODE / CODEX" => "Агенты", "ОБНОВЛЕНИЯ" => "Обновления", _ => "OBS" };
        var panel = Ui.Stack(content); panel.Margin = new Thickness(0, 4, 14, 12); sections[name] = panel; return panel;
    }
    public void SelectCategory(string name)
    {
        foreach (var (key, panel) in sections) panel.Visibility = key == name ? Visibility.Visible : Visibility.Collapsed;
        heading.Text = name; scroll?.ScrollToTop();
        if (name == "Общие") RefreshAutoStart();
    }
    private void RefreshAutoStart()
    {
        try { autoStart.IsChecked = SystemService.AutoStartEnabled(); autoStart.IsEnabled = true; }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            autoStart.IsEnabled = false; saveStatus.Text = "Не удалось прочитать настройку автозагрузки · " + e.GetType().Name;
        }
    }
    private UIElement Text(string label, string value, Action<string> set, bool multiline = false) { var input = Ui.Input(label, value, multiline); if (multiline) input.Height = 70; BindText(input, () => set(input.Text)); return Ui.Field(label, input); }
    private UIElement Number(string label, string value, Action<double> set) => Text(label, value, x => { if (!double.TryParse(x.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)) throw new InvalidOperationException("Проверьте число: " + label); set(n); });
    private UIElement Range(string label, double value, double min, double max, double step, string suffix, Action<double> set)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(0, 8, 12, 4), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        var number = Ui.Text("", 11, "Muted"); number.MinWidth = 68; number.TextAlignment = TextAlignment.Right; number.VerticalAlignment = VerticalAlignment.Center;
        void Describe() => number.Text = slider.Value.ToString(step < 1 ? "0.##" : "0") + suffix;
        slider.ValueChanged += (_, _) => { Describe(); Change(() => set(slider.Value)); }; Describe();
        var row = new DockPanel(); DockPanel.SetDock(number, Dock.Right); row.Children.Add(number); row.Children.Add(slider); return Ui.Field(label, row);
    }
    private UIElement ColorField(string label, string value, string[] palette, Action<string> set)
    {
        var input = Ui.Input("#RRGGBB", value); input.Width = 105; input.Margin = new Thickness(0, 0, 12, 0);
        var row = Ui.Row(input);
        foreach (var hex in palette)
        {
            var button = Ui.Button("", () => input.Text = hex); button.Width = button.Height = 27; button.Padding = new Thickness(0); button.ToolTip = hex;
            button.Content = new Border { Width = 19, Height = 19, CornerRadius = new CornerRadius(6), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)), BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1) }; row.Children.Add(button);
        }
        BindText(input, () => { var hex = input.Text.Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$")) throw new InvalidOperationException("Укажите цвет в формате #RRGGBB: " + label); set(hex); });
        return Ui.Field(label, row);
    }
    private UIElement Check(string label, bool value, Action<bool> set) { var input = new CheckBox { Content = label, IsChecked = value }; input.Click += (_, _) => Change(() => set(input.IsChecked == true)); return input; }
    private UIElement Combo(string label, string[] options, string value, Action<string> set) { var combo = new ComboBox { ItemsSource = options, SelectedItem = value }; combo.SelectionChanged += (_, _) => Change(() => set(combo.SelectedItem as string ?? options[0])); return Ui.Field(label, combo); }
    private UIElement Secret(string label, Action<string> set)
    {
        var password = new PasswordBox { Margin = new Thickness(0, 5, 0, 8) }; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) }; var dirty = false;
        void Flush() { timer.Stop(); if (!dirty) return; dirty = false; if (password.Password.Length > 0) Change(() => set(password.Password)); }
        timer.Tick += (_, _) => Flush(); password.PasswordChanged += (_, _) => { dirty = true; timer.Stop(); timer.Start(); }; password.LostKeyboardFocus += (_, _) => Flush(); pending.Add((timer, Flush)); return Ui.Field(label, password);
    }
    private void BindText(TextBox input, Action update)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) }; var dirty = false;
        void Flush() { timer.Stop(); if (!dirty) return; dirty = false; Change(update); }
        timer.Tick += (_, _) => Flush(); input.TextChanged += (_, _) => { dirty = true; timer.Stop(); timer.Start(); }; input.LostKeyboardFocus += (_, _) => Flush(); pending.Add((timer, Flush));
    }
    private UIElement HeaderEditor()
    {
        var panel = Ui.Stack(); var labels = new Dictionary<string, string> { ["home"]="Главная", ["clipboard"]="Буфер", ["shelf"]="Полка", ["tools"]="Текст", ["ai"]="Чат", ["notes"]="Заметки", ["weather"]="Погода", ["control"]="Управление" };
        void Build()
        {
            panel.Children.Clear();
            foreach (var key in C.Settings.HeaderTabs.Concat(labels.Keys).Distinct().ToArray())
            {
                var row = new DockPanel();
                Button Move(int direction)
                {
                    var index = C.Settings.HeaderTabs.IndexOf(key); var button = Ui.Button("", () => { Change(() => { var at = C.Settings.HeaderTabs.IndexOf(key); if (at < 0 || at + direction < 0 || at + direction >= C.Settings.HeaderTabs.Count) return; C.Settings.HeaderTabs.RemoveAt(at); C.Settings.HeaderTabs.Insert(at + direction, key); }); Build(); });
                    var icon = Icons.Make("chevron-right", 14); icon.LayoutTransform = new System.Windows.Media.RotateTransform(direction < 0 ? -90 : 90); button.Content = icon; button.ToolTip = direction < 0 ? "Выше" : "Ниже"; button.Width = 32; button.Padding = new Thickness(6); button.IsEnabled = index >= 0 && index + direction >= 0 && index + direction < C.Settings.HeaderTabs.Count; return button;
                }
                var buttons = Ui.Row(Move(-1), Move(1)); DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
                row.Children.Add(Check(labels[key], C.Settings.HeaderTabs.Contains(key), enabled => { if (enabled) C.Settings.HeaderTabs.Add(key); else C.Settings.HeaderTabs.Remove(key); Build(); })); panel.Children.Add(row);
            }
        }
        Build(); return panel;
    }
    private void Change(Action action)
    {
        if (Disposed) return;
        var audio = C.Settings.AudioOutputId; var bridge = (C.Settings.BridgeEnabled, C.Settings.BridgePort); var obs = (C.Settings.ObsEnabled, C.Settings.ObsPort, C.Settings.ObsPasswordEncrypted);
        try
        {
            action(); C.Settings.NormalizeAppearance(); C.State.Touch();
            if (!window.IsClosing)
            {
                window.ApplySettings();
                if (audio != C.Settings.AudioOutputId) C.Media.StartAudio(C.Settings.AudioOutputId);
                if (bridge != (C.Settings.BridgeEnabled, C.Settings.BridgePort)) C.Agents.Start();
                if (obs != (C.Settings.ObsEnabled, C.Settings.ObsPort, C.Settings.ObsPasswordEncrypted)) C.Obs.Start();
            }
            saveStatus.Text = "Изменения применены · сохранение автоматически";
        }
        catch (Exception e) { saveStatus.Text = e is InvalidOperationException or ArgumentException ? e.Message : "Проверьте значение · " + e.GetType().Name; }
    }
    private void Save()
    {
        foreach (var item in pending) item.Flush(); C.State.Save();
    }
    public override void Dispose() { updates.Dispose(); Save(); foreach (var item in pending) item.Timer.Stop(); base.Dispose(); }
}

