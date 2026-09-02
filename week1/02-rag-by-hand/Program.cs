using D1Prov = D1.Providers;
using D2;
using D2.Chunking;
using D2.Providers;
using Microsoft.Extensions.Configuration;

// === D2: RAG by hand, no framework ===
// Read a document -> chunk it -> embed every chunk -> cosine-search for the
// query -> stuff the top-k chunks into the prompt -> call a chat model (D1's).
// The chat hop is inherited from Day 1; today is the retrieval half.
// One provider end to end: NVIDIA NIM serves both embeddings and chat, so the
// whole pipeline runs on a single key.

var config = new ConfigurationBuilder()
    .AddUserSecrets(assembly: System.Reflection.Assembly.GetEntryAssembly()!)
    .AddEnvironmentVariables()
    .Build();

var docPath = Arg("--doc", "data/sample.txt")!; // non-null: fallback is baked in
var query = Arg("--query", null) ?? ReadStdinFallback();
var topK = ArgInt("--top-k", 3);
var chunkSize = ArgInt("--chunk-size", 250);
var chunkOverlap = ArgInt("--chunk-overlap", 25);

if (string.IsNullOrWhiteSpace(query))
{
    Console.Error.WriteLine("No query given. Pass --query or pipe text on stdin.");
    return 1;
}

// Large hosted models/embeddings can exceed HttpClient's default 100s timeout.
var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

try
{
    var doc = File.ReadAllText(docPath);
    var chunks = Chunker.Chunk(doc, chunkSize, chunkOverlap);
    if (chunks.Count == 0)
    {
        Console.Error.WriteLine($"No text to search in {docPath}.");
        return 1;
    }
    Console.WriteLine($"Document: {docPath}  ({chunks.Count} chunks, ~{chunkSize} words each)");

    // --- retrieve ----------------------------------------------------------
    IEmbedder embedder = NvidiNimEmbeddingClient.FromEnv(http, config);

    var chunkVectors = await embedder.EmbedAsync(chunks, inputType: "passage");
    Console.WriteLine($"Embedded {chunks.Count} chunks in {embedder.Latency.TotalMilliseconds:F0} ms ({chunkVectors[0].Length} dims)");

    var queryVectors = await embedder.EmbedAsync(new[] { query }, inputType: "query");
    var top = Similarity.TopK(queryVectors[0], chunkVectors, topK);

    Console.WriteLine("Retrieved (best first): " + string.Join(", ", top));

    // --- answer ------------------------------------------------------------
    var context = string.Join("\n\n", top.Select(i => $"[chunk {i}]\n{chunks[i]}"));
    var userPrompt = $"Answer the question using ONLY the provided context. If the context " +
                     $"doesn't answer the question, say so plainly.\n\nContext:\n{context}\n\nQuestion: {query}";

    D1.IModelClient client = D1Prov.NvidiaNimClient.FromEnv(http, config);

    var result = await client.CompleteAsync("You are a helpful assistant.", userPrompt);

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

// -- helpers -------------------------------------------------------------
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

string ReadStdinFallback()
    => Console.IsInputRedirected ? Console.In.ReadToEnd().Trim() : "";