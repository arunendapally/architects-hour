using Microsoft.Extensions.Configuration;

namespace D1.Providers;

/// <summary>
/// OmniRoute local proxy. Config mirrors settings.local.json:
/// ANTHROPIC_BASE_URL, ANTHROPIC_AUTH_TOKEN, ANTHROPIC_MODEL.
/// Values resolve env var > user-secret > default.
/// </summary>
public class OmniRouteClient : OpenAiCompatibleClient
{
    private readonly string _endpoint; // e.g. http://localhost:20128/v1
    private readonly string _model;
    private readonly string _token;

    public OmniRouteClient(HttpClient http, string endpoint, string token, string model,
        double? temperature = null, double? topP = null) : base(http, temperature, topP)
    {
        _endpoint = endpoint.TrimEnd('/');
        _model = model;
        _token = token;
    }

    public static OmniRouteClient FromEnv(HttpClient http, IConfiguration config,
        string? modelOverride = null, double? temperature = null, double? topP = null)
    {
        var endpoint = config["ANTHROPIC_BASE_URL"] ?? "http://localhost:20128/v1";
        var token = config["ANTHROPIC_AUTH_TOKEN"] ?? "omniroute";
        var model = modelOverride ?? config["ANTHROPIC_MODEL"] ?? "auto/best-free";
        return new OmniRouteClient(http, endpoint, token, model, temperature, topP);
    }

    protected override Uri ChatCompletionsUrl => new($"{_endpoint}/chat/completions");
    protected override string Model => _model;
    protected override string BearerToken => _token;
}
