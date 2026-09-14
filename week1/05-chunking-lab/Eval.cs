using D2;
using D5.Chunking;

namespace D5;

/// <summary>One row of the sweep: a chunker at a chunk size and its retrieval recall@k.</summary>
public record EvalRow(string Chunker, int Size, int Chunks, double Recall);

/// <summary>
/// The measurement that answers "what's the right chunk size?" — empirically, not by
/// opinion. Each eval question carries a golden substring that appears verbatim in the
/// document; retrieval succeeds when that substring shows up in any top-k chunk. The
/// ground truth is chunk-size-agnostic, so the same questions score every chunker and
/// every size fairly.
/// </summary>
public static class Eval
{
    /// <summary>Parses a TSV of "question \t golden-substring". Skips blank and header lines.</summary>
    public static (string Q, string Golden)[] LoadQuestions(string path)
        => File.ReadAllLines(path)
            .Skip(1) // header
            .Where(l => l.Contains('\t'))
            .Select(l =>
            {
                var parts = l.Split('\t', 2);
                return (parts[0].Trim(), parts[1].Trim());
            })
            .ToArray();

    public static async Task<List<EvalRow>> SweepAsync(
        IEmbedder embedder,
        string doc,
        (string Q, string Golden)[] questions,
        int[] sizes,
        int topK = 3,
        CancellationToken ct = default)
    {
        // Question vectors don't depend on chunk size — embed them once.
        var qVectors = await embedder.EmbedAsync(questions.Select(q => q.Q).ToList(), inputType: "query", ct);

        var rows = new List<EvalRow>();
        foreach (var size in sizes)
        {
            var word = D2.Chunking.Chunker.Chunk(doc, size, overlapWords: 25);
            rows.Add(await RunOneAsync(embedder, questions, qVectors, "word", word, size, topK, ct));

            var semantic = SemanticChunker.Chunk(doc, size, overlapWords: 25);
            rows.Add(await RunOneAsync(embedder, questions, qVectors, "semantic", semantic, size, topK, ct));

            // The "wrong way" baseline: naive char-count chunking (D3), which splits
            // mid-sentence. ~6 chars/word maps a word budget to a char budget.
            var charChunks = D3.Chunking.CharChunker.Chunk(doc, maxChars: size * 6, overlapChars: 25);
            rows.Add(await RunOneAsync(embedder, questions, qVectors, "char", charChunks, size, topK, ct));
        }
        return rows;
    }

    private static async Task<EvalRow> RunOneAsync(
        IEmbedder embedder,
        (string Q, string Golden)[] questions,
        IReadOnlyList<float[]> qVectors,
        string label,
        IReadOnlyList<string> chunks,
        int size,
        int topK,
        CancellationToken ct)
    {
        var chunkVectors = await embedder.EmbedAsync(chunks, inputType: "passage", ct);
        var hits = 0;
        for (var i = 0; i < questions.Length; i++)
        {
            var top = Similarity.TopK(qVectors[i], chunkVectors, topK);
            var found = top.Any(idx => chunks[idx].Contains(questions[i].Golden, StringComparison.Ordinal));
            if (found) hits++;
        }
        return new EvalRow(label, size, chunks.Count, (double)hits / questions.Length);
    }
}
