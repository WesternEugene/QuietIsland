using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using QuietIsland.Models;

namespace QuietIsland.Services;

public sealed class ClipboardService(DesktopState state)
{
    private bool writing;
    public event Action? Changed;
    public void Capture()
    {
        if (writing || !state.Data.Settings.ClipboardEnabled) return;
        if (new[] { "keepass", "bitwarden", "1password", "keeper" }.Any(x => Native.ForegroundProcess().Contains(x, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            ClipEntry? entry = null;
            if (System.Windows.Clipboard.ContainsFileDropList()) entry = new() { Kind = "files", Files = System.Windows.Clipboard.GetFileDropList().Cast<string>().ToList() };
            else if (System.Windows.Clipboard.ContainsImage())
            {
                var bitmap = System.Windows.Clipboard.GetImage();
                var folder = Path.Combine(DesktopState.DataDirectory, "clipboard"); Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                entry = new() { Kind = "image", ImagePath = path };
            }
            else if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 1_000_000) return;
                var old = state.Data.Clipboard.FirstOrDefault(x => x.Kind == "text" && x.Text == text);
                entry = old ?? new() { Text = text };
                if (old != null) state.Data.Clipboard.Remove(old);
            }
            if (entry == null) return;
            state.Data.Clipboard.Insert(0, entry);
            var oldEntries = state.Data.Clipboard.Where(x => !x.Pinned).Skip(state.Data.Settings.ClipboardLimit).ToArray();
            foreach (var old in oldEntries) { state.Data.Clipboard.Remove(old); RemoveOwnedImage(old); }
            state.Touch(); Changed?.Invoke();
        }
        catch (Exception e) when (e is COMException or IOException or InvalidOperationException) { Log.Write("Clipboard", e); }
    }
    public void Copy(ClipEntry entry, IntPtr pasteTarget)
    {
        writing = true;
        try
        {
            if (entry.Kind == "image" && File.Exists(entry.ImagePath))
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(entry.ImagePath); image.EndInit(); image.Freeze();
                System.Windows.Clipboard.SetImage(image);
            }
            else if (entry.Kind == "files") { var list = new StringCollection(); list.AddRange(entry.Files.Where(x => File.Exists(x) || Directory.Exists(x)).ToArray()); System.Windows.Clipboard.SetFileDropList(list); }
            else System.Windows.Clipboard.SetText(entry.Text);
            if (state.Data.Settings.ClipboardAutoPaste) Native.Paste(pasteTarget);
        }
        finally { writing = false; }
    }
    public void Remove(ClipEntry entry) { state.Data.Clipboard.Remove(entry); RemoveOwnedImage(entry); state.Touch(); Changed?.Invoke(); }
    private static void RemoveOwnedImage(ClipEntry entry)
    {
        if (entry.ImagePath == null) return;
        var folder = Path.GetFullPath(Path.Combine(DesktopState.DataDirectory, "clipboard")) + Path.DirectorySeparatorChar;
        try { if (Path.GetFullPath(entry.ImagePath).StartsWith(folder, StringComparison.OrdinalIgnoreCase)) File.Delete(entry.ImagePath); }
        catch (IOException) { }
    }
}
