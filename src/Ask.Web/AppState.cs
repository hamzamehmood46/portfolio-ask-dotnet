using System.Net.Http.Json;
using Ask.Core;

namespace Ask.Web;

/// <summary>Loads the bundled documents once, builds the index and hands out the assistant. The browser can only use the stand-in model.</summary>
public sealed class AppState
{
    private readonly HttpClient _http;
    private Task? _loading;

    public AppState(HttpClient http) => _http = http;

    public PortfolioAssistant? Assistant { get; private set; }

    public KnowledgeIndex? Index { get; private set; }

    public IReadOnlyList<Doc> Docs { get; private set; } = [];

    public string? LoadError { get; private set; }

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var names = await _http.GetFromJsonAsync<string[]>("corpus/index.json") ?? [];
            var docs = new List<Doc>();
            foreach (var name in names)
            {
                var text = await _http.GetStringAsync($"corpus/{name}");
                docs.Add(new Doc(name, TitleOf(text, name), text));
            }

            Docs = docs;
            Index = KnowledgeIndex.Build(docs);
            Assistant = new PortfolioAssistant(Index, new FakeChatClient());
        }
        catch (Exception ex)
        {
            LoadError = $"Could not load the documents ({ex.GetType().Name}).";
        }
    }

    public static string TitleOf(string markdown, string fallback)
    {
        foreach (var line in markdown.Split('\n'))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        return fallback;
    }
}
