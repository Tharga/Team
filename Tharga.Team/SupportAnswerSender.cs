namespace Tharga.Team;

/// <summary>An address a support answer can be sent from.</summary>
public sealed record SupportAnswerSender
{
    /// <summary>The address mail is sent from.</summary>
    public required string Address { get; init; }

    /// <summary>The name the recipient sees beside it, or null for none.</summary>
    public string Name { get; init; }

    /// <summary>True when it is the answering staff member's own address rather than one of support's.</summary>
    public bool Personal { get; init; }
}
