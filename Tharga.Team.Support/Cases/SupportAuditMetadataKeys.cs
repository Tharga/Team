namespace Tharga.Team.Support.Cases;

/// <summary>
/// Metadata keys support-case audit entries use, following the dotted convention of
/// <c>AuditMetadataKeys</c>.
/// </summary>
/// <remarks>
/// <b>There is deliberately no key for a message body.</b> A support case is where somebody pastes a
/// password, a token or a customer's details, and an audit entry travels further than the case does — more
/// readers, longer retention, easier export. Recording that a reply happened is the audit's job; recording
/// what it said is the case's.
/// </remarks>
public static class SupportAuditMetadataKeys
{
    public const string CaseId = "support.case.id";

    public const string CaseSubject = "support.case.subject";

    public const string CaseVisibility = "support.case.visibility";

    /// <summary>Where an entry sent again stands afterwards — Sent, Failed or Pending.</summary>
    public const string Delivery = "support.message.delivery";

    /// <summary>How many files a reply or answer carried; their names are kept out, as bodies are.</summary>
    public const string AttachmentCount = "support.message.attachments";

    /// <summary>
    /// What the assistant answers a team with after a change: Platform, Off, or a provider.
    /// </summary>
    /// <remarks>There is deliberately no key for the API key, for the reason there is none for a body.</remarks>
    public const string AssistantConnection = "assistant.connection";

    /// <summary>The model chosen at the provider.</summary>
    public const string AssistantModel = "assistant.model";

    /// <summary>The provider's host, without path or credentials.</summary>
    public const string AssistantHost = "assistant.host";
}
