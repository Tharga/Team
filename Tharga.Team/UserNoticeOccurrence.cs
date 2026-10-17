namespace Tharga.Team;

/// <summary>Something that happened, handed to <see cref="IUserNoticeRaiser"/> so the users the notice concerns are told.</summary>
/// <remarks>
/// <b>The same occurrence reaches the same person once.</b> It is identified by <see cref="NoticeKey"/>,
/// <see cref="SubjectId"/> and <see cref="Discriminator"/>; raising it again, from this instance or another, reaches only
/// recipients it has not reached before.
/// </remarks>
public sealed record UserNoticeOccurrence
{
    /// <summary>The key of a notice in the catalogue.</summary>
    public required string NoticeKey { get; init; }

    /// <summary>The team the occurrence belongs to, whose active members it may reach.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The id of the thing the notice is about, such as a case or a key.</summary>
    public required string SubjectId { get; init; }

    /// <summary>How the thing is named to a reader, the value of <c>{subject}</c>; the id when null.</summary>
    public string SubjectText { get; init; }

    /// <summary>What tells two occurrences about the same thing apart, such as <c>14-days-before</c>; null when the notice is sent once per subject.</summary>
    public string Discriminator { get; init; }

    /// <summary>The value of every placeholder the notice declares, by name.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

    /// <summary>The site path of the thing, such as <c>/cases/42</c>, made absolute on the configured base address; null for no link.</summary>
    public string LinkPath { get; init; }

    /// <summary>Users named as recipients by key, such as the owner of the thing, added to whoever the catalogue's rule admits.</summary>
    public string[] Recipients { get; init; } = [];
}
