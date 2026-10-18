namespace Tharga.Team.Email.Users;

/// <summary>The categories contact mail may be sent in, checked once at registration.</summary>
internal sealed class ContactMailCategoryRegistry
{
    private readonly Dictionary<string, ContactMailCategory> _byKey;

    private ContactMailCategoryRegistry(IReadOnlyList<ContactMailCategory> all)
    {
        All = all;
        _byKey = all.ToDictionary(x => x.Key, StringComparer.Ordinal);
    }

    /// <summary>Every category, in the order the host added them.</summary>
    public IReadOnlyList<ContactMailCategory> All { get; }

    /// <summary>The category with this key, or null.</summary>
    public ContactMailCategory Find(string key) => key != null && _byKey.TryGetValue(key.Trim(), out var category) ? category : null;

    /// <summary>The category with this key.</summary>
    /// <exception cref="ArgumentException">No contact category has this key.</exception>
    public ContactMailCategory Get(string key, string parameterName)
        => Find(key) ?? throw new ArgumentException($"'{key}' is not a registered contact mail category. Register it with {nameof(ContactMessageOptions)}.{nameof(ContactMessageOptions.AddCategory)}.", parameterName);

    /// <summary>The host's categories, refusing none at all, or a malformed, unnamed or repeated key, or one a category of mail to users already has.</summary>
    /// <exception cref="InvalidOperationException">A category is not acceptable.</exception>
    public static ContactMailCategoryRegistry Create(IEnumerable<ContactMailCategory> hostCategories, UserMailCategoryRegistry userCategories)
    {
        var all = new List<ContactMailCategory>();

        foreach (var category in hostCategories ?? [])
        {
            if (category == null) continue;

            if (!UserMailCategoryRegistry.IsKey(category.Key))
                throw new InvalidOperationException($"The contact mail category key '{category.Key}' is not valid: use 1 to 64 lowercase letters, digits, dots, dashes or underscores, starting with a letter or digit.");

            if (userCategories?.Find(category.Key) != null)
                throw new InvalidOperationException($"The contact mail category key '{category.Key}' is the key of a category of mail to users. Use a key of its own, so a mail's category says which it is.");

            if (all.Any(x => x.Key == category.Key))
                throw new InvalidOperationException($"The contact mail category '{category.Key}' is registered twice.");

            if (string.IsNullOrWhiteSpace(category.Name))
                throw new InvalidOperationException($"The contact mail category '{category.Key}' has no name.");

            all.Add(category);
        }

        if (all.Count == 0)
            throw new InvalidOperationException($"No contact mail category is registered, so staff could write to contacts in none. Add one with {nameof(ContactMessageOptions)}.{nameof(ContactMessageOptions.AddCategory)}.");

        return new ContactMailCategoryRegistry(all);
    }
}
