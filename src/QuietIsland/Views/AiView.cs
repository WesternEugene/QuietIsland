using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuietIsland.Controls;
using QuietIsland.Models;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class AiView : IslandView
{
    private readonly StackPanel messages = new();
    private readonly ScrollViewer scroll;
    private readonly TextBox input = Ui.Input("Сообщение ИИ · Ctrl+Enter — отправить", multiline: true);
    private readonly TextBlock attachments = Ui.Text("", 10, "Muted"), status = Ui.Text("", 11, "Muted");
    private readonly List<string> files = [];
    private CancellationTokenSource? request;
    private bool busy;
    public AiView(IslandContext c) : base(c)
    {
        var root = new DockPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; var clear = Ui.Button("Новый чат", () => { request?.Cancel(); C.State.Data.Chat.Clear(); C.State.Touch(); Build(); }); DockPanel.SetDock(clear, Dock.Right); top.Children.Add(clear); top.Children.Add(Ui.Heading("Чат")); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        input.Height = 76; input.Text = C.AiDraft; C.AiDraft = "";
        var send = Ui.AsyncButton("Отправить ↑", SendAsync, C.Error, true); var stop = Ui.Button("Стоп", () => request?.Cancel());
        var bottom = Ui.Stack(attachments, input, Ui.Row(Ui.Button("+ Файл", Attach), Ui.Button("Вставить картинку", PasteImage), send, stop, Ui.Button("Настройки ИИ", () => C.Go("settings:ai"))), status); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        scroll = Ui.Scroll(messages); root.Children.Add(scroll); Content = root;
        input.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; try { await SendAsync(); } catch (Exception error) { C.Error(error); } } };
        AllowDrop = true; Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { foreach (var path in paths.Take(5)) AddFile(path); e.Handled = true; } };
        Build();
    }
    private void Build()
    {
        messages.Children.Clear();
        if (C.State.Data.Chat.Count == 0) { var hint = Ui.Text("Выберите локальную или облачную модель в настройках ИИ.", 12, "Muted"); hint.Margin = new Thickness(8, 20, 8, 0); messages.Children.Add(hint); }
        foreach (var message in C.State.Data.Chat)
        {
            var text = Ui.Text(message.DisplayText.Length > 0 ? message.DisplayText : message.Text, 13); text.IsEnabled = true;
            var copy = Ui.Button("Копировать", () => { if (message.Text.Length > 0) System.Windows.Clipboard.SetText(message.Text); }); copy.Padding = new Thickness(7, 3, 7, 3);
            messages.Children.Add(Ui.Card(Ui.Stack(Ui.Text(message.Role == "user" ? "ВЫ" : "ПОМОЩНИК", 10, message.Role == "user" ? "Muted" : "Accent"), text, copy), 14));
        }
        scroll?.ScrollToEnd();
    }
    private void Attach()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Документы и изображения|*.txt;*.md;*.json;*.csv;*.pdf;*.docx;*.xlsx;*.pptx;*.png;*.jpg;*.jpeg;*.webp|Все файлы|*.*" };
        if (dialog.ShowDialog() == true) foreach (var file in dialog.FileNames.Take(5)) AddFile(file);
    }
    private void AddFile(string path) { if (File.Exists(path) && !files.Contains(path)) files.Add(path); attachments.Text = string.Join(" · ", files.Select(Path.GetFileName)); }
    private void PasteImage()
    {
        var image = System.Windows.Clipboard.ContainsImage() ? System.Windows.Clipboard.GetImage() : null;
        if (image == null) { C.Notify("В буфере нет изображения"); return; }
        var folder = Path.Combine(DesktopState.DataDirectory, "attachments"); var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png"); CaptureService.SavePng(image, path); AddFile(path);
    }
    private async Task SendAsync()
    {
        if (busy || string.IsNullOrWhiteSpace(input.Text)) return;
        busy = true; request = new(); var token = request.Token;
        ChatMessage? answer = null;
        try
        {
            var display = input.Text; var text = display; var images = new List<string>();
            foreach (var path in files.Take(5))
            {
                if (Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp")
                {
                    if (new FileInfo(path).Length > 8_000_000) throw new InvalidOperationException("Изображение больше 8 МБ.");
                    var folder = Path.Combine(DesktopState.DataDirectory, "attachments"); Directory.CreateDirectory(folder); var copy = Path.Combine(folder, Guid.NewGuid().ToString("N") + Path.GetExtension(path)); File.Copy(path, copy); images.Add(copy);
                }
                else text += "\n\n[Файл: " + Path.GetFileName(path) + "]\n" + await Task.Run(() => AiService.ExtractAttachment(path), token);
                display += "\nФайл: " + Path.GetFileName(path);
            }
            C.State.Data.Chat.Add(new() { Role = "user", Text = text, DisplayText = display, Images = images });
            var history = C.State.Data.Chat.ToArray(); answer = new() { Role = "assistant" }; C.State.Data.Chat.Add(answer);
            input.Clear(); files.Clear(); attachments.Text = ""; C.State.Touch(); Build(); status.Text = "Модель отвечает…";
            var block = ((messages.Children[^1] as Border)?.Child as StackPanel)?.Children[1] as TextBlock;
            var progress = new Progress<string>(value => { answer.Text = value; if (!Disposed && block != null) { block.Text = value; scroll.ScrollToEnd(); } });
            answer.Text = await C.Ai.CompleteAsync(history, progress, token); C.State.Touch(); if (!Disposed) { Build(); status.Text = "Готово · контекст последних 40 сообщений"; }
        }
        catch (OperationCanceledException) { if (!Disposed) status.Text = "Остановлено"; }
        catch (Exception e) { if (!Disposed) status.Text = e is InvalidOperationException ? e.Message : "Нет ответа сервиса · проверьте подключение и настройки"; Log.Write("AI chat", e); }
        finally { if (answer != null && answer.Text.Length == 0) C.State.Data.Chat.Remove(answer); C.State.Touch(); busy = false; }
    }
    public override void FocusInput() => input.Focus();
    public override void Dispose() { request?.Cancel(); base.Dispose(); }
}
