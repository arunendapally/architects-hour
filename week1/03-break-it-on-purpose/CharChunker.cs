namespace D3.Chunking;

/// <summary>
/// The deliberate mistake. D2's chunker is sentence-aware on purpose; this one is
/// char-blind on purpose — it slices at exactly <paramref name="maxChars"/> characters
/// no matter where a sentence ends. That reproduces the doc's mistake #1: chunking that
/// splits sentences in half. Feed it 50 chars and every chunk is a shard of a sentence;
/// feed it 5000 and the whole doc is one blob.
/// </summary>
public static class CharChunker
{
    public static IReadOnlyList<string> Chunk(string text, int maxChars = 250, int overlapChars = 25)
    {
        var flat = text.Replace("\r\n", "\n").Replace("\n", " ").Trim();
        var chunks = new List<string>();
        var step = Math.Max(1, maxChars - overlapChars);

        for (int i = 0; i < flat.Length; i += step)
        {
            var len = Math.Min(maxChars, flat.Length - i);
            chunks.Add(flat.Substring(i, len).Trim());
        }
        return chunks;
    }
}
