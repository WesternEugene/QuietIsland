using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;
using QuietIsland.Models;
using UglyToad.PdfPig;

namespace QuietIsland.Services;

public sealed class AiService(DesktopState state)
{
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public async Task<string[]> ModelsAsync(CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = Request(HttpMethod.Get, "models");
        using var response = await client.SendAsync(request, deadline.Token); response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
        return doc.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToArray();
    }
    private HttpRequestMessage Request(HttpMethod method, string route)
    {
        var endpoint = state.Data.Settings.AiEndpoint.TrimEnd('/');
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new InvalidOperationException("Укажите корректный API URL в настройках ИИ.");
        var request = new HttpRequestMessage(method, endpoint + "/" + route);
        var key = DesktopState.Unprotect(state.Data.Settings.AiKeyEncrypted);
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, IProgress<string>? progress = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(state.Data.Settings.AiModel)) throw new InvalidOperationException("Выберите модель в настройках ИИ. Можно подключить локальный Ollama или совместимый API.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var messages = new List<object> { new { role = "system", content = state.Data.Settings.AiSystemPrompt } };
        foreach (var message in history.TakeLast(40))
        {
            if (message.Images.Count == 0) { messages.Add(new { role = message.Role, content = (object)message.Text }); continue; }
            var content = new List<object> { new { type = "text", text = message.Text } };
            foreach (var path in message.Images.Take(5))
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 8_000_000) continue;
                var mime = Path.GetExtension(path).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "image/png" };
                content.Add(new { type = "image_url", image_url = new { url = "data:" + mime + ";base64," + Convert.ToBase64String(await File.ReadAllBytesAsync(path, deadline.Token)) } });
            }
            messages.Add(new { role = message.Role, content = (object)content });
        }
        using var request = Request(HttpMethod.Post, "chat/completions");
        request.Content = new StringContent(JsonSerializer.Serialize(new { model = state.Data.Settings.AiModel, messages, stream = true }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Сервис ИИ ответил HTTP {(int)response.StatusCode}. Проверьте URL, модель, ключ и баланс сервиса.");
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
            var content = result.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            progress?.Report(content); return content;
        }
        var answer = new StringBuilder();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
        while (await reader.ReadLineAsync(deadline.Token) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var data = line[5..].Trim(); if (data == "[DONE]") break;
            using var chunk = JsonDocument.Parse(data);
            if (!chunk.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) { answer.Append(content.GetString()); progress?.Report(answer.ToString()); }
        }
        if (answer.Length == 0) throw new InvalidOperationException("Модель вернула пустой ответ.");
        return answer.ToString();
    }
    public static string ExtractAttachment(string path)
    {
        if (new FileInfo(path).Length > 25_000_000) throw new InvalidOperationException("Вложение больше 25 МБ. Выберите меньший файл.");
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".pdf") { using var pdf = PdfDocument.Open(path); return Limit(string.Join("\n", pdf.GetPages().Select(x => x.Text))); }
        if (ext is ".docx" or ".xlsx" or ".pptx")
        {
            using var zip = ZipFile.OpenRead(path);
            var relevant = zip.Entries.Where(e => e.FullName == "word/document.xml" || e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml") || e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml") || e.FullName == "xl/sharedStrings.xml");
            var text = new StringBuilder();
            foreach (var entry in relevant.OrderBy(x => x.FullName))
            {
                if (entry.Length > 10_000_000) throw new InvalidOperationException("Слишком большой документ внутри архива.");
                using var stream = entry.Open(); var xml = XDocument.Load(stream);
                text.AppendLine(string.Join(" ", xml.Descendants().Where(x => x.Name.LocalName is "t" or "v").Select(x => x.Value)));
                if (text.Length > 200_000) break;
            }
            return Limit(text.ToString());
        }
        return Limit(File.ReadAllText(path));
    }
    private static string Limit(string text) => text.Length <= 200_000 ? text : text[..200_000] + "\n[Вложение обрезано до 200 000 символов]";
}
