namespace D2.Chunking;

/// <summary>
/// By-hand chunking. A chunker is only a way to slice text; the goal here is to
/// keep sentences intact and land near a target word count with a little overlap,
/// so paragraph- and question-sized units survive embedding. No AI involved.
/// </summary>
public static class Chunker
{
    /// <summary>
    /// Splits a document into overlapping chunks of ~<paramref name="maxWords"/>
    /// words each, cutting on sentence boundaries so a chunk never starts or ends
    /// mid-sentence. Returns chunks in source order.
    /// </summary>
    public static IReadOnlyList<string> Chunk(string document, int maxWords = 250, int overlapWords = 25)
    {
        var sentences = SplitSentences(document);
        var chunks = new List<string>();
        var current = new List<string>();
        var wordCount = 0;

        foreach (var sentence in sentences)
        {
            var sentenceWords = CountWords(sentence);

            // This sentence alone exceeds the target — flush current, emit it whole.
            if (sentenceWords > maxWords)
            {
                Flush();
                chunks.Add(sentence.Trim());
                continue;
            }

            current.Add(sentence);
            wordCount += sentenceWords;

            if (wordCount >= maxWords)
                Flush();
        }
        Flush();

        return chunks;

        void Flush()
        {
            if (current.Count == 0) return;

            // Keep a little overlap so meaning isn't lost at the seam: carry the
            // last ~overlapWords of trailing sentences into the next chunk.
            var tail = 0;
            var tailWords = 0;
            for (int i = current.Count - 1; i >= 0 && tailWords < overlapWords; i--)
            {
                tailWords += CountWords(current[i]);
                tail++;
            }

            chunks.Add(string.Join(' ', current).Trim());
            if (tail >= 1 && tail < current.Count)
            {
                current = current.GetRange(current.Count - tail, tail);
                wordCount = tailWords;
            }
            else
            {
                // Nothing left to carry — the whole chunk was its own tail.
                // wordCount must go with it, or the next chunk starts pre-loaded.
                current.Clear();
                wordCount = 0;
            }
        }
    }

    private static IReadOnlyList<string> SplitSentences(string text)
        => text
            .Replace("\r\n", "\n")
            .Replace("\n", " ")
            .Split('.', '!', '?')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Select(s => s + ".")
            .ToList();

    private static int CountWords(string s)
        => string.IsNullOrWhiteSpace(s)
            ? 0
            : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
