using D4;
using Microsoft.Extensions.Configuration;

// === D4: Your first tool-calling agent ===
// One function the model can call (read_file, with a real filesystem side effect),
// and a hand-built loop that lets it decide when to use it. When the question's
// answer lives in a file, the model calls read_file, gets the contents, and answers
// from them. When it can answer directly, it doesn't call anything.

var config = new ConfigurationBuilder()
    .AddUserSecrets(assembly: System.Reflection.Assembly.GetEntryAssembly()!)
    .AddEnvironmentVariables()
    .Build();

var prompt = Arg("--prompt", null) ?? ReadStdinFallback();
var maxTurns = ArgInt("--max-turns", 8);
var raw = HasFlag("--raw");

if (string.IsNullOrWhiteSpace(prompt))
{
    Console.Error.WriteLine("No prompt given. Pass --prompt or pipe text on stdin.");
    return 1;
}

var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

try
{
    var client = new ToolAgentClient(http, config, raw);

    var system =
        "You are a helpful assistant. A file called 'data/note.txt' holds the quarterly " +
        "notes and is located relative to this app's working directory. When the user asks " +
        "about 'the notes' or 'the Q3 note' (product launch, headcount, budget, data center), " +
        "call the read_file tool on 'data/note.txt' and answer from what you actually find " +
        "in it — do not invent contents. If the file cannot be read, say so. If the question " +
        "does not require the file, answer directly.";

    var result = await client.RunAsync(system, prompt, Tools.Schemas(), call => Task.FromResult(Tools.Execute(call)), maxTurns);

    Console.WriteLine("──────────────────────────────────────────────────────────");
    Console.WriteLine(result.Content.TrimEnd());
    Console.WriteLine("──────────────────────────────────────────────────────────");
    Console.WriteLine($"Input: {result.InputTokens} tokens   Output: {result.OutputTokens} tokens   " +
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

// -- helpers (same as D1-D3) ----------------------------------------------
string? Arg(string name, string? fallback)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            return args[i + 1];
    var eq = args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal));
    return eq != null ? eq[(name.Length + 1)..] : fallback;
}

int ArgInt(string name, int fallback)
    => int.TryParse(Arg(name, null), out var n) ? n : fallback;

bool HasFlag(string name) => args.Contains(name);

string ReadStdinFallback()
    => Console.IsInputRedirected ? Console.In.ReadToEnd().Trim() : "";
