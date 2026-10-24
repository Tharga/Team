namespace Tharga.Team;

/// <summary>
/// What the support assistant answers a team with — the platform's model, the team's own provider, or nothing —
/// chosen by the team.
/// </summary>
/// <remarks>
/// <b>Operations, not a settings record.</b> Using the platform's model, turning the assistant off and connecting a
/// provider are separate operations, because each is authorized, checked and audited as the fact it is. Connecting a
/// provider in particular is refused unless the provider answers first, which a generic save could not express.
/// <para>
/// Every operation requires the <c>assistant:manage</c> scope on the team. Whether a member is offered the assistant
/// is a different question, answered by <see cref="ISupportCaseService.GetAssistantReadinessAsync"/> to members.
/// </para>
/// <para>
/// <b>The key goes in and never comes out.</b> It is stored encrypted, and no operation, result, log entry or audit
/// entry carries it.
/// </para>
/// </remarks>
public interface ISupportAiConnectionService
{
    /// <summary>What the team may choose here: the platform's model only when the host has one, and the providers the host installed.</summary>
    Task<SupportAiConnectionChoices> GetChoicesAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>The team's connection; the platform's model when the team never chose.</summary>
    Task<SupportAiConnection> GetConnectionAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Answers the team with the platform's model, and forgets any key the team stored.</summary>
    Task UsePlatformModelAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Turns the assistant off for the team, and forgets any key the team stored.</summary>
    Task TurnOffAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers the team with a provider, after checking that the provider lists the chosen model for the key.
    /// Nothing is saved unless that check passes.
    /// </summary>
    Task<SupportAiConnectionResult> ConnectAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default);

    /// <summary>The models a provider offers for the key, so one can be chosen. Saves nothing.</summary>
    Task<SupportAiModelList> ListModelsAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends the model one short message and returns its reply, to show that answering works. Saves nothing.</summary>
    Task<SupportAiConnectionResult> TestAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default);
}
