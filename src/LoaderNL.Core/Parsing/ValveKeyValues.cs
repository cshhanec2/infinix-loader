using System.Text;

namespace LoaderNL.Core.Parsing;

public static class ValveKeyValues
{
    public static IReadOnlyList<string> FindValues(string content, string key)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var tokens = Tokenize(content);
        var values = new List<string>();

        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (!string.Equals(tokens[index], key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = tokens[index + 1];
            if (candidate is not "{" and not "}")
            {
                values.Add(candidate);
            }
        }

        return values;
    }

    private static List<string> Tokenize(string content)
    {
        var tokens = new List<string>();
        var index = 0;

        while (index < content.Length)
        {
            SkipTrivia(content, ref index);
            if (index >= content.Length)
            {
                break;
            }

            if (content[index] is '{' or '}')
            {
                tokens.Add(content[index].ToString());
                index++;
                continue;
            }

            if (content[index] == '"')
            {
                tokens.Add(ReadQuoted(content, ref index));
                continue;
            }

            var start = index;
            while (index < content.Length &&
                   !char.IsWhiteSpace(content[index]) &&
                   content[index] is not '{' and not '}')
            {
                index++;
            }

            if (index > start)
            {
                tokens.Add(content[start..index]);
            }
        }

        return tokens;
    }

    private static void SkipTrivia(string content, ref int index)
    {
        while (index < content.Length)
        {
            if (char.IsWhiteSpace(content[index]))
            {
                index++;
                continue;
            }

            if (content[index] == '/' &&
                index + 1 < content.Length &&
                content[index + 1] == '/')
            {
                index += 2;
                while (index < content.Length && content[index] is not '\r' and not '\n')
                {
                    index++;
                }

                continue;
            }

            break;
        }
    }

    private static string ReadQuoted(string content, ref int index)
    {
        index++;
        var builder = new StringBuilder();

        while (index < content.Length)
        {
            var current = content[index++];
            if (current == '"')
            {
                break;
            }

            if (current == '\\' && index < content.Length)
            {
                var escaped = content[index];
                if (escaped is '\\' or '"')
                {
                    builder.Append(escaped);
                    index++;
                    continue;
                }
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
