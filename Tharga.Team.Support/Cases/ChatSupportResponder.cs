using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Answers a support case with the chat model the host gives the case's team.
/// </summary>
/// <remarks>
/// <b>The only type in the toolkit that knows an AI exists.</b> Everything above it speaks
/// <see cref="ISupportResponder"/>, which names cases and answers; everything below it is the host's
/// <see cref="IChatClient"/>, chosen per team by <see cref="ISupportChatClientResolver"/>. Swapping a local
/// model for a hosted one, or one vendor for another, is a change to the host's registration and touches
/// nothing here.
/// <para>
/// <b>It reads through the same services as any caller.</b> The tools it is given resolve through the
/// scope-checked support and team services, so the assistant sees exactly what the person it is answering
/// would see. An assistant with a wider view than its user is a data leak wearing a helpful face.
/// </para>
/// </remarks>
internal sealed class ChatSupportResponder(
    ISupportChatClientResolver chatClients,
    ISupportAssistantTools tools,
    SupportAssistantOptions options,
    ILogger<ChatSupportResponder> logger) : ISupportResponder
{
    internal const string NotConfiguredReason = "The assistant is not set up for this team.";

    public async Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
    {
        if (transcript == null || transcript.Count == 0) return SupportAnswer.Declined("The case has no messages to answer.");

        var history = new List<ChatMessage> { new(ChatRole.System, options.Instructions) };
        history.AddRange(transcript.TakeLast(options.TranscriptLimit).Select(ToChatMessage));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);

        try
        {
            // Inside the try: finding the model is as much a part of answering as calling it, and a team's
            // settings being unreadable must decline like a provider being down rather than fail the caller.
            var chat = await chatClients.GetAsync(supportCase.TeamKey, cancellationToken);
            if (chat == null)
            {
                logger.LogInformation("The support assistant has no model for team {TeamKey}, so case {CaseId} is left for a person.", supportCase.TeamKey, supportCase.Id);
                return SupportAnswer.Declined(NotConfiguredReason);
            }

            var response = await chat.GetResponseAsync(
                history,
                new ChatOptions { Tools = [.. await tools.ForCurrentCallerAsync(supportCase.TeamKey, cancellationToken)] },
                deadline.Token);

            var body = response.Text?.Trim();

            return string.IsNullOrWhiteSpace(body)
                ? SupportAnswer.Declined("The assistant returned nothing.")
                : SupportAnswer.FromBody(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The assistant's own deadline, not the caller going away. Declining leaves the case for a
            // person, which is the outcome the customer would have had anyway.
            logger.LogWarning("The support assistant did not answer case {CaseId} within {Timeout}.", supportCase.Id, options.Timeout);
            return SupportAnswer.Declined("The assistant took too long to answer.");
        }
        catch (Exception ex)
        {
            // A provider being down must not stop somebody reporting a problem. The case already exists and
            // is authoritative; the assistant failing to answer it is a degraded path, not a lost one.
            logger.LogError(ex, "The support assistant failed on case {CaseId}.", supportCase.Id);
            return SupportAnswer.Declined("The assistant could not be reached.");
        }
    }

    /// <remarks>
    /// <b>A failing lookup reads as not configured, never as an exception.</b> Readiness is asked while a case
    /// is being raised, and somebody reporting a problem must not be stopped because the team's model
    /// settings could not be read. The case then goes to a person, which is what they would get anyway.
    /// </remarks>
    public async Task<SupportAssistantReadiness> GetReadinessAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        try
        {
            return await chatClients.GetAsync(teamKey, cancellationToken) == null
                ? SupportAssistantReadiness.NotConfigured
                : SupportAssistantReadiness.Ready;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not find the support assistant's model for team {TeamKey}.", teamKey);
            return SupportAssistantReadiness.NotConfigured;
        }
    }

    /// <remarks>
    /// The customer is the user and everyone answering is the assistant, whether the earlier answer came from
    /// a person or from a model. The distinction the model needs is "who is asking" against "what has already
    /// been said to them"; which colleague said it is not something it can act on.
    /// </remarks>
    private static ChatMessage ToChatMessage(SupportMessage message) =>
        new(message.Kind == SupportMessageKind.User ? ChatRole.User : ChatRole.Assistant, message.Body);
}
