namespace Ask.Core.Tests;

public class ChunkerTests
{
    private static IReadOnlyList<Chunk> Split(string md, string path = "d.md") => Chunker.Split([new Doc(path, "T", md)]);

    [Fact]
    public void Headings_become_a_path()
    {
        var chunks = Split("# Top\n\nIntro text that is long enough to keep.\n\n## Sub\n\nSub text that is long enough to keep.\n\n### Deep\n\nDeep text that is long enough to keep.");
        Assert.Equal(["Top", "Top > Sub", "Top > Sub > Deep"], chunks.Select(c => c.Heading));
    }

    [Fact]
    public void A_sibling_heading_replaces_the_previous_one()
    {
        var chunks = Split("# Top\n\n## A\n\nText under A that is long enough.\n\n## B\n\nText under B that is long enough.");
        Assert.Equal(["Top > A", "Top > B"], chunks.Select(c => c.Heading));
    }

    [Fact]
    public void A_hash_inside_a_code_fence_is_not_a_heading()
    {
        var chunks = Split("# Top\n\n```bash\n# install everything now please\ndotnet test\n```\n\nSome words after the fence that are long enough.");
        Assert.All(chunks, c => Assert.Equal("Top", c.Heading));
        Assert.Contains(chunks, c => c.Text.Contains("# install everything"));
    }

    [Fact]
    public void Ids_are_path_hash_number_and_count_up_per_document()
    {
        var chunks = Chunker.Split([new Doc("a.md", "A", "# A\n\nFirst text that is long enough.\n\n## B\n\nSecond text that is long enough."), new Doc("b.md", "B", "# B\n\nOther text that is long enough.")]);
        Assert.Equal(["a.md#1", "a.md#2", "b.md#1"], chunks.Select(c => c.Id));
    }

    [Fact]
    public void Chunks_stay_under_the_size_limit_even_for_a_huge_table()
    {
        var rows = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"| row number {i} | some value for the row | another cell |"));
        var chunks = Split("# T\n\n" + rows);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= Chunker.MaxChars, $"{c.Id} has {c.Text.Length} characters"));
    }

    [Fact]
    public void Small_blocks_are_packed_together()
    {
        var chunks = Split("# T\n\nOne sentence that is long enough.\n\nTwo sentence that is long enough.\n\nThree sentence that is long enough.");
        Assert.Single(chunks);
    }

    [Fact]
    public void A_badge_line_is_not_worth_a_chunk()
    {
        Assert.Empty(Split("# T\n\n![ok](https://x.y/z.svg)"));
    }

    [Fact]
    public void Empty_and_whitespace_documents_give_no_chunks()
    {
        Assert.Empty(Split(""));
        Assert.Empty(Split("   \n\n  "));
    }

    [Fact]
    public void Windows_line_endings_work()
    {
        var chunks = Split("# T\r\n\r\n## S\r\n\r\nText under the section that is long enough.");
        Assert.Equal("T > S", Assert.Single(chunks).Heading);
    }

    [Fact]
    public void Text_before_any_heading_uses_the_document_title()
    {
        var chunks = Chunker.Split([new Doc("p.md", "Plain title", "Just some text with no heading at all, long enough.")]);
        Assert.Equal("Plain title", Assert.Single(chunks).Heading);
    }

    [Fact]
    public void Every_character_of_prose_ends_up_in_some_chunk()
    {
        var text = "# T\n\n" + string.Join("\n\n", Enumerable.Range(0, 40).Select(i => $"Paragraph number {i} has enough words to count."));
        var joined = string.Join("\n", Split(text).Select(c => c.Text));
        for (var i = 0; i < 40; i++)
        {
            Assert.Contains($"Paragraph number {i} has", joined);
        }
    }
}

public class EmbedderTests
{
    private static FakeEmbedder Fitted() => FakeEmbedder.Fit(["alpha beta gamma", "beta delta", "gamma epsilon zeta"]);

    [Fact]
    public void The_same_text_gives_the_same_vector()
    {
        Assert.Equal(Fitted().Embed("alpha beta"), Fitted().Embed("alpha beta"));
    }

    [Fact]
    public void Vectors_have_unit_length()
    {
        var v = Fitted().Embed("alpha beta gamma delta");
        Assert.Equal(1.0, Math.Sqrt(v.Sum(x => (double)x * x)), 4);
    }

    [Fact]
    public void Identical_text_has_cosine_one()
    {
        var e = Fitted();
        Assert.Equal(1.0, FakeEmbedder.Cosine(e.Embed("alpha beta"), e.Embed("alpha beta")), 4);
    }

    [Fact]
    public void Text_with_no_shared_words_has_cosine_near_zero()
    {
        var e = Fitted();
        Assert.True(FakeEmbedder.Cosine(e.Embed("alpha"), e.Embed("zeta")) < 0.05);
    }

    [Fact]
    public void Empty_or_all_stop_word_text_gives_a_zero_vector()
    {
        var e = Fitted();
        Assert.All(e.Embed(""), x => Assert.Equal(0f, x));
        Assert.All(e.Embed("the of and"), x => Assert.Equal(0f, x));
    }

    [Fact]
    public void Plurals_and_endings_match_their_stem()
    {
        Assert.Equal(FakeEmbedder.Tokens("test").Single(), FakeEmbedder.Tokens("tests").Single());
        Assert.Equal(FakeEmbedder.Tokens("study").Single(), FakeEmbedder.Tokens("studied").Single());
        Assert.Equal(FakeEmbedder.Tokens("pause").Single(), FakeEmbedder.Tokens("pauses").Single());
    }

    [Fact]
    public void Case_and_punctuation_do_not_matter()
    {
        Assert.Equal(FakeEmbedder.Tokens("Hello, WORLD!"), FakeEmbedder.Tokens("hello world"));
    }

    [Fact]
    public void A_rare_word_counts_more_than_a_common_one()
    {
        var e = FakeEmbedder.Fit(["common common rare", "common again", "common more", "common last"]);
        var rare = FakeEmbedder.Cosine(e.Embed("rare"), e.Embed("common common rare"));
        var common = FakeEmbedder.Cosine(e.Embed("common"), e.Embed("common common rare"));
        Assert.True(rare > common);
    }

    [Fact]
    public void A_word_missing_from_the_corpus_lowers_the_match()
    {
        var e = Fitted();
        var plain = FakeEmbedder.Cosine(e.Embed("alpha"), e.Embed("alpha beta"));
        var withUnknown = FakeEmbedder.Cosine(e.Embed("alpha unknownword"), e.Embed("alpha beta"));
        Assert.True(withUnknown < plain);
    }
}

public class RedactorTests
{
    [Theory]
    [InlineData("mail me at a.b+c@example.co.uk please", "mail me at [email] please")]
    [InlineData("call +1 806-702-3068 now", "call [phone] now")]
    [InlineData("call (555) 123-4567 now", "call [phone] now")]
    [InlineData("no personal data here", "no personal data here")]
    public void Emails_and_phone_numbers_are_replaced(string input, string expected) =>
        Assert.Equal(expected, Redactor.Redact(input));

    [Fact]
    public void A_short_number_is_left_alone() => Assert.Equal("version 12345", Redactor.Redact("version 12345"));
}
