using System.Text;

namespace Ask.Core;

/// <summary>Splits markdown into passages that follow its headings. Code fences are kept whole and never mistaken for headings.</summary>
public static class Chunker
{
    public const int MaxChars = 900;

    public static IReadOnlyList<Chunk> Split(IEnumerable<Doc> docs)
    {
        var chunks = new List<Chunk>();
        foreach (var doc in docs)
        {
            var n = 0;
            foreach (var (heading, text) in Sections(doc))
            {
                foreach (var piece in Pack(text))
                {
                    if (piece.Count(char.IsLetter) < 20)
                    {
                        continue; // a badge line or a lone link carries nothing worth citing
                    }

                    chunks.Add(new Chunk($"{doc.Path}#{++n}", doc.Path, heading, piece));
                }
            }
        }

        return chunks;
    }

    private static IEnumerable<(string Heading, string Text)> Sections(Doc doc)
    {
        var path = new List<string>();
        var body = new StringBuilder();
        var inFence = false;

        string Current() => path.Count == 0 ? doc.Title : string.Join(" > ", path);

        foreach (var line in doc.Text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            var level = inFence ? 0 : HeadingLevel(line);
            if (level == 0)
            {
                body.AppendLine(line);
                continue;
            }

            if (body.ToString().Trim().Length > 0)
            {
                yield return (Current(), body.ToString().Trim());
            }

            body.Clear();
            while (path.Count >= level)
            {
                path.RemoveAt(path.Count - 1);
            }

            path.Add(line.TrimStart('#').Trim());
        }

        if (body.ToString().Trim().Length > 0)
        {
            yield return (Current(), body.ToString().Trim());
        }
    }

    private static int HeadingLevel(string line)
    {
        var t = line.TrimStart();
        var hashes = 0;
        while (hashes < t.Length && t[hashes] == '#')
        {
            hashes++;
        }

        return hashes is >= 1 and <= 3 && hashes < t.Length && t[hashes] == ' ' ? hashes : 0;
    }

    /// <summary>Groups blank-line separated blocks up to <see cref="MaxChars"/>; an oversized block (a long table) is cut by line.</summary>
    private static IEnumerable<string> Pack(string text)
    {
        var current = new StringBuilder();
        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var part in block.Length <= MaxChars ? [block] : CutByLine(block))
            {
                if (current.Length > 0 && current.Length + part.Length + 2 > MaxChars)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                if (current.Length > 0)
                {
                    current.Append("\n\n");
                }

                current.Append(part);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static IEnumerable<string> CutByLine(string block)
    {
        var current = new StringBuilder();
        foreach (var line in block.Split('\n'))
        {
            if (current.Length > 0 && current.Length + line.Length + 1 > MaxChars)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(line.Length > MaxChars ? line[..MaxChars] : line);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
