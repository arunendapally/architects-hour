using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace D2.Providers;

/// <summary>
/// Shared implementation for providers that expose the OpenAI embeddings shape
/// (POST /embeddings with {model, input:[...]}, returns data[i].embedding).
/// NVIDIA NIM is the only provider wired up today; subclasses supply endpoint,
/// model, and bearer, so a second one is a ~15-line subclass.
/// Mirrors D1's OpenAiCompatibleClient.
/// </summary>
public abstract class OpenAiEmbeddingClient : EmbedderBase
{
    private readonly HttpClient _http;

    protected OpenAiEmbeddingClient(HttpClient http) => _http = http;

    protected abstract Uri EmbeddingsUrl { get; }
    protected abstract string Model { get; }
    protected abstract string BearerToken { get; }

    protected override async Task<IReadOnlyList<float[]>> EmbedCoreAsync(
        IReadOnlyList<string> texts, string? inputType, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["input"] = texts,
        };
        if (inputType is not null)
            payload["input_type"] = inputType;

        using var req = new HttpRequestMessage(HttpMethod.Post, EmbeddingsUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);

        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {raw}");

        using var doc = JsonDocument.Parse(raw);
        var data = doc.RootElement.GetProperty("data");

        // data is ordered by input index; read each embedding into a float[].
        var results = new float[data.GetArrayLength()][];
        var i = 0;
        foreach (var item in data.EnumerateArray())
        {
            var vector = item.GetProperty("embedding");
            var arr = new float[vector.GetArrayLength()];
            var j = 0;
            foreach (var v in vector.EnumerateArray())
                arr[j++] = v.GetSingle();
            results[i++] = arr;
        }

        return results;
    }
}
