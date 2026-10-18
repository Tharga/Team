namespace Tharga.Team;

/// <summary>A kind of mail the product's staff send to contacts — people who are not users, such as prospective customers — which each address may opt out of.</summary>
/// <remarks>
/// <b>Always optional and never a user's choice.</b> Every contact mail carries an unsubscribe link, an opt-out is kept
/// for the address rather than for a user, and contact categories are never listed in a user's mail settings.
/// <para>
/// <b>The key is stored</b> on every mail and every opt-out, so it never changes once mail has been sent in it, and it
/// may not be the key of a user mail category. The name and description are shown on the unsubscribe page through the
/// host's text catalogue: <see cref="NameKey"/> and <see cref="DescriptionKey"/> are what a translation is registered
/// under, <see cref="Name"/> and <see cref="Description"/> the English used when there is none.
/// </para>
/// </remarks>
public sealed record ContactMailCategory
{
    private readonly string _nameKey;
    private readonly string _descriptionKey;

    /// <summary>The stable key: lowercase letters, digits, dots, dashes and underscores, starting with a letter or digit.</summary>
    public required string Key { get; init; }

    /// <summary>The name shown on the unsubscribe page and to staff, in English.</summary>
    public required string Name { get; init; }

    /// <summary>One sentence on what the mail is about, in English.</summary>
    public string Description { get; init; }

    /// <summary>The text key the name is translated under; <c>contactMail.category.{Key}.name</c> unless set.</summary>
    public string NameKey
    {
        get => _nameKey ?? $"contactMail.category.{Key}.name";
        init => _nameKey = value;
    }

    /// <summary>The text key the description is translated under; <c>contactMail.category.{Key}.description</c> unless set.</summary>
    public string DescriptionKey
    {
        get => _descriptionKey ?? $"contactMail.category.{Key}.description";
        init => _descriptionKey = value;
    }
}
