using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// The one place a team's AI connection is authorized: every operation requires <see cref="SupportScopes.AssistantManage"/>
/// on the team.
/// </summary>
/// <remarks>
/// Reading the connection is gated as changing it is. It shows which provider the team's conversations go to and on
/// whose account, which is the team administrators' business; whether a member is offered the assistant is answered
/// to members elsewhere, by readiness.
/// </remarks>
internal sealed class AuthorizationSupportAiConnectionServiceDecorator(
    ISupportAiConnectionService inner,
    TeamAuthorizer authorizer) : ISupportAiConnectionService
{
    public async Task<SupportAiConnectionChoices> GetChoicesAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        await RequireAsync(teamKey);
        return await inner.GetChoicesAsync(teamKey, cancellationToken);
    }

    public async Task<SupportAiConnection> GetConnectionAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        await RequireAsync(teamKey);
        return await inner.GetConnectionAsync(teamKey, cancellationToken);
    }

    public async Task UsePlatformModelAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        await RequireAsync(teamKey);
        await inner.UsePlatformModelAsync(teamKey, cancellationToken);
    }

    public async Task TurnOffAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        await RequireAsync(teamKey);
        await inner.TurnOffAsync(teamKey, cancellationToken);
    }

    public async Task<SupportAiConnectionResult> ConnectAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAsync(teamKey);
        return await inner.ConnectAsync(teamKey, request, cancellationToken);
    }

    public async Task<SupportAiModelList> ListModelsAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAsync(teamKey);
        return await inner.ListModelsAsync(teamKey, request, cancellationToken);
    }

    public async Task<SupportAiConnectionResult> TestAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAsync(teamKey);
        return await inner.TestAsync(teamKey, request, cancellationToken);
    }

    private async Task RequireAsync(string teamKey)
    {
        if (!await authorizer.HasTeamScopeAsync(SupportScopes.AssistantManage, teamKey))
            throw new UnauthorizedAccessException(
                $"Choosing the assistant's connection for team '{teamKey}' requires the '{SupportScopes.AssistantManage}' scope on that team.");
    }
}
