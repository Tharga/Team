namespace Tharga.Team.Email.Mailbox;

/// <summary>A team address read back: the key, and the item after <c>+</c> when there was one.</summary>
internal sealed record ParsedTeamMailAddress(string Key, string ItemKey);
