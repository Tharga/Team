namespace Tharga.Team;

/// <summary>How many mails in one category an outbox sent to users since a time, and to how many users.</summary>
/// <param name="Category">The category's key.</param>
/// <param name="Sent">The mails sent or bounced, to a user rather than to an address alone.</param>
/// <param name="Recipients">The users who received at least one of them.</param>
public sealed record UserMailCategorySent(string Category, int Sent, int Recipients);
