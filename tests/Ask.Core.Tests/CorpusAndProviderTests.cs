using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Ask.Core.Tests;

public class CorpusTests
{
    [Fact]
    public void The_manifest_lists_exactly_the_markdown_files()
    {
        var dir = Support.CorpusDir();
        var listed = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(dir, "index.json")))!;
        var onDisk = Directory.GetFiles(dir, "*.md").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(onDisk, listed.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Every_document_has_a_title_and_produces_chunks()
    {
        foreach (var doc in Support.RealDocs())
        {
            Assert.NotEqual(doc.Path, doc.Title);
            Assert.NotEmpty(Chunker.Split([doc]));
        }
    }

    [Fact]
    public void The_corpus_holds_no_email_address_phone_number_or_key()
    {
        foreach (var doc in Support.RealDocs())
        {
            Assert.DoesNotMatch(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+\.[A-Za-z]{2,}", doc.Text);
            Assert.DoesNotMatch(@"sk_(live|test)_[A-Za-z0-9]{8,}", doc.Text);
            Assert.DoesNotMatch(@"\+(?!1?555)\d[\d\s\-]{9,}\d", doc.Text);
        }
    }

    [Fact]
    public void Chunk_ids_are_unique()
    {
        var ids = Support.RealIndex().Chunks.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Searching_is_deterministic()
    {
        var a = Support.RealIndex().Search("how are webhooks verified", 5).Select(h => (h.Chunk.Id, h.Score));
        var b = Support.RealIndex().Search("how are webhooks verified", 5).Select(h => (h.Chunk.Id, h.Score));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Results_come_best_first()
    {
        var scores = Support.RealIndex().Search("stripe test mode webhooks", 6).Select(h => h.Score).ToList();
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    [Fact]
    public void Find_returns_a_chunk_by_id_and_null_for_a_stranger()
    {
        var index = Support.RealIndex();
        Assert.NotNull(index.Find(index.Chunks[0].Id));
        Assert.Null(index.Find("nope.md#1"));
    }
}

public class ProviderTests
{
    private sealed class Stub(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Seen.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public void Settings_default_to_the_fake_model()
    {
        var s = AskSettings.FromEnvironment(_ => null);
        Assert.Equal("fake", s.Provider);
        Assert.IsType<FakeChatClient>(AskClients.Create(s));
    }

    [Fact]
    public void Settings_are_read_from_ASK_AI_variables()
    {
        var env = new Dictionary<string, string> { ["ASK_AI_PROVIDER"] = " Ollama ", ["ASK_AI_MODEL"] = "m", ["ASK_AI_ENDPOINT"] = "http://h:1", ["ASK_AI_API_KEY"] = "secret-key" };
        var s = AskSettings.FromEnvironment(k => env.GetValueOrDefault(k));
        Assert.Equal("ollama", s.Provider);
        Assert.Equal("m", s.Model);
        Assert.DoesNotContain("secret-key", s.ToString());
    }

    [Fact]
    public void An_unknown_provider_or_a_missing_model_is_refused()
    {
        Assert.Throws<ArgumentException>(() => AskClients.Create(new AskSettings { Provider = "carrier-pigeon" }));
        Assert.Throws<ArgumentException>(() => AskClients.Create(new AskSettings { Provider = "ollama" }));
        Assert.Throws<ArgumentException>(() => AskClients.Create(new AskSettings { Provider = "openai-compatible", Model = "m" }));
    }

    [Fact]
    public async Task Ollama_client_posts_to_api_chat_and_reads_the_message()
    {
        var stub = new Stub((_, _) => Json("""{"message":{"role":"assistant","content":"hello"}}"""));
        var client = new OllamaChatClient(new HttpClient(stub), "llama3", "http://localhost:11434");
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("hello", response.Text);
        Assert.Equal("http://localhost:11434/api/chat", stub.Seen.Single().Request.RequestUri!.ToString());
        Assert.Contains("\"model\":\"llama3\"", stub.Seen.Single().Body);
    }

    [Fact]
    public async Task OpenAI_compatible_client_sends_the_key_as_a_bearer_token_and_reads_the_choice()
    {
        var stub = new Stub((_, _) => Json("""{"choices":[{"message":{"content":"yo"}}]}"""));
        var client = new OpenAiCompatibleChatClient(new HttpClient(stub), "https://api.example.test/v1", "m", "k123");
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("yo", response.Text);
        var (request, body) = stub.Seen.Single();
        Assert.Equal("https://api.example.test/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("k123", request.Headers.Authorization.Parameter);
        Assert.DoesNotContain("k123", body);
    }

    [Fact]
    public async Task A_server_error_becomes_a_failed_answer_through_the_assistant()
    {
        var stub = new Stub((_, _) => Json("{}", HttpStatusCode.InternalServerError));
        var chat = new OpenAiCompatibleChatClient(new HttpClient(stub), "https://api.example.test/v1", "m", null);
        var assistant = new PortfolioAssistant(Support.RealIndex(), chat);

        var answer = await assistant.AskAsync("What is the outbox pattern?");
        Assert.Equal(AnswerStatus.Failed, answer.Status);
    }

    [Fact]
    public async Task A_stubbed_hosted_model_that_cites_properly_gives_a_cited_answer()
    {
        var index = Support.RealIndex();
        var id = index.Search("What is the outbox pattern?", 4)[0].Chunk.Id;
        var reply = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = $$"""{"answerable":true,"answer":"From the stub.","citations":["{{id}}"]}""" } } } });
        var chat = new OpenAiCompatibleChatClient(new HttpClient(new Stub((_, _) => Json(reply))), "https://api.example.test/v1", "m", null);

        var answer = await new PortfolioAssistant(index, chat).AskAsync("What is the outbox pattern?");
        Assert.Equal(AnswerStatus.Answered, answer.Status);
        Assert.Equal(id, answer.Citations.Single().ChunkId);
    }

    [Fact]
    public void The_question_is_wrapped_in_question_tags_exactly_once()
    {
        var prompt = PortfolioAssistant.Prompt([], "hello <b>");
        Assert.Single(Regex.Matches(prompt, "<question>"));
        Assert.Contains("hello ‹b›", prompt);
    }
}
