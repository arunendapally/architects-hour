using D1Prov = D1.Providers;
using D2;
using D2.Providers;
using D5;
using D5.Chunking;
using Microsoft.Extensions.Configuration;

// === Chunking Lab: the right way to chunk, with evidence ===
// Reader question from D2: "is the chunk size a fixed number, and does that lose
// context?" This project answers it with data. It adds a meaning-aware chunker
// (paragraph-then-sentence) and an eval sweep that measures retrieval recall@k
// across chunkers and sizes, so "what's the right size" is a table, not a guess.
//
//   eval    — sweep word, semantic and char chunkers x sizes, print recall@k
//   chunk   — show the chunks a given chunker/size produces
//   answer  — end-to-end spot check: retrieve top-k + chat

var config = new ConfigurationBuilder()
    .AddUserSecrets(assembly: System.Reflection.Assembly.GetEntryAssembly()!)
    .AddEnvironmentVariables()
    .Build();

var mode = Arg("--mode", "eval");
var docPath = Arg("--doc", "data/corpus.txt")!;
var topK = ArgInt("--top-k", 3);

var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

try
{
    var doc = File.ReadAllText(docPath);

    switch (mode)
    {
        case "eval":
            await RunEvalAsync(http, config, doc);
            break;
        case "chunk":
            RunChunk(doc);
            break;
        case "answer":
            await RunAnswerAsync(http, config, doc, topK);
            break;
        default:
            Console.Error.WriteLine($"Unknown --mode '{mode}' (expected eval|chunk|answer).");
            return 1;
    }
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

// -- modes -----------------------------------------------------------------
async Task RunEvalAsync(HttpClient http, IConfiguration config, string doc)
{
    var embedder = NvidiNimEmbeddingClient.FromEnv(http, config);
    var questions = Eval.LoadQuestions(Arg("--questions", "data/eval-questions.tsv")!);
    var sizes = Arg("--sizes", null) is string s
        ? s.Split(',').Select(int.Parse).ToArray()
        : new[] { 10, 20, 50, 100, 250, 500 };

    Console.WriteLine($"Corpus: {docPath}  Questions: {questions.Length}  top-k: {topK}");
    var rows = await Eval.SweepAsync(embedder, doc, questions, sizes, topK);

    Console.WriteLine();
    Console.WriteLine($"{"chunker",-9}{"size",6}{"chunks",8}{"recall@k",10}");
    Console.WriteLine(new string('-', 34));
    foreach (var r in rows)
        Console.WriteLine($"{r.Chunker,-9}{r.Size,6}{r.Chunks,8}{(int)(r.Recall * 100),6}%");
}

void RunChunk(string doc)
{
    var chunker = Arg("--chunker", "semantic")!;
    var size = ArgInt("--size", 250);
    var chunks = ChunkWith(chunker, doc, size, ArgInt("--overlap", 25));

    Console.WriteLine($"{chunker} chunker, {size} words: {chunks.Count} chunks\n");
    for (var i = 0; i < chunks.Count; i++)
        Console.WriteLine($"[{i}] {chunks[i]}\n");
}

async Task RunAnswerAsync(HttpClient http, IConfiguration config, string doc, int topK)
{
    var query = Arg("--query", null) ?? ReadStdinFallback();
    if (string.IsNullOrWhiteSpace(query))
        throw new ArgumentException("No query given for --mode answer. Pass --query or pipe stdin.");

    var chunks = ChunkWith(Arg("--chunker", "semantic")!, doc, ArgInt("--size", 250), ArgInt("--overlap", 25));

    var embedder = NvidiNimEmbeddingClient.FromEnv(http, config);
    var chunkVectors = await embedder.EmbedAsync(chunks, inputType: "passage");
    var queryVectors = await embedder.EmbedAsync(new[] { query }, inputType: "query");
    var top = Similarity.TopK(queryVectors[0], chunkVectors, topK);

    Console.WriteLine($"Retrieved (best first): {string.Join(", ", top)}");
    var context = string.Join("\n\n", top.Select(i => $"[chunk {i}]\n{chunks[i]}"));
    var userPrompt = $"Answer the question using ONLY the provided context. If the context " +
                     $"doesn't answer the question, say so plainly.\n\nContext:\n{context}\n\nQuestion: {query}";

    D1.IModelClient client = D1Prov.NvidiaNimClient.FromEnv(http, config);
    var result = await client.CompleteAsync("You are a helpful assistant.", userPrompt);

    Console.WriteLine("──────────────────────────────────────────────────────────");
    Console.WriteLine(result.Content.TrimEnd());
    Console.WriteLine("──────────────────────────────────────────────────────────");
    Console.WriteLine($"Input: {result.InputTokens}   Output: {result.OutputTokens}   " +
                      $"Latency: {result.Latency.TotalMilliseconds:F0} ms");
}

// -- chunker selection -----------------------------------------------------
// The same three chunkers the eval sweeps, so `--mode chunk --chunker char` shows you
// the shredded chunks behind the char row of the table. `size` is a word budget; the
// char chunker gets it as characters at ~6 chars/word, matching Eval.SweepAsync.
IReadOnlyList<string> ChunkWith(string chunker, string doc, int size, int overlap)
    => chunker switch
    {
        "word" => D2.Chunking.Chunker.Chunk(doc, size, overlap),
        "semantic" => SemanticChunker.Chunk(doc, size, overlap),
        "char" => D3.Chunking.CharChunker.Chunk(doc, size * 6, overlap),
        _ => throw new ArgumentException($"Unknown --chunker '{chunker}' (expected word|semantic|char).")
    };

// -- helpers (same as D1-D4) ----------------------------------------------
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
