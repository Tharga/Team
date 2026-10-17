namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportMailboxAddress</c> — which support address a mailed case came in to.
/// </summary>
public static class SupportMailboxText
{
    /// <summary>Names the support address a case's mail came in to, and that answers go out from; <c>{0}</c> is the address.</summary>
    public static readonly TextKey CameInTo = new("team.support.mailbox.cameInTo", "to {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [CameInTo];
}
