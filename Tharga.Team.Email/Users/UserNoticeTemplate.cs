using System.Text;
using System.Text.RegularExpressions;

namespace Tharga.Team.Email.Users;

/// <summary>Reads and fills a notice template: <c>{name}</c> is a placeholder, <c>{{</c> and <c>}}</c> write a brace, and any other brace is an error.</summary>
internal static partial class UserNoticeTemplate
{
    /// <summary>The placeholder names a template uses, in order of first use.</summary>
    /// <exception cref="FormatException">A brace is not closed, not opened, or encloses something that is not a name.</exception>
    public static string[] Names(string template)
    {
        var names = new List<string>();
        Walk(template, _ => { }, name =>
        {
            if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        });
        return [.. names];
    }

    /// <summary>The template with each placeholder replaced by its value passed through <paramref name="encode"/>.</summary>
    /// <exception cref="FormatException">The template is malformed, or uses a name <paramref name="values"/> has no value for.</exception>
    public static string Render(string template, IReadOnlyDictionary<string, string> values, Func<string, string> encode)
    {
        var result = new StringBuilder();
        Walk(template, text => result.Append(text), name =>
        {
            if (values == null || !values.TryGetValue(name, out var value)) throw new FormatException($"The template uses '{{{name}}}', which has no value.");
            result.Append(encode(value ?? string.Empty));
        });
        return result.ToString();
    }

    /// <summary>Whether <paramref name="name"/> can be a placeholder: a letter, then letters, digits or underscores.</summary>
    public static bool IsName(string name) => name != null && NamePattern().IsMatch(name);

    private static void Walk(string template, Action<string> onText, Action<string> onName)
    {
        var source = template ?? string.Empty;
        var start = 0;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c != '{' && c != '}') continue;

            onText(source[start..i]);

            if (i + 1 < source.Length && source[i + 1] == c)
            {
                onText(c.ToString());
                i++;
            }
            else if (c == '}')
            {
                throw new FormatException($"The template has a '}}' at {i} that closes nothing; write '}}}}' for a brace.");
            }
            else
            {
                var end = source.IndexOf('}', i + 1);
                if (end < 0) throw new FormatException($"The template has a '{{' at {i} that is never closed; write '{{{{' for a brace.");

                var name = source[(i + 1)..end];
                if (!IsName(name)) throw new FormatException($"The template's '{{{name}}}' is not a placeholder name: use a letter, then letters, digits or underscores.");

                onName(name);
                i = end;
            }

            start = i + 1;
        }

        onText(source[start..]);
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex NamePattern();
}
