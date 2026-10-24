namespace Tharga.Team;

/// <summary>
/// A team's AI connection as it may be shown: everything but the key.
/// </summary>
/// <remarks>
/// <b>No member here can carry the key.</b> A key is written once, through
/// <see cref="ISupportAiConnectionService.ConnectAsync"/>, and never read back by anyone; <see cref="HasApiKey"/> is
/// all a component needs to offer replacing it.
/// </remarks>
public sealed record SupportAiConnection
{
    /// <summary>What the assistant answers the team with.</summary>
    public SupportAiConnectionKind Kind { get; init; }

    /// <summary>The provider's address, for a provider connection; null for the platform's model and off.</summary>
    public string BaseAddress { get; init; }

    /// <summary>The model chosen at the provider; null for the platform's model and off.</summary>
    public string Model { get; init; }

    /// <summary>Whether a key is stored for the provider.</summary>
    public bool HasApiKey { get; init; }

    /// <summary>When the connection was last changed; null when the team never chose.</summary>
    public DateTime? ChangedAt { get; init; }

    /// <summary>Who last changed the connection, by name; null when the team never chose.</summary>
    public string ChangedBy { get; init; }
}

/// <summary>What a team may choose here, and where each provider starts.</summary>
public sealed record SupportAiConnectionChoices
{
    /// <summary>Every kind the team may choose, in the order to offer them.</summary>
    public SupportAiConnectionKind[] Kinds { get; init; } = [];

    /// <summary>The address each provider kind starts from, so a form can prefill it.</summary>
    public SupportAiProviderDefault[] ProviderDefaults { get; init; } = [];
}

/// <summary>The address a provider kind starts from.</summary>
public sealed record SupportAiProviderDefault(SupportAiConnectionKind Kind, string BaseAddress);

/// <summary>
/// A provider connection to check, list the models of, or save.
/// </summary>
/// <remarks>
/// <b>Its text never shows the key.</b> A record prints every member, so a request logged by mistake would otherwise
/// write the key into the log; <see cref="ToString"/> is overridden to leave it out.
/// </remarks>
public sealed record SupportAiProviderRequest
{
    /// <summary>The provider: <see cref="SupportAiConnectionKind.Neurolito"/> or <see cref="SupportAiConnectionKind.Anthropic"/>.</summary>
    public SupportAiConnectionKind Kind { get; init; }

    /// <summary>The provider's address; null for the provider's default.</summary>
    public string BaseAddress { get; init; }

    /// <summary>
    /// The key; null to use the one the team already stored, which is allowed only while the kind and the address
    /// are unchanged, so a stored key is never sent somewhere it was not entered for.
    /// </summary>
    public string ApiKey { get; init; }

    /// <summary>The model to answer with. Required to connect and to test; not used to list models.</summary>
    public string Model { get; init; }

    /// <inheritdoc />
    public override string ToString()
        => $"{nameof(SupportAiProviderRequest)} {{ Kind = {Kind}, BaseAddress = {BaseAddress}, Model = {Model}, ApiKey = {(ApiKey == null ? "null" : "(hidden)")} }}";
}

/// <summary>A model a provider offers.</summary>
public sealed record SupportAiModel(string Id, string DisplayName);

/// <summary>The models a provider offers, or why they could not be listed.</summary>
public sealed record SupportAiModelList
{
    /// <summary>The models, newest first as the provider lists them; empty when <see cref="Problem"/> is set.</summary>
    public SupportAiModel[] Models { get; init; } = [];

    /// <summary>Why the models could not be listed, in words for the person setting it up; null when they were.</summary>
    public string Problem { get; init; }
}

/// <summary>The outcome of connecting to or testing a provider.</summary>
public sealed record SupportAiConnectionResult
{
    /// <summary>Whether it worked.</summary>
    public bool Succeeded { get; init; }

    /// <summary>What the model answered, for a test; null otherwise.</summary>
    public string Reply { get; init; }

    /// <summary>Why it did not work, in words for the person setting it up; null when it did.</summary>
    public string Problem { get; init; }

    /// <summary>It worked, with the model's reply for a test.</summary>
    public static SupportAiConnectionResult Success(string reply = null) => new() { Succeeded = true, Reply = reply };

    /// <summary>It did not, and why.</summary>
    public static SupportAiConnectionResult Failure(string problem) => new() { Problem = problem };
}
