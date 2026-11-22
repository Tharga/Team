using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>Reads a team's allowance from the registered source.</summary>
/// <remarks>Authorization is the decorator's, as for every other support service.</remarks>
internal sealed class SupportAssistantAllowanceService(ISupportAssistantAllowanceSource source) : ISupportAssistantAllowanceService
{
    public Task<SupportAssistantAllowance> GetAllowanceAsync(string teamKey, CancellationToken cancellationToken = default)
        => source.GetAsync(teamKey, cancellationToken);
}

/// <summary>
/// The one place the allowance is authorized: a member of the team reads it, and the amounts stay with <see cref="SupportScopes.AssistantManage"/>.
/// </summary>
/// <remarks>
/// The share and the state go to every member, because they need to know why the assistant has stopped answering. What the team spends goes to the
/// people who choose its model and connection, who are the ones who can do something about it.
/// </remarks>
internal sealed class AuthorizationSupportAssistantAllowanceServiceDecorator(
    ISupportAssistantAllowanceService inner,
    TeamAuthorizer authorizer) : ISupportAssistantAllowanceService
{
    public async Task<SupportAssistantAllowance> GetAllowanceAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (!await authorizer.IsMemberOfAsync(teamKey))
            throw new UnauthorizedAccessException($"The support assistant's allowance for team '{teamKey}' requires being a member of that team with it selected.");

        var allowance = await inner.GetAllowanceAsync(teamKey, cancellationToken);
        return await authorizer.HasTeamScopeAsync(SupportScopes.AssistantManage, teamKey) ? allowance : allowance.WithoutAmounts();
    }
}
