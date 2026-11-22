using Microsoft.Extensions.AI;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// How the assistant behaves when one is available.
/// </summary>
/// <remarks>
/// <b>No model, no endpoint, no key.</b> Those belong to the <c>IChatClient</c> the host registers, which is
/// what keeps the choice of provider — a local model, a self-hosted one, or a hosted API — out of the toolkit
/// entirely.
/// </remarks>
public class SupportAssistantOptions
{
    /// <summary>
    /// What the assistant is told about its job, prepended to every case it answers.
    /// </summary>
    /// <remarks>
    /// The default says only what is true of every host: answer from the tools, and say so rather than guess.
    /// A host that knows its own product should replace it.
    /// </remarks>
    public string Instructions { get; set; } =
        "You answer support questions for the signed-in user. " +
        "Use the supplied tools to look up their teams, membership and cases rather than assuming. " +
        "If you cannot answer from what the tools return, say so plainly and tell them a person will pick it up. " +
        "When they ask for something none of your tools can do, call report_missing_skill with what they asked for, then tell them you cannot do it. " +
        "Never guess at account state, and never claim to have changed anything: you can read, not act.";

    /// <summary>
    /// How long the assistant gets before the case is left to a person.
    /// </summary>
    /// <remarks>
    /// A customer waiting on an answer is the thing being protected here. A local model on a cold start can
    /// take a while, so this is generous rather than tight — but it is bounded, because a request that never
    /// returns is worse than one that declines.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How many transcript entries the assistant is given.
    /// </summary>
    /// <remarks>
    /// Bounded because a transcript is unbounded and a context window is not. The newest entries are kept:
    /// the question being asked now matters more than how the case opened.
    /// </remarks>
    public int TranscriptLimit { get; set; } = 20;

    /// <summary>
    /// Where a case is on this site, as a path with <c>{caseId}</c>, such as <c>/support/{caseId}</c>, so the assistant
    /// can link to the cases it mentions. Null, the default, gives it no links.
    /// </summary>
    /// <remarks>
    /// A template because the route is the host's to know, as with the case link in notifications. The components show
    /// a link the assistant writes as a link only when it stays on the site; any other is shown as text.
    /// </remarks>
    public string CaseLinkTemplate { get; set; }

    /// <summary>
    /// How hard the model is asked to reason before it answers. Null, the default, asks for nothing, and the model
    /// answers as it would have.
    /// </summary>
    /// <remarks>
    /// <b>A model reasons only when it is asked to.</b> A reasoning model such as Claude, asked for nothing, answers at
    /// once and sends no reasoning, so the panel never says "Thinking…". Set this and the request carries it, and the
    /// model's reasoning is sent back so the panel can show it.
    /// <para>
    /// <b>It goes to every model the assistant uses</b>, the platform's and the teams' own. A model that does not reason
    /// can refuse the request, so set it only when the models you offer all take it.
    /// </para>
    /// </remarks>
    public ReasoningEffort? Reasoning { get; set; }

    /// <summary>
    /// Which conversation ratings are handed to every registered <see cref="ISupportConversationFeedbackSink"/>. Default
    /// thumbs-down only.
    /// </summary>
    /// <remarks>
    /// Every rating is kept on the case and counted whatever this says; it only decides which ones the host delivers. A
    /// disappointed person is what the people building the assistant most need to hear about.
    /// </remarks>
    public SupportAnswerRating[] ConversationFeedbackRatings { get; set; } = DefaultConversationFeedbackRatings;

    internal static SupportAnswerRating[] DefaultConversationFeedbackRatings => [SupportAnswerRating.Down];

    /// <summary>
    /// Whether what each tool returned to the model is kept with the answer, as <see cref="SupportAssistantCall.Result"/>. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <b>Off by default, because a tool's result can hold the tenant's own data</b> and not every host wants it kept. A host that evaluates or debugs its
    /// assistant turns it on, to tell a tool that returned the wrong thing from a model that misread a right one. Whatever a responder puts in a call's
    /// <c>Result</c> is dropped before the answer is stored when this is off. The result is never shown in the panel, and never part of the rated-conversation
    /// feedback, which names the tools and nothing they returned.
    /// </remarks>
    public bool StoreCallResults { get; set; }

    /// <summary>
    /// The longest a stored tool result may be, in characters; a longer one is cut and ends with <see cref="CutMarker"/>. Default 2,000. Used only with
    /// <see cref="StoreCallResults"/>. A value of zero or less keeps no result.
    /// </summary>
    /// <remarks>A tool can return a whole guidance document, and a case is a conversation, not a log.</remarks>
    public int MaxStoredCallResultLength { get; set; } = 2000;

    /// <summary>
    /// How long the assistant stays away from a provider that refused the account (a revoked key, billing, a usage or spend limit) before it asks again. Default 10 minutes.
    /// </summary>
    /// <remarks>Long enough not to spend a request on every message, short enough that a topped-up account is answering again without anybody restarting anything.</remarks>
    public TimeSpan ProviderRefusalRetryAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>What a team may spend on the platform's model, and how it is shown. Nothing is limited by default.</summary>
    public SupportAssistantAllowanceOptions Allowance { get; set; } = new();

    /// <summary>What a stored tool result ends with when it was cut to <see cref="MaxStoredCallResultLength"/>.</summary>
    public const string CutMarker = "… [cut]";
}
