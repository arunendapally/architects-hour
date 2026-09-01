using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace D1.Providers;

/// <summary>
/// Shared implementation for providers that expose the OpenAI chat.completions
/// shape (OmniRoute, NVIDIA NIM, OpenAI, Azure all do). Subclasses supply the
/// endpoint, model, and bearer token; this handles POST + response parsing.
/// </summary>
public abstract class OpenAiCompatibleClient : ModelClientBase
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly HttpClient _http;
    private readonly RequestOptions _options;

    protected OpenAiCompatibleClient(HttpClient http, RequestOptions? options = null)
    {
        _http = http;
        _options = options ?? new RequestOptions();
    }

    protected abstract Uri ChatCompletionsUrl { get; }
    protected abstract string Model { get; }
    protected abstract string BearerToken { get; }

    /// <summary>Provider-specific extra request body fields (e.g. NVIDIA reasoning kwargs).</summary>
    protected virtual IReadOnlyDictionary<string, object?> ExtraBody()
        => new Dictionary<string, object?>();

    protected override async Task<(string Content, int Input, int Output)> CallCoreAsync(
        string system, string user, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
        };
        foreach (var (k, v) in ExtraBody())
            payload[k] = v;
        if (_options.Temperature is not null) payload["temperature"] = _options.Temperature;
        if (_options.TopP is not null) payload["top_p"] = _options.TopP;

        var body = JsonSerializer.Serialize(payload);

        // --raw: show exactly what goes out and comes back. Diagnostics go to stderr so
        // stdout stays a clean, pipeable answer. The bearer token is never printed.
        if (_options.Raw)
        {
            Console.Error.WriteLine($"→ POST {ChatCompletionsUrl}");
            Console.Error.WriteLine("  Content-Type: application/json");
            Console.Error.WriteLine("  Authorization: Bearer <redacted>");
            Console.Error.WriteLine(Prettify(body));
            Console.Error.WriteLine();
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);

        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (_options.Raw)
        {
            Console.Error.WriteLine($"← HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
            Console.Error.WriteLine(Prettify(raw));
            Console.Error.WriteLine();
        }

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {raw}");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new InvalidOperationException($"Response had no choices: {raw}");
        var message = choices[0].GetProperty("message");

        // Reasoning models may leave content null and put text in reasoning_content.
        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(content) &&
            message.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
            content = rc.GetString() ?? "";

        // Not every proxied model reports usage; zeros beat crashing after a call that worked.
        int input = 0, output = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            input = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
            output = usage.TryGetProperty("completion_tokens", out var o) ? o.GetInt32() : 0;
        }

        return (content, input, output);
    }

    /// <summary>Indent JSON for reading; hand back the original if it isn't JSON (error pages aren't).</summary>
    private static string Prettify(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, Indented);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
