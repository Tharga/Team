namespace Tharga.Team;

/// <summary>Result of <see cref="IUserManagementService.LinkIdentityAsync"/>.</summary>
/// <param name="Identity">The identity the kept user signs in by from now on, which the removed duplicate held.</param>
/// <param name="PreviousIdentity">The identity the kept user held before, so the audit log can say what it was changed from.</param>
public sealed record UserLinkResult(string Identity, string PreviousIdentity);
