namespace Tharga.Team;

/// <summary>
/// One way a directory user signs in (for Entra: an entry of the Graph <c>identities</c> collection).
/// </summary>
/// <param name="SignInType">How the user signs in, e.g. <c>federated</c>, <c>emailAddress</c> or <c>userPrincipalName</c>.</param>
/// <param name="Issuer">Who issued the identity; for a federated identity, the external identity provider.</param>
/// <param name="IssuerAssignedId">The identifier the issuer assigned; for a federated identity, the upstream subject.</param>
public sealed record DirectoryUserIdentity(string SignInType, string Issuer, string IssuerAssignedId);
