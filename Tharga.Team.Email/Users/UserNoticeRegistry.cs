using System.Text.RegularExpressions;

namespace Tharga.Team.Email.Users;

/// <summary>The catalogue of notices, checked once at registration against the mail categories.</summary>
internal sealed partial class UserNoticeRegistry
{
    private readonly Dictionary<string, UserNoticeType> _byKey;

    private UserNoticeRegistry(IReadOnlyList<UserNoticeType> all)
    {
        All = all;
        _byKey = all.ToDictionary(x => x.Key, StringComparer.Ordinal);
    }

    /// <summary>Every notice, in the order the host added them.</summary>
    public IReadOnlyList<UserNoticeType> All { get; }

    /// <summary>The notice with this key, or null.</summary>
    public UserNoticeType Find(string key) => key != null && _byKey.TryGetValue(key, out var notice) ? notice : null;

    /// <summary>The notice with this key.</summary>
    /// <exception cref="ArgumentException">No notice has this key.</exception>
    public UserNoticeType Get(string key, string parameterName)
        => Find(key?.Trim()) ?? throw new ArgumentException($"'{key}' is not a notice in the catalogue. Add it with {nameof(UserNoticeOptions)}.{nameof(UserNoticeOptions.Add)}.", parameterName);

    /// <summary>The host's notices, refusing a malformed or repeated key, an unknown category, no delivery and a template using a placeholder it does not declare.</summary>
    /// <exception cref="InvalidOperationException">A notice is not acceptable.</exception>
    public static UserNoticeRegistry Create(IEnumerable<UserNoticeType> notices, UserMailCategoryRegistry categories)
    {
        var all = new List<UserNoticeType>();

        foreach (var notice in notices ?? [])
        {
            if (notice == null) continue;

            Validate(notice, categories);
            if (all.Any(x => x.Key == notice.Key)) throw new InvalidOperationException($"The notice '{notice.Key}' is in the catalogue twice.");

            all.Add(notice with { Deliveries = [.. notice.Deliveries.Distinct()], Placeholders = [.. notice.Placeholders ?? []] });
        }

        return new UserNoticeRegistry(all);
    }

    private static void Validate(UserNoticeType notice, UserMailCategoryRegistry categories)
    {
        if (string.IsNullOrWhiteSpace(notice.Key) || !KeyPattern().IsMatch(notice.Key))
            throw new InvalidOperationException($"The notice key '{notice.Key}' is not valid: use 1 to 64 lowercase letters, digits, dots, dashes or underscores, starting with a letter or digit.");

        if (string.IsNullOrWhiteSpace(notice.Name))
            throw new InvalidOperationException($"The notice '{notice.Key}' has no name.");

        if (categories?.Find(notice.Category) == null)
            throw new InvalidOperationException($"The notice '{notice.Key}' is sent in '{notice.Category}', which is not a registered mail category. Register it with {nameof(UserMailOptions)}.{nameof(UserMailOptions.AddCategory)}.");

        if (notice.Audience == null)
            throw new InvalidOperationException($"The notice '{notice.Key}' says nothing about who may receive it: set {nameof(UserNoticeType.Audience)}.");

        if (notice.Deliveries == null || notice.Deliveries.Length == 0)
            throw new InvalidOperationException($"The notice '{notice.Key}' allows no delivery: allow at once, the daily summary, or both.");

        if (!notice.Allows(notice.DefaultDelivery))
            throw new InvalidOperationException($"The notice '{notice.Key}' defaults to {notice.DefaultDelivery}, which it does not allow.");

        if (string.IsNullOrWhiteSpace(notice.Subject))
            throw new InvalidOperationException($"The notice '{notice.Key}' has no subject.");

        var declared = notice.Placeholders ?? [];
        foreach (var name in declared)
        {
            if (!UserNoticeTemplate.IsName(name))
                throw new InvalidOperationException($"The notice '{notice.Key}' declares the placeholder '{name}', which is not a name: use a letter, then letters, digits or underscores.");
            if (name == UserNoticeType.SubjectPlaceholder)
                throw new InvalidOperationException($"The notice '{notice.Key}' declares '{UserNoticeType.SubjectPlaceholder}', which every notice has already: it is the occurrence's subject text.");
        }

        if (declared.Distinct(StringComparer.Ordinal).Count() != declared.Length)
            throw new InvalidOperationException($"The notice '{notice.Key}' declares a placeholder twice.");

        CheckTemplate(notice, nameof(UserNoticeType.Subject), notice.Subject, declared);
        CheckTemplate(notice, nameof(UserNoticeType.Body), notice.Body, declared);
    }

    private static void CheckTemplate(UserNoticeType notice, string part, string template, string[] declared)
    {
        string[] names;
        try
        {
            names = UserNoticeTemplate.Names(template);
        }
        catch (FormatException e)
        {
            throw new InvalidOperationException($"The {part.ToLowerInvariant()} of the notice '{notice.Key}' is not a valid template. {e.Message}", e);
        }

        var unknown = names.Where(x => x != UserNoticeType.SubjectPlaceholder && !declared.Contains(x, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"The {part.ToLowerInvariant()} of the notice '{notice.Key}' uses {string.Join(", ", unknown.Select(x => $"{{{x}}}"))}, which it does not declare in {nameof(UserNoticeType.Placeholders)}.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex KeyPattern();
}
