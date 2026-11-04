using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using QuietIsland.Models;

namespace QuietIsland.Services;

public sealed class EverythingService : IDisposable
{
    private readonly HwndSource source;
    private readonly SemaphoreSlim gate = new(1, 1);
    private TaskCompletionSource<IReadOnlyList<SearchResult>>? pending;
    private uint replyId = 0x51490000;
    public string Status { get; private set; } = "Everything · готов к поиску";
    public EverythingService()
    {
        source = new HwndSource(new HwndSourceParameters("QuietIsland.Everything") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
        source.AddHook(Hook);
    }
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        await gate.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Everything_SetSearchW(query);
            Everything_SetMax(60);
            Everything_SetOffset(0);
            Everything_SetSort(1);
            Everything_SetRequestFlags(0x1 | 0x2 | 0x4 | 0x10);
            Everything_SetReplyWindow(source.Handle);
            Everything_SetReplyID(++replyId);
            if (!Everything_QueryW(false)) throw new InvalidOperationException("Everything не отвечает. Запустите Everything в фоне (ошибка " + Everything_GetLastError() + ").");
            var result = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellation);
            Status = $"Everything · {Everything_GetTotResults():N0} результатов";
            return result;
        }
        catch (TimeoutException) { Status = "Everything не ответил за 2 с. Проверьте, что индекс загружен."; return []; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        { Status = e is DllNotFoundException ? "Не найден Everything64.dll рядом с приложением." : e.Message; return []; }
        finally { pending = null; gate.Release(); }
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg != 0x4A || pending == null) return IntPtr.Zero;
        try
        {
            if (!Everything_IsQueryReply((uint)msg, wp, lp, replyId)) return IntPtr.Zero;
            var results = new List<SearchResult>();
            var buffer = new StringBuilder(32768);
            for (uint i = 0; i < Everything_GetNumResults(); i++)
            {
                buffer.Clear();
                Everything_GetResultFullPathNameW(i, buffer, (uint)buffer.Capacity);
                var path = buffer.ToString();
                var folder = Everything_IsFolderResult(i);
                var size = Everything_GetResultSize(i, out var s) ? s : -1;
                results.Add(new(Path.GetFileName(path.TrimEnd('\\')), path, folder ? "Папка" : "Файл", size));
            }
            pending.TrySetResult(results);
            handled = true;
            return new IntPtr(1);
        }
        catch (Exception e) { pending.TrySetException(e); return IntPtr.Zero; }
    }
    public void Dispose() { pending?.TrySetCanceled(); source.RemoveHook(Hook); source.Dispose(); }
    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)] private static extern void Everything_SetSearchW(string text);
    [DllImport("Everything64.dll")] private static extern void Everything_SetMax(uint max);
    [DllImport("Everything64.dll")] private static extern void Everything_SetOffset(uint offset);
    [DllImport("Everything64.dll")] private static extern void Everything_SetSort(uint sort);
    [DllImport("Everything64.dll")] private static extern void Everything_SetRequestFlags(uint flags);
    [DllImport("Everything64.dll")] private static extern void Everything_SetReplyWindow(IntPtr handle);
    [DllImport("Everything64.dll")] private static extern void Everything_SetReplyID(uint id);
    [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);
    [DllImport("Everything64.dll")] private static extern uint Everything_GetLastError();
    [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Everything_IsQueryReply(uint message, IntPtr wp, IntPtr lp, uint id);
    [DllImport("Everything64.dll")] private static extern uint Everything_GetNumResults();
    [DllImport("Everything64.dll")] private static extern uint Everything_GetTotResults();
    [DllImport("Everything64.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint Everything_GetResultFullPathNameW(uint index, StringBuilder path, uint size);
    [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Everything_IsFolderResult(uint index);
    [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Everything_GetResultSize(uint index, out long size);
}
