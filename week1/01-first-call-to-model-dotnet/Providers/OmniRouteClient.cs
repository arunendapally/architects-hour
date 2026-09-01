using Microsoft.Extensions.Configuration;

namespace D1.Providers;

/// <summary>
/// OmniRoute local proxy. Config: OMNIROUTE_BASE_URL, OMNIROUTE_AUTH_TOKEN,
/// OMNIROUTE_MODEL — resolved env var > user-secret > default.
/// Deliberately *not* named ANTHROPIC_* even though OmniRoute's own setup uses those:
/// Claude Code and every Anthropic SDK read those names, so borrowing them means any
/// machine with those tools installed silently redirects this app somewhere else.
/// </summary>
public class OmniRouteClient : OpenAiCompatibleClient
{
    private readonly string _endpoint; // e.g. http://localhost:20128/v1
    private readonly string _model;
    private readonly string _token;

    public OmniRouteClient(HttpClient http, string endpoint, string token, string model,
        RequestOptions? options = null) : base(http, options)
    {
        _endpoint = endpoint.TrimEnd('/');
        _model = model;
        _token = token;
    }

    public static OmniRouteClient FromEnv(HttpClient http, IConfiguration config,
        string? modelOverride = null, RequestOptions? options = null)
    {
        var endpoint = config["OMNIROUTE_BASE_URL"] ?? "http://localhost:20128/v1";
        var token = config["OMNIROUTE_AUTH_TOKEN"] ?? "omniroute";
        var model = modelOverride ?? config["OMNIROUTE_MODEL"] ?? "auto/best-free";
        return new OmniRouteClient(http, endpoint, token, model, options);
    }

    protected override Uri ChatCompletionsUrl => new($"{_endpoint}/chat/completions");
    protected override string Model => _model;
    protected override string BearerToken => _token;
}
