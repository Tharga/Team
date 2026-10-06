using System.Text.Json.Serialization;

namespace Tharga.Team.Entra;

internal sealed record GraphUser
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; }

    [JsonPropertyName("mail")]
    public string Mail { get; init; }

    [JsonPropertyName("userPrincipalName")]
    public string UserPrincipalName { get; init; }

    [JsonPropertyName("accountEnabled")]
    public bool? AccountEnabled { get; init; }

    public DirectoryUser ToDirectoryUser()
        => new(Id, DisplayName, Mail ?? UserPrincipalName, AccountEnabled);
}

internal sealed record GraphUserPage
{
    [JsonPropertyName("value")]
    public IReadOnlyList<GraphUser> Value { get; init; }

    [JsonPropertyName("@odata.nextLink")]
    public string NextLink { get; init; }
}

internal sealed record GraphUserIdentities
{
    [JsonPropertyName("identities")]
    public IReadOnlyList<GraphObjectIdentity> Identities { get; init; }
}

internal sealed record GraphObjectIdentity
{
    [JsonPropertyName("signInType")]
    public string SignInType { get; init; }

    [JsonPropertyName("issuer")]
    public string Issuer { get; init; }

    [JsonPropertyName("issuerAssignedId")]
    public string IssuerAssignedId { get; init; }

    public DirectoryUserIdentity ToDirectoryUserIdentity()
        => new(SignInType, Issuer, IssuerAssignedId);
}
