namespace Tharga.Team;

/// <summary>A kind of mail the product sends its users, which each user may turn off unless it is required.</summary>
/// <remarks>
/// <b>The key is stored</b> on every choice a user makes and every mail queued, so it never changes once mail has
/// been sent in it. The name and description are shown to users through the host's text catalogue: <see cref="NameKey"/>
/// and <see cref="DescriptionKey"/> are what a translation is registered under, <see cref="Name"/> and
/// <see cref="Description"/> the English used when there is none.
/// </remarks>
public sealed record UserMailCategory
{
    private readonly string _nameKey;
    private readonly string _descriptionKey;

    /// <summary>The stable key: lowercase letters, digits, dots, dashes and underscores, starting with a letter or digit.</summary>
    public required string Key { get; init; }

    /// <summary>The name shown to users, in English.</summary>
    public required string Name { get; init; }

    /// <summary>One sentence on what the mail is about, in English.</summary>
    public string Description { get; init; }

    /// <summary>The text key the name is translated under; <c>userMail.category.{Key}.name</c> unless set.</summary>
    public string NameKey
    {
        get => _nameKey ?? $"userMail.category.{Key}.name";
        init => _nameKey = value;
    }

    /// <summary>The text key the description is translated under; <c>userMail.category.{Key}.description</c> unless set.</summary>
    public string DescriptionKey
    {
        get => _descriptionKey ?? $"userMail.category.{Key}.description";
        init => _descriptionKey = value;
    }

    /// <summary>Whether every user receives it whatever they chose, as account and security mail must be.</summary>
    public bool Required { get; init; }

    /// <summary>Whether a user who never chose receives it; ignored for a required category.</summary>
    public bool OnByDefault { get; init; } = true;

    /// <summary>Whether a user receives this category, given what they chose or null when they never chose.</summary>
    public bool Receives(bool? choice) => Required || (choice ?? OnByDefault);
}
