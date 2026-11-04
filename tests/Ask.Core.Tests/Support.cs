using Ask.Core;

namespace Ask.Core.Tests;

internal static class Support
{
    /// <summary>The real bundled corpus, found by walking up from the test binary to the folder that holds Ask.sln.</summary>
    public static string CorpusDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ask.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Ask.sln not found"), "src", "Ask.Web", "wwwroot", "corpus");
    }

    public static List<Doc> RealDocs() =>
        [.. Directory.GetFiles(CorpusDir(), "*.md").OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => { var t = File.ReadAllText(p); return new Doc(Path.GetFileName(p), TitleOf(t, Path.GetFileName(p)), t); })];

    public static string TitleOf(string markdown, string fallback) =>
        markdown.Split('\n').FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? fallback;

    public static KnowledgeIndex RealIndex() => KnowledgeIndex.Build(RealDocs());

    public static KnowledgeIndex Index(params (string Path, string Text)[] docs) =>
        KnowledgeIndex.Build(docs.Select(d => new Doc(d.Path, TitleOf(d.Text, d.Path), d.Text)));
}
