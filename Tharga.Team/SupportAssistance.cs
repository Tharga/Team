namespace Tharga.Team;

/// <summary>
/// Whether the person raising a case wants an assistant to answer it.
/// </summary>
/// <remarks>
/// The choice is the customer's, made once when the case is raised, and it is never made for them: a case
/// raised without asking for an assistant never sees one, however the host is configured. Presence may
/// suggest the assistant when nobody is on the support channel — suggesting is as far as it goes.
/// </remarks>
public enum SupportAssistance
{
    /// <summary>A person answers. The behaviour of every case raised before assistants existed.</summary>
    None,

    /// <summary>An assistant answers first, and the customer can still ask for a person at any point.</summary>
    Assistant
}
