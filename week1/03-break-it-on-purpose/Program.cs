using D1Prov = D1.Providers;
using D2;
using D2.Providers;
using D3.Chunking;
using Microsoft.Extensions.Configuration;

// === D3: Break it on purpose ===
// Same RAG pipeline as D2, but the chunker is deliberately broken: it slices at a fixed
// character count and ignores sentence boundaries. Run it tiny (50 chars) or huge (5000)
// and record the wrong answers verbatim — those are the post.
// Everything else (embed, cosine search, chat) is inherited from D2 unchanged.

var config = new ConfigurationBuilder()
    .AddUserSecrets(assembly: System.Reflection.Assembly.GetEntryAssembly()!)
    .AddEnvironmentVariables()
    .Build();

var docPath = Arg("--doc", "../02-rag-by-hand/data/sample.txt")!;
var query = Arg("--query", null) ?? ReadStdinFallback();
var charSize = ArgInt("--chars", 50);
var charOverlap = ArgInt("--overlap", 10);
var topK = ArgInt("--top-k", 3);

if (string.IsNullOrWhiteSpace(query))
{
    Console.Error.WriteLine("No query given. Pass --query or pipe text on stdin.");
    return 1;
}

if (charOverlap >= charSize)
{
    Console.Error.WriteLine($"--overlap ({charOverlap}) must be smaller than --chars ({charSize}); " +
                            "otherwise the chunker steps 1 char at a time and embeds the doc thousands of times.");
    return 1;
}

var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

try
{
    var doc = File.ReadAllText(docPath);
    var chunks = CharChunker.Chunk(doc, charSize, charOverlap);
    if (chunks.Count == 0)
    {
        Console.Error.WriteLine($"No text to search in {docPath}.");
        return 1;
    }
    Console.WriteLine($"Document: {docPath}  ({chunks.Count} char-chunks, ~{charSize} chars each)");

    // --- retrieve (reuses D2) --------------------------------------------
    IEmbedder embedder = NvidiNimEmbeddingClient.FromEnv(http, config);

    var chunkVectors = await embedder.EmbedAsync(chunks, inputType: "passage");
    Console.WriteLine($"Embedded {chunks.Count} chunks in {embedder.Latency.TotalMilliseconds:F0} ms ({chunkVectors[0].Length} dims)");

    var queryVectors = await embedder.EmbedAsync(new[] { query }, inputType: "query");
    var top = Similarity.TopK(queryVectors[0], chunkVectors, topK);

    Console.WriteLine("Retrieved (best first): " + string.Join(", ", top));

    // --- answer (reuses D2/D1, guardrail intact so the break shows up in retrieval) ---
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

// -- helpers (same as D2) ------------------------------------------------
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
