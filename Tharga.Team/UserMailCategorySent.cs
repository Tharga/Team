namespace Tharga.Team;

/// <summary>How many mails in one category an outbox sent to users since a time, and to how many users; for a contact category, to how many addresses.</summary>
/// <param name="Category">The category's key.</param>
/// <param name="Sent">The mails sent or bounced: to a user rather than to an address alone, or for a contact category to a contact.</param>
/// <param name="Recipients">The users who received at least one of them, or for a contact category the distinct addresses.</param>
public sealed record UserMailCategorySent(string Category, int Sent, int Recipients);
