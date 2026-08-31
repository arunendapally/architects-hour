using System.Diagnostics;

namespace D1;

/// <summary>One model completion, normalized across providers.</summary>
public record ModelResponse(string Content, int InputTokens, int OutputTokens, TimeSpan Latency);

/// <summary>Thin provider abstraction — one completion = one call.</summary>
public interface IModelClient
{
    Task<ModelResponse> CompleteAsync(string system, string user, CancellationToken ct = default);
}

/// <summary>Runs a provider call and measures latency in one place so it's comparable across providers.</summary>
public abstract class ModelClientBase : IModelClient
{
    protected abstract Task<(string Content, int Input, int Output)> CallCoreAsync(
        string system, string user, CancellationToken ct);

    public async Task<ModelResponse> CompleteAsync(string system, string user, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var (content, input, output) = await CallCoreAsync(system, user, ct);
        sw.Stop();
        return new ModelResponse(content, input, output, sw.Elapsed);
    }
}
