using System.Diagnostics;

namespace D2;

/// <summary>
/// Thin embedding abstraction — one call embeds a batch of texts and returns
/// one vector per input, in the same order. Mirrors D1's IModelClient: the
/// provider is swappable behind one interface, latency measured in one place.
/// </summary>
public interface IEmbedder
{
    /// <param name="inputType">"passage" for document chunks, "query" for user questions. Ignored by providers that don't need it.</param>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, string? inputType = null, CancellationToken ct = default);
    TimeSpan Latency { get; }
}

/// <summary>Embeds a batch and times it so per-provider latency is comparable.</summary>
public abstract class EmbedderBase : IEmbedder
{
    private readonly Stopwatch _sw = new();
    public TimeSpan Latency => _sw.Elapsed;

    protected abstract Task<IReadOnlyList<float[]>> EmbedCoreAsync(
        IReadOnlyList<string> texts, string? inputType, CancellationToken ct);

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, string? inputType = null, CancellationToken ct = default)
    {
        _sw.Restart();
        try
        {
            return await EmbedCoreAsync(texts, inputType, ct);
        }
        finally
        {
            _sw.Stop();
        }
    }
}
