namespace Tharga.Team;

/// <summary>
/// What the support assistant answers a team with.
/// </summary>
/// <remarks>
/// <see cref="Platform"/> is the zero value on purpose. A team that never chose gets the platform's model, which is
/// what every team had before teams could choose.
/// </remarks>
public enum SupportAiConnectionKind
{
    /// <summary>The model the host registers for everybody. The team brings no key.</summary>
    Platform,

    /// <summary>No assistant for this team: it is offered nobody, and every case goes to a person.</summary>
    Off,

    /// <summary>The team's own Neurolito key, at a Neurolito address.</summary>
    Neurolito,

    /// <summary>The team's own Anthropic API key.</summary>
    Anthropic
}
