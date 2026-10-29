using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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

    internal const string MissingSkillTool = "report_missing_skill";

    internal const string MissingSkillNoted = "Noted for the people who build this assistant. Now tell the person plainly that you cannot do this, and that a person can help.";

    public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
        => AnswerAsync(supportCase, transcript, null, cancellationToken);

    /// <remarks>Streams only when somebody is watching; without <paramref name="progress"/> the model is asked for the answer in one piece, as it always was.</remarks>
    public async Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, IProgress<SupportAssistantProgress> progress, CancellationToken cancellationToken = default)
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

            // Who serves and with which model, for the usage; a client wrapped for function calls asks the one inside.
            var metadata = chat.GetService<ChatClientMetadata>();
            var gaps = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var chatOptions = new ChatOptions { Tools = [.. await tools.ForCurrentCallerAsync(supportCase.TeamKey, cancellationToken), MissingSkill(gaps)] };

            string body;
            SupportAssistantCall[] calls;
            SupportAssistantUsage usage;

            if (progress == null)
            {
                // The tool loop adds up the usage of its rounds into the response it returns.
                var response = await chat.GetResponseAsync(history, chatOptions, deadline.Token);
                body = response.Text?.Trim();
                calls = [.. response.Messages.SelectMany(x => x.Contents).OfType<FunctionCallContent>().Where(IsServiceCall).Select(CallOf)];
                usage = UsageOf([response.Usage], metadata?.ProviderName, response.ModelId ?? metadata?.DefaultModelId);
            }
            else
            {
                (body, calls, usage) = await StreamAsync(chat, history, chatOptions, progress, supportCase.Id, metadata, deadline.Token);
                body = body.Trim();
            }

            var answer = string.IsNullOrWhiteSpace(body)
                ? SupportAnswer.Declined("The assistant returned nothing.")
                : SupportAnswer.FromBody(body);

            return answer with { Gaps = [.. gaps], Calls = calls, Usage = usage };
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
    /// <b>Always given, whatever tools the host supplies</b>, because what the assistant could not do is what tells
    /// the people building it what to add. It changes nothing but the list it is given.
    /// </remarks>
    private static AIFunction MissingSkill(System.Collections.Concurrent.ConcurrentQueue<string> gaps) =>
        AIFunctionFactory.Create(
            (string what) =>
            {
                if (!string.IsNullOrWhiteSpace(what)) gaps.Enqueue(what);
                return MissingSkillNoted;
            },
            MissingSkillTool,
            "Call this when the person asks for something none of your other tools can do, so the people who build this " +
            "assistant can add it. Pass what they asked for in a few words, such as \"search cases by subject\". Then tell " +
            "them plainly that you cannot do it.");

    /// <remarks>
    /// <b>Rounds are read from what the tool loop yields:</b> the model's call for a tool comes through as it is made,
    /// and the tool's result as it goes back, which is when the next call to the model starts.
    /// <para>
    /// <b>A reader that fails is dropped, not the answer.</b> The reports are a preview for whoever is watching; the
    /// answer they preview is still kept.
    /// </para>
    /// </remarks>
    private async Task<(string Body, SupportAssistantCall[] Calls, SupportAssistantUsage Usage)> StreamAsync(IChatClient chat, List<ChatMessage> history, ChatOptions chatOptions, IProgress<SupportAssistantProgress> progress, string caseId, ChatClientMetadata metadata, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var round = 1;
        List<string> running = [];
        List<SupportAssistantCall> calls = [];
        List<UsageDetails> usage = [];
        string modelId = null;
        var waiting = false;
        var reporting = true;

        void Report()
        {
            if (!reporting) return;

            try
            {
                progress.Report(new SupportAssistantProgress { AnswerSoFar = text.ToString(), Round = round, Tools = [.. running], Calls = [.. calls] });
            }
            catch (Exception ex)
            {
                reporting = false;
                logger.LogWarning(ex, "Showing the support assistant's progress on case {CaseId} failed; the answer is still kept.", caseId);
            }
        }

        Report();

        await foreach (var update in chat.GetStreamingResponseAsync(history, chatOptions, cancellationToken))
        {
            // Streamed, each round reports its own, and nothing adds them up.
            usage.AddRange(update.Contents.OfType<UsageContent>().Select(x => x.Details));
            modelId = update.ModelId ?? modelId;

            var asked = update.Contents.OfType<FunctionCallContent>().ToArray();
            if (asked.Length > 0)
            {
                waiting = true;
                var shown = asked.Where(IsServiceCall).ToArray();
                running.AddRange(shown.Select(x => x.Name));
                calls.AddRange(shown.Select(CallOf));
                Report();
            }

            if (waiting && update.Contents.OfType<FunctionResultContent>().Any())
            {
                waiting = false;
                running.Clear();
                round++;
                Report();
            }

            if (string.IsNullOrEmpty(update.Text)) continue;

            text.Append(update.Text);
            Report();
        }

        return (text.ToString(), [.. calls], UsageOf(usage, metadata?.ProviderName, modelId ?? metadata?.DefaultModelId));
    }

    /// <summary>The tokens reported, added up; <c>null</c> when nothing was reported, so no count reads as zero.</summary>
    internal static SupportAssistantUsage UsageOf(IEnumerable<UsageDetails> reported, string provider = null, string model = null)
    {
        var details = reported.Where(x => x != null).ToArray();
        if (details.Length == 0) return null;

        return new SupportAssistantUsage
        {
            InputTokens = details.Sum(x => x.InputTokenCount ?? 0),
            OutputTokens = details.Sum(x => x.OutputTokenCount ?? 0),
            TotalTokens = details.Sum(x => x.TotalTokenCount ?? (x.InputTokenCount ?? 0) + (x.OutputTokenCount ?? 0)),
            CachedInputTokens = details.Sum(x => x.CachedInputTokenCount ?? 0),
            Provider = provider,
            Model = model
        };
    }

    /// <remarks>Noting what the assistant could not do is not a call on the host's services, so it is neither shown nor kept as one.</remarks>
    private static bool IsServiceCall(FunctionCallContent call) => call.Name != MissingSkillTool;

    private static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The call as a person reads it: <c>text: "export", pageSize: 10</c>, values as the model passed them.</summary>
    internal static SupportAssistantCall CallOf(FunctionCallContent call) => new()
    {
        Name = call.Name,
        Arguments = call.Arguments is { Count: > 0 } arguments
            ? string.Join(", ", arguments.Select(x => $"{x.Key}: {ValueOf(x.Value)}"))
            : string.Empty
    };

    private static string ValueOf(object value) => value switch
    {
        null => "null",
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(value, Written)
    };

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
