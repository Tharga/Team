namespace Tharga.Team.Blazor.Features.Activity;

/// <summary>
/// Whether the person on this circuit is using the page: the one browser signal that last seen and the support desk's presence follow (Tharga/Team#399).
/// </summary>
/// <remarks>
/// Raised at most about once a minute, and only while the tab is visible and being used. It says nothing for a caller that is not at a browser (an API key,
/// an MCP client), which is not what last seen means.
/// </remarks>
public interface IActivitySignal
{
    /// <summary>When this circuit last reported use of the page, or <c>null</c> before it has.</summary>
    DateTimeOffset? LastActive { get; }

    /// <summary>Raised each time the page reports use. A handler that throws costs only its own notice.</summary>
    event EventHandler Active;
}
