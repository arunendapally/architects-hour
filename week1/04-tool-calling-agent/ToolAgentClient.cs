using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace D4;

/// <summary>One tool invocation the model requested.</summary>
public record ToolCall(string Id, string Name, string Arguments);

/// <summary>A final model answer after the tool loop finishes.</summary>
public record AgentResult(string Content, int InputTokens, int OutputTokens, TimeSpan Latency);

/// <summary>
/// The new thing on Day 4: an OpenAI-compatible chat loop that can call tools.
/// D1's IModelClient is single-shot (system+user -> one answer); it can't send a
/// `tools` array or continue after a tool result. So the tool loop is new wire code.
///
/// Loop: send messages+tools -> model replies with tool_calls -> execute each tool,
/// append the results as `tool`-role messages, and repeat until the model answers
/// with plain text. This is the agentic loop, built by hand.
/// </summary>
public class ToolAgentClient
{
    private readonly HttpClient _http;
    private readonly Uri _url;
    private readonly string _model;
    private readonly string _token;
    private readonly bool _raw;

    public ToolAgentClient(HttpClient http, IConfiguration config, bool raw = false)
    {
        _http = http;
        _token = config["NVIDIA_API_KEY"]
            ?? throw new InvalidOperationException("Set NVIDIA_API_KEY (env var or 'dotnet user-secrets set NVIDIA_API_KEY ...')");
        _model = config["NVIDIA_NIM_MODEL"] ?? "nvidia/nemotron-3.5-lightning-30b-a3b";
        _url = new Uri("https://integrate.api.nvidia.com/v1/chat/completions");
        _raw = raw;
    }

    /// <param name="tools">JSON tool schemas ({"type":"function",...}) to advertise.</param>
    /// <param name="executor">Runs a requested tool call and returns its result text.</param>
    public async Task<AgentResult> RunAsync(
        string system,
        string user,
        IReadOnlyList<object> tools,
        Func<ToolCall, Task<string>> executor,
        int maxTurns = 8,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // messages grows across turns: system, user, then assistant tool_calls
        // + tool results appended as the loop goes. Last element is always a
        // user-role question or a tool result, never a dangling assistant turn.
        var messages = new List<object>
        {
            new { role = "system", content = system },
            new { role = "user", content = user },
        };

        int input = 0, output = 0;
        for (int turn = 0; turn < maxTurns; turn++)
        {
            var (reply, content, toolCalls, inTok, outTok) = await ChatAsync(messages, tools, ct);
            input += inTok;
            output += outTok;

            if (toolCalls.Count == 0)
            {
                sw.Stop();
                return new AgentResult(content, input, output, sw.Elapsed);
            }

            // Replay the assistant message verbatim so the model sees its own
            // request, then answer each tool call with a tool-role message.
            messages.Add(reply);
            foreach (var call in toolCalls)
            {
                string result;
                try
                {
                    result = await executor(call);
                }
                catch (Exception ex)
                {
                    result = $"Error: {ex.Message}";
                }
                Console.Error.WriteLine($"[tool] {call.Name}({call.Arguments}) -> {Truncate(result, 120)}");
                messages.Add(new { role = "tool", tool_call_id = call.Id, content = result });
            }
        }

        throw new InvalidOperationException(
            $"Model still asking for tools at the {maxTurns}-turn limit; raise --max-turns or simplify the question.");
    }

    private async Task<(JsonElement Reply, string Content, List<ToolCall> Calls, int Input, int Output)>
        ChatAsync(List<object> messages, IReadOnlyList<object> tools, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["tools"] = tools,
            // NVIDIA reasoning models return nothing unless thinking is enabled.
            ["chat_template_kwargs"] = new { enable_thinking = true },
            ["reasoning_budget"] = 4096,
        };

        var body = JsonSerializer.Serialize(payload);
        if (_raw)
        {
            Console.Error.WriteLine("→ POST " + _url);
            Console.Error.WriteLine("  Authorization: Bearer <redacted>");
            Console.Error.WriteLine(Prettify(body));
            Console.Error.WriteLine();
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {raw}");
        if (_raw)
        {
            Console.Error.WriteLine("← HTTP " + (int)resp.StatusCode);
            Console.Error.WriteLine(Prettify(raw));
            Console.Error.WriteLine();
        }

        using var doc = JsonDocument.Parse(raw);
        var message = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        var calls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var tc))
        {
            foreach (var c in tc.EnumerateArray())
            {
                calls.Add(new ToolCall(
                    c.GetProperty("id").GetString()!,
                    c.GetProperty("function").GetProperty("name").GetString()!,
                    c.GetProperty("function").GetProperty("arguments").GetString()!));
            }
        }

        var content = message.TryGetProperty("content", out var ctElem) && ctElem.ValueKind == JsonValueKind.String
            ? ctElem.GetString() ?? ""
            : "";

        int inTok = 0, outTok = 0;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            inTok = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
            outTok = usage.TryGetProperty("completion_tokens", out var o) ? o.GetInt32() : 0;
        }

        return (message.Clone(), content, calls, inTok, outTok);
    }

    private static string Truncate(string s, int n)
        => s.Length <= n ? s : s[..n] + "…";

    private static string Prettify(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
