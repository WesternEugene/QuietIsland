using System.Collections.Concurrent;

namespace QuietIsland.Core;

public record AgentRequest(string Id, string Source, string Title, string Message, string[] Options, bool AllowText = false);
public sealed class AgentBroker
{
    private readonly ConcurrentDictionary<string, (AgentRequest Request, TaskCompletionSource<string> Completion)> pending = new();
    public IReadOnlyList<AgentRequest> Pending => pending.Values.Select(x => x.Request).ToList();
    public event Action? Changed;
    public async Task<string> AskAsync(AgentRequest request, TimeSpan timeout, CancellationToken cancellation = default)
    {
        if (request.Options.Length == 0 || request.Options.Length > 12 || request.Id.Length > 100) throw new ArgumentException("Invalid agent request");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(request.Id, (request, completion))) throw new ArgumentException("Duplicate request id");
        Changed?.Invoke();
        try { return await completion.Task.WaitAsync(timeout, cancellation); }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException) { return "deny"; }
        finally { pending.TryRemove(request.Id, out _); Changed?.Invoke(); }
    }
    public bool Answer(string id, string option)
    {
        if (!pending.TryGetValue(id, out var item) || (!item.Request.Options.Contains(option, StringComparer.Ordinal) && !(item.Request.AllowText && option.Length is > 0 and <= 20000))) return false;
        return item.Completion.TrySetResult(option);
    }
}
