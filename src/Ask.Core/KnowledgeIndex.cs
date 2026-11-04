namespace Ask.Core;

/// <summary>The searchable corpus: chunks plus their vectors. Everything lives in memory.</summary>
public sealed class KnowledgeIndex
{
    private readonly IReadOnlyList<Chunk> _chunks;
    private readonly float[][] _vectors;
    private readonly IEmbedder _embedder;

    public KnowledgeIndex(IReadOnlyList<Chunk> chunks, IEmbedder embedder)
    {
        _chunks = chunks;
        _embedder = embedder;
        _vectors = [.. chunks.Select(c => embedder.Embed(c.Embedded))];
    }

    public IReadOnlyList<Chunk> Chunks => _chunks;

    /// <summary>Chunks the documents and fits the stand-in embedder to them.</summary>
    public static KnowledgeIndex Build(IEnumerable<Doc> docs)
    {
        var chunks = Chunker.Split(docs);
        return new KnowledgeIndex(chunks, FakeEmbedder.Fit(chunks.Select(c => c.Embedded)));
    }

    /// <summary>Best matches first. Ties keep corpus order, so results are stable.</summary>
    public IReadOnlyList<Hit> Search(string query, int k)
    {
        var q = _embedder.Embed(query);
        return [.. _chunks
            .Select((c, i) => new Hit(c, FakeEmbedder.Cosine(q, _vectors[i])))
            .Where(h => h.Score > 0)
            .OrderByDescending(h => h.Score)
            .Take(k)];
    }

    public Chunk? Find(string id) => _chunks.FirstOrDefault(c => c.Id == id);
}
