using Microsoft.Extensions.Configuration;

namespace D2.Providers;

/// <summary>
/// NVIDIA NIM embeddings — OpenAI-compatible /embeddings.
/// Key: NVIDIA_API_KEY (same secret as D1). Model: NVIDIA_EMBEDDING_MODEL,
/// default nvidia/nemotron-3-embed-1b (confirmed live on NVIDIA's catalog).
/// </summary>
public class NvidiNimEmbeddingClient : OpenAiEmbeddingClient
{
    private const string DefaultUrl = "https://integrate.api.nvidia.com/v1/embeddings";
    private const string DefaultModel = "nvidia/nemotron-3-embed-1b";

    private readonly Uri _url;
    private readonly string _model;
    private readonly string _token;

    public NvidiNimEmbeddingClient(HttpClient http, string token, string model, string? baseUrl = null)
        : base(http)
    {
        _url = new Uri(baseUrl ?? DefaultUrl);
        _model = model;
        _token = token;
    }

    public static NvidiNimEmbeddingClient FromEnv(HttpClient http, IConfiguration config)
    {
        var token = config["NVIDIA_API_KEY"]
            ?? throw new InvalidOperationException("Set NVIDIA_API_KEY (env var or 'dotnet user-secrets set NVIDIA_API_KEY ...')");
        var model = config["NVIDIA_EMBEDDING_MODEL"] ?? DefaultModel;
        return new NvidiNimEmbeddingClient(http, token, model);
    }

    protected override Uri EmbeddingsUrl => _url;
    protected override string Model => _model;
    protected override string BearerToken => _token;
}
