namespace D2;

/// <summary>
/// By-hand vector search. Cosine similarity, computed directly: normalize every
/// vector to unit length (L2), then cosine similarity = the dot product. No
/// framework, no vector DB — this is the whole retrieval layer.
/// </summary>
public static class Similarity
{
    /// <summary>Returns indices ranked by cosine similarity to the query, best first.</summary>
    public static IReadOnlyList<int> TopK(
        float[] query, IReadOnlyList<float[]> vectors, int k)
    {
        NormalizeInPlace(query);
        var scores = new (int Index, float Score)[vectors.Count];
        for (var i = 0; i < vectors.Count; i++)
        {
            NormalizeInPlace(vectors[i]);
            scores[i] = (i, Dot(query, vectors[i]));
        }
        Array.Sort(scores, static (a, b) => b.Score.CompareTo(a.Score)); // descending
        return scores.Take(Math.Min(k, scores.Length)).Select(s => s.Index).ToArray();
    }

    private static float Dot(float[] a, float[] b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    private static void NormalizeInPlace(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        if (norm == 0f) return;
        for (var i = 0; i < v.Length; i++)
            v[i] /= norm;
    }
}