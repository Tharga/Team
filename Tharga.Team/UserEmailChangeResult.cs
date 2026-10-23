namespace Tharga.Team;

/// <summary>Result of <see cref="IUserManagementService.SetUserEmailAsync"/>.</summary>
/// <param name="Previous">The address the user held before, or null when they held none; kept so the audit log can say what it was changed from.</param>
public sealed record UserEmailChangeResult(string Previous = null);
