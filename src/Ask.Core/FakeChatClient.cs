using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Ask.Core;

/// <summary>
/// A stand-in for a language model. It is extractive: it copies the sentences from the retrieved passages that share the most words with
/// the question and cites the passages they came from. It cannot paraphrase, reason or answer anything the passages do not already say.
/// It is deterministic, needs no network or key, and can also replay scripted replies for tests.
/// </summary>
public sealed partial class FakeChatClient : IChatClient
{
    private readonly Queue<Func<string>> _script = new();
    private readonly List<IReadOnlyList<ChatMessage>> _requests = [];

    public IReadOnlyList<IReadOnlyList<ChatMessage>> Requests => _requests;

    /// <summary>Queue replies that are returned in order instead of the rule-based answer.</summary>
    public FakeChatClient Script(params string[] replies)
    {
        foreach (var r in replies)
        {
            _script.Enqueue(() => r);
        }

        return this;
    }

    public FakeChatClient ScriptThrow(Exception ex)
    {
        _script.Enqueue(() => throw ex);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        _requests.Add(list);
        var reply = _script.Count > 0 ? _script.Dequeue()() : Extract(list.LastOrDefault(m => m.Role == ChatRole.User && m.Text.Contains("<question>"))?.Text ?? "");
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    [GeneratedRegex("<doc id=\"(?<id>[^\"]*)\"[^>]*>\\n(?<body>.*?)\\n</doc>", RegexOptions.Singleline)]
    private static partial Regex DocBlock();

    [GeneratedRegex("<question>\\n(?<q>.*?)\\n</question>", RegexOptions.Singleline)]
    private static partial Regex QuestionBlock();

    private static string Extract(string prompt)
    {
        var question = QuestionBlock().Match(prompt).Groups["q"].Value;
        var wanted = FakeEmbedder.Tokens(question).ToHashSet();

        var sentences = new List<(string Id, string Sentence, HashSet<string> Words)>();
        foreach (Match doc in DocBlock().Matches(prompt))
        {
            foreach (var sentence in Sentences(doc.Groups["body"].Value))
            {
                sentences.Add((doc.Groups["id"].Value, sentence, FakeEmbedder.Tokens(sentence).ToHashSet()));
            }
        }

        // A word found in most sentences (a project's own name, say) says little about which sentence answers; a rare one says a lot.
        double Weight(string word) => Math.Log((sentences.Count + 1.0) / (sentences.Count(s => s.Words.Contains(word)) + 1.0)) + 1.0;

        var candidates = sentences
            .Select((s, order) =>
            {
                var matched = wanted.Where(s.Words.Contains).ToList();
                return (s.Id, s.Sentence, Order: order, Overlap: matched.Count, Score: matched.Sum(Weight));
            })
            .ToList();

        // A sentence must share at least half of the question's content words, so a lone name or a stray word is not enough.
        var needed = Math.Max(1, (int)Math.Ceiling(wanted.Count / 2.0));
        var qualified = candidates.Where(c => c.Overlap >= needed).ToList();
        var top = qualified.Count == 0 ? 0 : qualified.Max(c => c.Score);
        // Only sentences nearly as good as the best are used, so a second sentence never just pads the answer.
        var best = qualified.Where(c => c.Score >= top * 0.8).OrderByDescending(c => c.Score).Take(2).OrderBy(c => c.Order).ToList();
        var reply = new JsonObject();
        if (best.Count == 0)
        {
            reply["answerable"] = false;
            reply["answer"] = "";
            reply["citations"] = new JsonArray();
        }
        else
        {
            reply["answerable"] = true;
            reply["answer"] = string.Join(" ", best.Select(b => b.Sentence));
            reply["citations"] = new JsonArray([.. best.Select(b => b.Id).Distinct().Select(id => (JsonNode?)JsonValue.Create(id))]);
        }

        return reply.ToJsonString();
    }

    private static IEnumerable<string> Sentences(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var text = Clean(line);
            if (text.Length < 25 || text.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var part in SentenceBreak().Split(text))
            {
                var s = part.Trim();
                if (s.Length >= 25)
                {
                    yield return s;
                }
            }
        }
    }

    private static string Clean(string line)
    {
        line = Images().Replace(line, " ");
        // Link text that is itself an address (a demo URL) is clutter, not prose.
        line = Links().Replace(line, m => m.Groups[1].Value.Contains(' ') || !(m.Groups[1].Value.Contains('.') || m.Groups[1].Value.Contains('/')) ? m.Groups[1].Value : " ");
        line = Urls().Replace(line, " ");
        return Whitespace().Replace(MarkdownNoise().Replace(line, " "), " ").Trim();
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Images();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Links();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Urls();

    [GeneratedRegex(@"[|*`>#]+|^\s*[-+]\s+")]
    private static partial Regex MarkdownNoise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();
}
