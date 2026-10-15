namespace Tharga.Team;

/// <summary>Who may keep a support case to themselves.</summary>
public static class SupportCaseVisibilityRules
{
    /// <summary>Whether only its author may read the case: allowed while nobody else in the team has written in it.</summary>
    public static bool MayBePrivate(SupportCase supportCase, IEnumerable<SupportMessage> messages)
        => supportCase != null
           && !(messages ?? []).Any(x => x.Kind == SupportMessageKind.User && !x.AsSupport && x.AuthorIdentity != supportCase.AuthorIdentity);
}
