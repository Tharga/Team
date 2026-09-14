using Microsoft.Extensions.AI;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// The tools an assistant may call while answering a case.
/// </summary>
/// <remarks>
/// <b>A host is expected to add its own.</b> The toolkit knows about teams, membership and cases; it knows
/// nothing about the product the customer is actually asking about, so answering well means the host
/// supplying tools over its own domain. Implement this to replace the built-in set, or resolve the built-in
/// one and add to what it returns.
/// <para>
/// Every tool must read through a scope-checked service, so it inherits the caller's authorization instead of
/// re-deciding it. A tool that reaches the store directly is a second enforcement point, and the assistant
/// would be the one caller able to use it.
/// </para>
/// </remarks>
public interface ISupportAssistantTools
{
    /// <summary>Tools bound to the current caller and team.</summary>
    Task<IReadOnlyList<AITool>> ForCurrentCallerAsync(string teamKey, CancellationToken cancellationToken = default);
}
