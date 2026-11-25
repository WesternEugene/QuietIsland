using System.Windows;
using System.Windows.Input;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Windows;

internal partial class TutorialWindow : Window
{
    private sealed record Step(string Icon, string Title, string Detail, params (string Label, string Tab)[] Actions);

    private readonly Step[] steps;
    private readonly Action<string> navigate;
    private int index;

    public TutorialWindow(string? searchShortcut, Action<string> navigate)
    {
        InitializeComponent();
        this.navigate = navigate;
        var searchHint = string.IsNullOrWhiteSpace(searchShortcut) ? "" : $" Горячая клавиша поиска — {searchShortcut}.";
        steps =
        [
            new("home", "Знакомство с чёлкой",
                "Наведите курсор на чёлку или щёлкните по ней. Булавка удерживает панель открытой, Esc сворачивает её. Колёсико на свёрнутой чёлке меняет громкость. " +
                "На «Главной» управляйте музыкой, читайте тексты песен и добавляйте ярлыки.",
                ("Открыть главную", "home")),
            new("search", "Поиск и быстрые команды",
                "Ищите приложения, файлы через Everything и параметры Windows. В поиске также можно считать, переводить единицы и валюты или ввести «таймер 5 мин»." + searchHint,
                ("Открыть поиск", "search")),
            new("clipboard", "Буфер и полка",
                "Недавние копии текста, картинок и файлов можно вставить снова или закрепить в «Буфере». Перетащите файл на «Полку», чтобы потом открыть его или перенести в другое приложение.",
                ("Открыть буфер", "clipboard"), ("Открыть полку", "shelf")),
            new("notes", "Заметки и таймеры",
                "Ведите заметки со списками и флажками задач, запускайте таймеры и создавайте разовые или повторяющиеся напоминания. Остаток времени виден на чёлке.",
                ("Открыть заметки", "notes")),
            new("tools", "Текст и захват экрана",
                "Исправляйте раскладку, меняйте формат текста, создавайте и считывайте QR-коды. Здесь же можно сделать снимок, распознать текст с экрана, взять цвет пипеткой и записать экран в MP4 или GIF.",
                ("Открыть инструменты", "tools")),
            new("ai", "Чат и диктовка",
                "В «Чате» можно подключить локальную или облачную модель для работы с текстом и вложениями. Значок микрофона запускает диктовку после настройки речевого движка Windows или Whisper.",
                ("Открыть чат", "ai"), ("Настроить диктовку", "settings:speech")),
            new("control", "Погода и управление Windows",
                "Выберите город для прогноза. В «Управлении» доступны звук, Wi-Fi, Bluetooth, Happ VPN, уведомления и питание. Заряд совместимых Bluetooth-устройств виден прямо на чёлке.",
                ("Открыть погоду", "weather"), ("Открыть управление", "control")),
            new("settings", "Настройте чёлку под себя",
                "Настройте оформление, вкладки, мониторы и горячие клавиши. Здесь же можно включить автозапуск, проверить обновления и подключить OBS, Claude Code и Codex. " +
                "Повторить тур можно из меню значка рядом с часами.",
                ("Открыть настройки", "settings"))
        ];
        SourceInitialized += (_, _) => Native.MatchTitleBarToBackground(this);
        Activated += (_, _) => Topmost = true;
        Deactivated += (_, _) => Topmost = false;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        RenderStep();
    }

    private void RenderStep()
    {
        var step = steps[index];
        var last = index == steps.Length - 1;
        StepIcon.Content = Icons.Tile(step.Icon, 44);
        StepProgress.Text = $"{index + 1} из {steps.Length}";
        StepTitle.Text = step.Title;
        StepDetail.Text = step.Detail;
        StepActions.Children.Clear();
        foreach (var (label, tab) in step.Actions) StepActions.Children.Add(Ui.Button(label, () => navigate(tab)));
        StepBody.ScrollToTop();
        BackButton.IsEnabled = index > 0;
        NextButton.Content = last ? "Готово" : "Далее";
        SkipButton.Visibility = last ? Visibility.Hidden : Visibility.Visible;
    }

    private void SkipClicked(object sender, RoutedEventArgs e) => Close();

    private void BackClicked(object sender, RoutedEventArgs e)
    {
        index--;
        RenderStep();
    }

    private void NextClicked(object sender, RoutedEventArgs e)
    {
        if (index == steps.Length - 1) Close();
        else
        {
            index++;
            RenderStep();
        }
    }
}
