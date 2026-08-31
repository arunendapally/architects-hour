using D1;
using D1.Providers;
using Microsoft.Extensions.Configuration;

// === D1: First call to a model, from .NET — provider-agnostic CLI ===
// Parse args: --provider, --prompt, --model. Prompt defaults to stdin.

// Config precedence: env var > user-secret > provider default. User secrets live
// outside the repo (%APPDATA%\Microsoft\UserSecrets) so public repos stay safe.
var config = new ConfigurationBuilder()
    .AddUserSecrets(assembly: System.Reflection.Assembly.GetEntryAssembly()!)
    .AddEnvironmentVariables()
    .Build();

var provider = Arg("--provider", "omniroute");
var prompt = Arg("--prompt", null) ?? ReadStdinFallback();
var modelOverride = Arg("--model", null);
var temperature = ArgDouble("--temperature", null);
var topP = ArgDouble("--top-p", null);

if (string.IsNullOrWhiteSpace(prompt))
{
    Console.Error.WriteLine("No prompt given. Pass --prompt or pipe text on stdin.");
    return 1;
}

// Large hosted models (e.g. NVIDIA's big Llama/Nemotron) can exceed the default
// 100s HttpClient timeout, so give network calls a generous budget.
var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

// Everything below can fail on config, network, or provider errors. Report the
// message on stderr and exit non-zero — a stack trace helps nobody here.
try
{
    IModelClient client = provider switch
    {
        "omniroute" => OmniRouteClient.FromEnv(http, config, modelOverride, temperature, topP),
        "nvidia" => NvidiaNimClient.FromEnv(http, config, modelOverride, temperature, topP),
        _ => throw new InvalidOperationException(
            $"Unknown provider: {provider}. Expected 'omniroute' or 'nvidia'."),
    };

    const string system = "You are a helpful assistant.";
    Console.WriteLine($"--- {provider} : {prompt} ---");

    var result = await client.CompleteAsync(system, prompt);

    Console.WriteLine("──────────────────────────────────────────────────────────");
    Console.WriteLine(result.Content.TrimEnd());
    Console.WriteLine("──────────────────────────────────────────────────────────");
    Console.WriteLine($"Input: {result.InputTokens} tokens   " +
                      $"Output: {result.OutputTokens} tokens   " +
                      $"Total: {result.InputTokens + result.OutputTokens}   " +
                      $"Latency: {result.Latency.TotalMilliseconds:F0} ms");
    return 0;
}
catch (TaskCanceledException)
{
    Console.Error.WriteLine($"Request timed out after {http.Timeout.TotalMinutes:F0} minutes.");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// -- helpers -------------------------------------------------------------
string? Arg(string name, string? fallback)
{
    // support both "--name value" and "--name=value"; a following flag is not a value
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            return args[i + 1];
    var eq = args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal));
    return eq != null ? eq[(name.Length + 1)..] : fallback;
}

double? ArgDouble(string name, double? fallback)
{
    var v = Arg(name, null);
    return v != null && double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var d)
        ? d : fallback;
}

string ReadStdinFallback()
    => Console.IsInputRedirected ? Console.In.ReadToEnd().Trim() : "";
