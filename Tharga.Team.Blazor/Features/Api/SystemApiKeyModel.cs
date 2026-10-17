namespace Tharga.Team.Blazor.Features.Api;

/// <summary>UI model for a system-level API key (not bound to a team).</summary>
public record SystemApiKeyModel
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string ApiKey { get; init; }
    public string VisibleKey { get; set; }
    public string[] SystemScopes { get; init; } = [];
    public DateTime? ExpiryDate { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public string CreatedBy { get; init; }

    /// <summary>The owning user's key for a personal key; null for a shared one.</summary>
    public string OwnerMemberKey { get; init; }

    /// <summary>Whether the key is personal ("private" in code, shown as "Personal").</summary>
    public bool IsPrivate => !string.IsNullOrEmpty(OwnerMemberKey);

    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate < DateTime.UtcNow;
    public DateTime? DisabledAt { get; init; }
    public string DisabledBy { get; init; }

    /// <inheritdoc cref="ApiKeyModel.IsDisabled"/>
    public bool IsDisabled => DisabledAt.HasValue;
}
