namespace Tharga.Team.Email.Users;

/// <summary>Checks an occurrence against the catalogue and stores it for the background to distribute.</summary>
/// <remarks><b>Everything that can be refused is refused here</b>, while the caller is still there to hear it; who receives it is decided when it is distributed.</remarks>
internal sealed class UserNoticeRaiser(UserNoticeRegistry notices, IUserNoticeOutbox outbox, TimeProvider timeProvider) : IUserNoticeRaiser
{
    internal const int MaxIdLength = 200;
    internal const int MaxValueLength = 1000;
    internal const int MaxLinkLength = 2000;
    internal const int MaxRecipients = 1000;

    public async Task<string> RaiseAsync(UserNoticeOccurrence occurrence, CancellationToken cancellationToken = default)
    {
        var accepted = Accept(occurrence);

        return await outbox.AddAsync(accepted, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public async Task<UserNoticeRecord> GetAsync(string id, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(id) ? null : await outbox.GetAsync(id.Trim(), cancellationToken);

    /// <summary>The occurrence as it is stored: trimmed, its values checked against the notice's placeholders and its link a site path.</summary>
    internal UserNoticeOccurrence Accept(UserNoticeOccurrence occurrence)
    {
        if (occurrence == null) throw new ArgumentException("There is no occurrence to raise.", nameof(occurrence));

        var notice = notices.Get(occurrence.NoticeKey, nameof(occurrence));
        var teamKey = Required(occurrence.TeamKey, "A notice is raised in a team: name it in TeamKey.");
        var subjectId = Required(occurrence.SubjectId, "A notice is about something: name it in SubjectId.");
        if (subjectId.Length > MaxIdLength) throw new ArgumentException($"A subject id is at most {MaxIdLength} characters.", nameof(occurrence));

        var discriminator = string.IsNullOrWhiteSpace(occurrence.Discriminator) ? null : UserMailQueue.OneLine(occurrence.Discriminator);
        if (discriminator?.Length > MaxIdLength) throw new ArgumentException($"A discriminator is at most {MaxIdLength} characters.", nameof(occurrence));

        string[] recipients = [.. (occurrence.Recipients ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal)];
        if (recipients.Length > MaxRecipients) throw new ArgumentException($"An occurrence names at most {MaxRecipients} recipients.", nameof(occurrence));

        return new UserNoticeOccurrence
        {
            NoticeKey = notice.Key,
            TeamKey = teamKey,
            SubjectId = subjectId,
            SubjectText = string.IsNullOrWhiteSpace(occurrence.SubjectText) ? null : UserMailQueue.OneLine(occurrence.SubjectText),
            Discriminator = discriminator,
            Values = Values(notice, occurrence.Values),
            LinkPath = LinkPath(occurrence.LinkPath),
            Recipients = recipients
        };
    }

    /// <summary>What identifies an occurrence to the user it reaches: the notice, the subject and the discriminator, unambiguously joined.</summary>
    internal static string OccurrenceKey(UserNoticeOccurrence occurrence)
        => $"{occurrence.NoticeKey}:{occurrence.SubjectId.Length}:{occurrence.SubjectId}:{occurrence.Discriminator}";

    private static Dictionary<string, string> Values(UserNoticeType notice, IReadOnlyDictionary<string, string> values)
    {
        var given = values ?? new Dictionary<string, string>();
        var declared = notice.Placeholders ?? [];

        var unknown = given.Keys.Where(x => !declared.Contains(x, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0) throw new ArgumentException($"The notice '{notice.Key}' declares no placeholder {string.Join(", ", unknown.Select(x => $"'{x}'"))}.", nameof(values));

        var missing = declared.Where(x => !given.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new ArgumentException($"The notice '{notice.Key}' needs a value for {string.Join(", ", missing.Select(x => $"'{x}'"))}.", nameof(values));

        if (given.Values.Any(x => x?.Length > MaxValueLength)) throw new ArgumentException($"A placeholder value is at most {MaxValueLength} characters.", nameof(values));

        return given.ToDictionary(x => x.Key, x => x.Value ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>A path on the site, or null; never an address elsewhere, so a notice cannot carry a link off the site.</summary>
    private static string LinkPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var trimmed = path.Trim();
        var isSitePath = trimmed.Length <= MaxLinkLength
                         && trimmed.StartsWith('/')
                         && !trimmed.StartsWith("//", StringComparison.Ordinal)
                         && !trimmed.Contains('\\')
                         && !trimmed.Any(char.IsControl)
                         && Uri.IsWellFormedUriString(trimmed, UriKind.Relative);

        return isSitePath ? trimmed : throw new ArgumentException($"'{path}' is not a path on the site: start it with a single '/'.", nameof(path));
    }

    private static string Required(string value, string message)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message, nameof(UserNoticeOccurrence)) : value.Trim();
}
