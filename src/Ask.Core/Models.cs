namespace Ask.Core;

/// <summary>One markdown document in the corpus. Path is the file name and is what a citation points at.</summary>
public sealed record Doc(string Path, string Title, string Text);

/// <summary>A passage of a document, small enough to quote. Id is "path#n".</summary>
public sealed record Chunk(string Id, string Source, string Heading, string Text)
{
    /// <summary>What gets embedded: the title and heading count too, so "Tests" finds the tests section.</summary>
    public string Embedded => $"{Source} {Heading} {Text}";
}

public sealed record Hit(Chunk Chunk, double Score);

public enum AnswerStatus
{
    /// <summary>The answer is backed by at least one retrieved passage.</summary>
    Answered,

    /// <summary>The corpus does not cover the question. The assistant says so and cites nothing.</summary>
    NoAnswer,

    /// <summary>The model failed, timed out or never produced a checkable answer.</summary>
    Failed,
}

public sealed record Citation(string ChunkId, string Source, string Heading, string Excerpt);

/// <param name="Considered">Every passage that was retrieved, with its score, so the UI can show why a question was or was not answered.</param>
public sealed record Answer(AnswerStatus Status, string Text, IReadOnlyList<Citation> Citations, string? Reason, IReadOnlyList<Hit> Considered)
{
    public static Answer NoAnswer(string reason, IReadOnlyList<Hit>? considered = null) =>
        new(AnswerStatus.NoAnswer, "I don't know from these docs.", [], reason, considered ?? []);

    public static Answer Failed(string reason, IReadOnlyList<Hit>? considered = null) =>
        new(AnswerStatus.Failed, "I could not produce an answer I can back with a source.", [], reason, considered ?? []);
}

public sealed class AskOptions
{
    /// <summary>Passages scoring below this are treated as unrelated. Below it for the best passage means "I don't know".</summary>
    public double MinScore { get; init; } = 0.20;

    public int TopK { get; init; } = 4;

    public int MaxQuestionChars { get; init; } = 500;

    /// <summary>Most characters of retrieved text sent to the model.</summary>
    public int MaxContextChars { get; init; } = 6000;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
