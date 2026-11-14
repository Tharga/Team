namespace Tharga.Team.Email.Users;

/// <summary>Builds what <see cref="IMailOutcomeSink"/> is told about a stored mail.</summary>
internal static class MailOutcomes
{
    public static MailOutcome Of(UserMailItem item, MailOutcomeKind outcome, string reason, DateTime at) => new()
    {
        MailId = item.Id,
        Outcome = outcome,
        Kind = item.Mail.Kind,
        Subject = item.Mail.Subject,
        Reason = reason,
        SentByUserKey = item.Mail.SentByUserKey,
        RecipientUserKey = item.Mail.RecipientUserKey,
        TeamKey = item.Mail.TeamKey,
        CaseId = item.Mail.CaseId,
        At = at
    };
}
