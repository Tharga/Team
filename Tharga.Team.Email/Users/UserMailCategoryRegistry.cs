using System.Text.RegularExpressions;

namespace Tharga.Team.Email.Users;

/// <summary>The categories mail to users may be sent in: the built-in ones, then the host's, checked once at registration.</summary>
internal sealed partial class UserMailCategoryRegistry
{
    private readonly Dictionary<string, UserMailCategory> _byKey;

    private UserMailCategoryRegistry(IReadOnlyList<UserMailCategory> all)
    {
        All = all;
        _byKey = all.ToDictionary(x => x.Key, StringComparer.Ordinal);
    }

    /// <summary>Every category, built-in ones first.</summary>
    public IReadOnlyList<UserMailCategory> All { get; }

    /// <summary>Whether any category can be turned off, which is what needs unsubscribe links.</summary>
    public bool HasOptional => All.Any(x => !x.Required);

    /// <summary>The category with this key, or null.</summary>
    public UserMailCategory Find(string key) => key != null && _byKey.TryGetValue(key, out var category) ? category : null;

    /// <summary>The category with this key.</summary>
    /// <exception cref="ArgumentException">No category has this key.</exception>
    public UserMailCategory Get(string key, string parameterName)
        => Find(key) ?? throw new ArgumentException($"'{key}' is not a registered mail category. Register it with {nameof(UserMailOptions)}.{nameof(UserMailOptions.AddCategory)}.", parameterName);

    /// <summary>The built-in categories followed by the host's, refusing a malformed, unnamed, repeated or reserved key; service messages are optional, on by default, unless <paramref name="serviceRequired"/>.</summary>
    /// <exception cref="InvalidOperationException">A category is not acceptable.</exception>
    public static UserMailCategoryRegistry Create(IEnumerable<UserMailCategory> hostCategories, bool serviceRequired = true)
    {
        var all = new List<UserMailCategory>
        {
            UserMailCategories.Account,
            serviceRequired ? UserMailCategories.Service : UserMailCategories.Service with { Required = false, OnByDefault = true }
        };

        foreach (var category in hostCategories ?? [])
        {
            if (category == null) continue;

            if (!IsKey(category.Key))
                throw new InvalidOperationException($"The mail category key '{category.Key}' is not valid: use 1 to 64 lowercase letters, digits, dots, dashes or underscores, starting with a letter or digit.");

            if (UserMailCategories.BuiltIn.Any(x => x.Key == category.Key))
                throw new InvalidOperationException($"The mail category key '{category.Key}' is built in. Use a key of your own.");

            if (all.Any(x => x.Key == category.Key))
                throw new InvalidOperationException($"The mail category '{category.Key}' is registered twice.");

            if (string.IsNullOrWhiteSpace(category.Name))
                throw new InvalidOperationException($"The mail category '{category.Key}' has no name.");

            all.Add(category);
        }

        return new UserMailCategoryRegistry(all);
    }

    /// <summary>Whether a category key has the shape every stored key has: 1 to 64 lowercase letters, digits, dots, dashes or underscores, starting with a letter or digit.</summary>
    internal static bool IsKey(string key) => !string.IsNullOrWhiteSpace(key) && KeyPattern().IsMatch(key);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex KeyPattern();
}
