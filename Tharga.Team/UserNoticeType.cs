namespace Tharga.Team;

/// <summary>One kind of automatic notice in the host's catalogue: who may receive it, how it is delivered by default, which mail category it belongs to and how one occurrence reads.</summary>
/// <remarks>
/// <b>The key is stored</b> on every choice, team default and occurrence, so it never changes once a notice has been
/// raised. Every text has a key a translation is registered under and the English used when there is none.
/// <para>
/// The subject and body are templates: <c>{name}</c> is replaced by the occurrence's value for a declared
/// <see cref="Placeholders"/> entry, <c>{subject}</c> by the occurrence's subject text, and <c>{{</c> and <c>}}</c> write
/// a brace. Values are HTML-encoded in the body, which is HTML, and inserted as they are in the subject line.
/// </para>
/// </remarks>
public sealed record UserNoticeType
{
    /// <summary>The placeholder every template may use for the occurrence's subject text.</summary>
    public const string SubjectPlaceholder = "subject";

    private readonly string _nameKey;
    private readonly string _descriptionKey;
    private readonly string _subjectKey;
    private readonly string _bodyKey;

    /// <summary>The stable key: lowercase letters, digits, dots, dashes and underscores, starting with a letter or digit.</summary>
    public required string Key { get; init; }

    /// <summary>The name shown to users, in English.</summary>
    public required string Name { get; init; }

    /// <summary>One sentence on when the notice is sent, in English.</summary>
    public string Description { get; init; }

    /// <summary>The text key the name is translated under; <c>userNotice.{Key}.name</c> unless set.</summary>
    public string NameKey
    {
        get => _nameKey ?? $"userNotice.{Key}.name";
        init => _nameKey = value;
    }

    /// <summary>The text key the description is translated under; <c>userNotice.{Key}.description</c> unless set.</summary>
    public string DescriptionKey
    {
        get => _descriptionKey ?? $"userNotice.{Key}.description";
        init => _descriptionKey = value;
    }

    /// <summary>The key of the registered mail category the notice is sent in, so a user who turns that category off receives none of it.</summary>
    public required string Category { get; init; }

    /// <summary>Who may receive the notice, required when the catalogue is registered; host-side only and never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public UserNoticeAudience Audience { get; init; }

    /// <summary>Whether a user receives it when neither they nor their team chose, default true; ignored for a <see cref="Required"/> notice.</summary>
    public bool OnByDefault { get; init; } = true;

    /// <summary>
    /// Whether every recipient receives it whatever they or their team chose, as a notice of a change to the terms must.
    /// Only the delivery stays a choice, where the notice allows more than one. The notice must be sent in a required
    /// mail category, so turning the category off cannot stop it either.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>The deliveries a user may choose between, at least one, default at once only.</summary>
    public UserNoticeDelivery[] Deliveries { get; init; } = [UserNoticeDelivery.AtOnce];

    /// <summary>The delivery used when neither the user nor their team chose; one of <see cref="Deliveries"/>, default at once.</summary>
    public UserNoticeDelivery DefaultDelivery { get; init; } = UserNoticeDelivery.AtOnce;

    /// <summary>The subject line template, in English.</summary>
    public required string Subject { get; init; }

    /// <summary>The text key the subject template is translated under; <c>userNotice.{Key}.subject</c> unless set.</summary>
    public string SubjectKey
    {
        get => _subjectKey ?? $"userNotice.{Key}.subject";
        init => _subjectKey = value;
    }

    /// <summary>The HTML body template, in English, or null for a mail carrying only its subject and link.</summary>
    public string Body { get; init; }

    /// <summary>The text key the body template is translated under; <c>userNotice.{Key}.body</c> unless set.</summary>
    public string BodyKey
    {
        get => _bodyKey ?? $"userNotice.{Key}.body";
        init => _bodyKey = value;
    }

    /// <summary>The names of the values every occurrence supplies, which the templates may use as <c>{name}</c>.</summary>
    public string[] Placeholders { get; init; } = [];

    /// <summary>
    /// At most one mail of this notice to a user per this interval; null, the default, mails as often as occurrences arrive.
    /// </summary>
    /// <remarks>
    /// <b>Per user, not per team</b>, so a person in two teams gets one mail covering both. Occurrences that arrive while the
    /// interval holds wait and go together when it is over. A notice with an interval, or a <see cref="Composer"/>, is
    /// delivered at once only: <see cref="Deliveries"/> may not allow the daily summary.
    /// </remarks>
    public TimeSpan? MinimumInterval { get; init; }

    /// <summary>
    /// Writes the mail from the host's current state when it is due, instead of replaying the occurrences that led there;
    /// host-side only and never serialized. Return null to send nothing.
    /// </summary>
    /// <remarks>
    /// An occurrence then means "this user may have something to hear about". The callback runs in a scope of the host's
    /// services, once per user's mail, and is given the user, the teams concerned, the occurrences waiting and what the last
    /// mail covered. A callback that throws leaves the occurrences waiting, to be tried again later.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<UserNoticeComposition, IServiceProvider, CancellationToken, Task<UserNoticeComposed>> Composer { get; init; }

    /// <summary>Whether the notice is held to an interval or composed from state, and so is delivered at once only.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsStateful => MinimumInterval != null || Composer != null;

    /// <summary>Whether a user may choose <paramref name="delivery"/> for this notice.</summary>
    public bool Allows(UserNoticeDelivery delivery) => (Deliveries ?? []).Contains(delivery);
}
