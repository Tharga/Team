namespace Tharga.Team.Email.Mailbox;

/// <summary>Where a received mail goes: a team, or the postmaster when <see cref="TeamKey"/> is null.</summary>
internal sealed record MailRouteDecision(string TeamKey, string ItemKey, string DeliveredTo, MailRouteReason Reason, DateTime? KeyRetiresAt);
