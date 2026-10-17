namespace Tharga.Team;

/// <summary>Whether one user receives one category, and whether that is their own choice or the category's default.</summary>
public sealed record UserMailChoice
{
    /// <summary>The category.</summary>
    public required UserMailCategory Category { get; init; }

    /// <summary>Whether the user receives mail in it; always true for a required category.</summary>
    public required bool Receives { get; init; }

    /// <summary>True when the user never chose otherwise, so the category's default applies.</summary>
    public required bool IsDefault { get; init; }
}
