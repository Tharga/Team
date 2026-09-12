using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// The built-in read-only tools: the caller's own cases, and one case's transcript.
/// </summary>
/// <remarks>
/// <b>Reads only, and deliberately few.</b> Closing, assigning and reopening stay with people — an assistant
/// that can close its own cases can close the ones it answered badly. The set is small because every tool is
/// surface the model can misuse, and because the tools worth having beyond this are the host's own.
/// </remarks>
/// <remarks>
/// <b>The case service is resolved when a tool runs, not injected.</b> It is what constructs the responder
/// that holds these tools, so taking it on the constructor would be a cycle in the container graph. By the
/// time a tool is called the scope is built and this returns the same instance the caller is using.
/// </remarks>
internal sealed class SupportAssistantTools(IServiceProvider services) : ISupportAssistantTools
{
    public Task<IReadOnlyList<AITool>> ForCurrentCallerAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var cases = services.GetRequiredService<ISupportCaseService>();

        IReadOnlyList<AITool> tools =
        [
            AIFunctionFactory.Create(
                async (CancellationToken ct) =>
                {
                    var page = await cases.GetMyCasesAsync(teamKey, cancellationToken: ct);
                    return page.Items.Select(x => new { x.Id, x.Subject, Status = x.Status.ToString(), x.CreatedAt }).ToArray();
                },
                "get_my_cases",
                "The support cases raised by the person you are answering, newest first."),

            AIFunctionFactory.Create(
                async (string caseId, CancellationToken ct) =>
                {
                    var page = await cases.GetMessagesAsync(teamKey, caseId, cancellationToken: ct);
                    return page.Items.Select(x => new { Author = x.AuthorName, x.Body, x.SentAt }).ToArray();
                },
                "get_case_messages",
                "The transcript of one of their cases, given its id from get_my_cases.")
        ];

        return Task.FromResult(tools);
    }
}
