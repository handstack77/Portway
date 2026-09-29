using System.Collections.Concurrent;
using Portway.Core;

namespace Portway.Desktop;

public sealed class AuthenticationBroker(IHostApplicationLifetime lifetime) : IAuthenticationInteraction
{
    sealed record Pending(string Id, string Site, string Host, string Instruction, AuthenticationPrompt[] Prompts, TaskCompletionSource<string[]> Completion);
    readonly ConcurrentDictionary<string, Pending> pending = new();
    public async Task<string[]> Answer(Site site, string instruction, AuthenticationPrompt[] prompts, CancellationToken ct)
    {
        if (pending.Count >= 16)
            throw new InvalidOperationException("인증 요청이 너무 많습니다.");
        var item = new Pending(Guid.NewGuid().ToString("N"), site.Name, site.Host, instruction, prompts, new(TaskCreationOptions.RunContinuationsAsynchronously));
        pending[item.Id] = item;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping);
        try
        {
            return await item.Completion.Task.WaitAsync(linked.Token);
        }
        finally
        {
            pending.TryRemove(item.Id, out _);
        }
    }

    public object[] List() => pending.Values.Select(p => (object)new { p.Id, p.Site, p.Host, p.Instruction, p.Prompts }).ToArray();
    public void Respond(string id, string[]? answers)
    {
        if (!pending.TryGetValue(id, out var item))
            throw new KeyNotFoundException("인증 요청이 만료되었습니다.");
        if (answers == null)
        {
            item.Completion.TrySetCanceled();
            return;
        }

        if (answers.Length != item.Prompts.Length || answers.Any(a => a.Length > 8192))
            throw new ArgumentException("인증 응답이 올바르지 않습니다.");
        item.Completion.TrySetResult(answers);
    }
}
