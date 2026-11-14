namespace Tharga.Team;

/// <summary>The From and Reply-To a user mail was sent with, or would have been sent with when it was not, as the sender resolved them.</summary>
/// <param name="FromAddress">The sending address.</param>
/// <param name="FromName">The sending display name, or null when there is none.</param>
/// <param name="ReplyTo">Where replies go, or null when they go to the sending address.</param>
public sealed record UserMailHeaders(string FromAddress, string FromName, string ReplyTo);
