using Microsoft.Extensions.Configuration;

namespace D1.Providers;

/// <summary>
/// NVIDIA NIM (hosted) — OpenAI-compatible chat.completions endpoint.
/// Config: NVIDIA_API_KEY (bearer, env var or user-secret), optional NVIDIA_NIM_MODEL.
/// Endpoint is fixed to NVIDIA's hosted NIM gateway; self-hosted NIM would
/// differ only in base URL.
/// </summary>
public class NvidiaNimClient : OpenAiCompatibleClient
{
    private const string DefaultUrl = "https://integrate.api.nvidia.com/v1/chat/completions";
    // NVIDIA's current reasoning-instruct model (per NVIDIA's own sample as of
    // Aug 2026). Older defaults often hit HTTP 410 end-of-life.
    private const string DefaultModel = "nvidia/nemotron-3.5-lightning-30b-a3b";

    private readonly Uri _url;
    private readonly string _model;
    private readonly string _token;

    public NvidiaNimClient(HttpClient http, string token, string model, string? baseUrl = null,
        RequestOptions? options = null) : base(http, options)
    {
        _url = new Uri(baseUrl ?? DefaultUrl);
        _model = model;
        _token = token;
    }

    public static NvidiaNimClient FromEnv(HttpClient http, IConfiguration config,
        string? modelOverride = null, RequestOptions? options = null)
    {
        var token = config["NVIDIA_API_KEY"]
            ?? throw new InvalidOperationException("Set NVIDIA_API_KEY (env var or 'dotnet user-secrets set NVIDIA_API_KEY ...')");
        var model = modelOverride ?? config["NVIDIA_NIM_MODEL"] ?? DefaultModel;
        return new NvidiaNimClient(http, token, model, baseUrl: null, options);
    }

    protected override Uri ChatCompletionsUrl => _url;
    protected override string Model => _model;
    protected override string BearerToken => _token;

    // NVIDIA reasoning models return nothing unless thinking is enabled and a
    // reasoning budget is given (mirrors NVIDIA's official Python sample).
    protected override IReadOnlyDictionary<string, object?> ExtraBody()
        => new Dictionary<string, object?>
        {
            ["chat_template_kwargs"] = new { enable_thinking = true },
            ["reasoning_budget"] = 16384,
        };
}
