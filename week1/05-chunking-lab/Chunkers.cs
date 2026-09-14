namespace D5.Chunking;

/// <summary>
/// The "right way" to chunk, built to answer the D2 reader question. Instead of a raw
/// word counter (which can split a meaning unit in half), it splits on meaning first:
/// paragraphs are kept whole, and only a paragraph too long to fit is broken into
/// sentences. A paragraph is already a coherent unit; a fixed count is not.
/// </summary>
public static class SemanticChunker
{
    /// <summary>
    /// Chunks <paramref name="document"/> by paragraph first, then sentence. A paragraph
    /// under <paramref name="maxWords"/> is emitted whole; only an oversized paragraph is
    /// handed to D2's sentence-aware chunker, which merges sentences (with overlap)
    /// toward the target. The paragraph pass is the only new idea here.
    /// </summary>
    public static IReadOnlyList<string> Chunk(string document, int maxWords = 250, int overlapWords = 25)
    {
        var chunks = new List<string>();
        foreach (var paragraph in SplitParagraphs(document))
        {
            if (CountWords(paragraph) <= maxWords)
            {
                chunks.Add(paragraph);
                continue;
            }
            chunks.AddRange(D2.Chunking.Chunker.Chunk(paragraph, maxWords, overlapWords));
        }
        return chunks;
    }

    private static IReadOnlyList<string> SplitParagraphs(string text)
        => text
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace("\n", " ").Trim())
            .Where(p => p.Length > 0)
            .ToList();

    private static int CountWords(string s)
        => string.IsNullOrWhiteSpace(s)
            ? 0
            : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
