namespace Ask.Core;

public interface IEmbedder
{
    /// <summary>Returns a unit-length vector, so cosine similarity is a dot product.</summary>
    float[] Embed(string text);
}

/// <summary>
/// A deterministic stand-in for an embedding model: hashed bag of words with tf-idf weights. It matches shared words, not meaning,
/// so "cost" will not find "price". It needs no network and no key, and gives the same vector for the same text every time.
/// </summary>
public sealed class FakeEmbedder : IEmbedder
{
    public const int Dimensions = 4096;

    private static readonly HashSet<string> Stop = new(
        ("a an and are as at be but by can do does for from has have how i if in is it its me my of on or our so than that the their then there these this to " +
         "did was we what when where which who why will with you your about into over under also not no yes").Split(' '));

    private readonly Dictionary<string, double> _idf;
    private readonly double _unknownIdf;

    private FakeEmbedder(Dictionary<string, double> idf, double unknownIdf)
    {
        _idf = idf;
        _unknownIdf = unknownIdf;
    }

    /// <summary>Learns how rare each word is across the corpus, so common words count less and words nobody has written count most.</summary>
    public static FakeEmbedder Fit(IEnumerable<string> corpus)
    {
        var df = new Dictionary<string, int>();
        var n = 0;
        foreach (var text in corpus)
        {
            n++;
            foreach (var token in Tokens(text).Distinct())
            {
                df[token] = df.GetValueOrDefault(token) + 1;
            }
        }

        var idf = df.ToDictionary(p => p.Key, p => Math.Log((n + 1.0) / (p.Value + 1.0)) + 1.0);
        return new FakeEmbedder(idf, Math.Log(n + 1.0) + 1.0);
    }

    public float[] Embed(string text)
    {
        var tf = new Dictionary<string, int>();
        foreach (var token in Tokens(text))
        {
            tf[token] = tf.GetValueOrDefault(token) + 1;
        }

        var vector = new float[Dimensions];
        foreach (var (token, count) in tf)
        {
            var weight = (1 + Math.Log(count)) * _idf.GetValueOrDefault(token, _unknownIdf);
            vector[(int)(Hash(token) % Dimensions)] += (float)weight;
        }

        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= (float)norm;
            }
        }

        return vector;
    }

    public static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }

    /// <summary>Lowercase words and numbers, stop words dropped, plural and -ing/-ed endings trimmed.</summary>
    public static IEnumerable<string> Tokens(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                var word = text[start..i].ToLowerInvariant();
                start = -1;
                if (word.Length >= 2 && !Stop.Contains(word))
                {
                    yield return Stem(word);
                }
            }
        }
    }

    private static string Stem(string w)
    {
        if (w.Length > 5 && (w.EndsWith("ied", StringComparison.Ordinal) || w.EndsWith("ies", StringComparison.Ordinal)))
        {
            return w[..^3] + "y";
        }

        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal))
        {
            return w[..^3];
        }

        if (w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal))
        {
            return w[..^2];
        }

        if (w.Length > 3 && w.EndsWith("s", StringComparison.Ordinal) && !w.EndsWith("ss", StringComparison.Ordinal))
        {
            return w[..^1];
        }

        return w;
    }

    // FNV-1a: stable across runs and platforms, unlike string.GetHashCode.
    private static uint Hash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s)
        {
            h = (h ^ c) * 16777619u;
        }

        return h;
    }
}
