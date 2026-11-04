using System.Windows;
using System.Windows.Controls;
using QuietIsland.Controls;
using QuietIsland.Core;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class ToolsView : IslandView
{
    private readonly TextBox text = Ui.Input("Текст для преобразования", multiline: true);
    private readonly StackPanel qrPanel = new();
    public ToolsView(IslandContext c, MainWindow window) : base(c)
    {
        text.Height = 150;
        var capture = Ui.Card(Ui.Stack(Ui.Row(Ui.AsyncButton("⌗ Скриншот", () => window.ScreenshotAsync("shot"), C.Error), Ui.AsyncButton("Текст · OCR", () => window.ScreenshotAsync("ocr"), C.Error), Ui.AsyncButton("QR-код", () => window.ScreenshotAsync("qr"), C.Error), Ui.AsyncButton("Пипетка", () => window.ScreenshotAsync("color"), C.Error)), Ui.Row(Ui.AsyncButton(c.Recording.IsRecording ? "■ Остановить MP4" : "● Запись MP4", () => window.ToggleRecordingAsync(), C.Error), Ui.AsyncButton("Запись GIF", () => window.ToggleRecordingAsync(true), C.Error), Ui.Button("Открыть папку", () => { Directory.CreateDirectory(C.Settings.CaptureFolder); SystemService.Open(C.Settings.CaptureFolder); }))));
        var transforms = new WrapPanel();
        foreach (var (key, label) in new[] { ("layout", "Раскладка"), ("upper", "АБВ"), ("lower", "абв"), ("title", "Заглавные"), ("translit", "Транслит"), ("trim", "Пробелы"), ("json", "JSON"), ("base64", "Base64 ↑"), ("base64-decode", "Base64 ↓"), ("url", "URL ↑"), ("url-decode", "URL ↓") }) transforms.Children.Add(Ui.Button(label, () => { try { text.Text = TextTransforms.Apply(key, text.Text); } catch { C.Notify("Этот текст не подходит для выбранного преобразования"); } }));
        var actions = Ui.Row(Ui.Button("Скопировать", () => { if (text.Text.Length > 0) { System.Windows.Clipboard.SetText(text.Text); C.Notify("Текст скопирован"); } }), Ui.Button("Вставить без форматирования", () => { if (text.Text.Length > 0) { System.Windows.Clipboard.SetText(text.Text); C.Collapse(); Native.Paste(C.PasteTarget); } }), Ui.Button("Сделать QR", MakeQr));
        foreach (var (instruction, label) in new[] { ("Переведи текст на английский язык", "Перевести"), ("Исправь ошибки в тексте", "Исправить"), ("Сократи текст, сохранив смысл", "Сократить"), ("Объясни текст понятным языком", "Объяснить"), ("Подготовь вежливый ответ на этот текст", "Ответить") }) actions.Children.Add(Ui.Button("✧ " + label, () => { C.AiDraft = instruction + ":\n" + text.Text; C.Go("ai"); }));
        var colors = Ui.Row(); foreach (var color in C.State.Data.Colors) { var b = Ui.Button(color, () => { System.Windows.Clipboard.SetText(color); C.Notify(color + " скопирован"); }); b.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)); colors.Children.Add(b); }
        Content = Ui.Scroll(Ui.Stack(text, transforms, actions, qrPanel, capture, Ui.Text("НЕДАВНИЕ ЦВЕТА", 10, "Muted"), colors));
    }
    private void MakeQr()
    {
        try
        {
            if (text.Text.Length == 0) return; var image = CaptureService.CreateQr(text.Text); qrPanel.Children.Clear(); qrPanel.Children.Add(new Image { Source = image, Width = 220, Height = 220 }); qrPanel.Children.Add(Ui.Button("Сохранить QR", () => { var path = C.Capture.NewPath(".png"); CaptureService.SavePng(image, path); C.Notify("QR сохранён: " + Path.GetFileName(path)); }));
        }
        catch { C.Notify("Текст слишком длинный для QR-кода"); }
    }
    public override void FocusInput() => text.Focus();
}

