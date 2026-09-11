using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A short invitation link resolves through the service chain a host actually gets from
/// <c>AddThargaTeamBlazor</c>, not merely through a bare team service.
/// </summary>
/// <remarks>
/// The resolution tests in <c>Tharga.Team.Service.Tests</c> drive the management service over a stub team
/// service, which is why they stayed green while every real host resolved short links to nothing: the
/// registration wraps the host's service in decorators, and those were the part that dropped the lookup
/// (Tharga/Team#272). Both shapes of the chain are covered — with audit logging, and without.
/// </remarks>
public class ShortInvitationLinkResolutionTests
{
    private const string Token = "84Fb6G_8BbXE";
    private const string TeamKey = "team-1";
    private const string EMail = "invitee@example.com";

    private static ServiceProvider Provider(bool withAuditLogging)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, AnonymousAuthenticationStateProvider>();
        if (withAuditLogging) services.AddThargaAuditLogging();

        services.AddThargaTeamBlazor(o => o.RegisterTeamService<InvitingTeamService, StubUserService, StubMember>());
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AShortToken_ResolvesToItsInvitation(bool withAuditLogging)
    {
        using var provider = Provider(withAuditLogging);
        using var scope = provider.CreateScope();

        var invitation = await scope.ServiceProvider.GetRequiredService<ITeamInvitationService>().GetInvitationAsync(Token);

        Assert.NotNull(invitation);
        Assert.Equal(TeamKey, invitation.TeamKey);
        Assert.Equal(EMail, invitation.EMail);
        Assert.Equal(Token, invitation.InviteKey);
    }

    /// <summary>Without the decorators in the path the test above would prove nothing about them.</summary>
    [Fact]
    public void TheRegisteredTeamService_IsDecorated()
    {
        using var provider = Provider(withAuditLogging: true);
        using var scope = provider.CreateScope();

        Assert.IsType<AuthorizationTeamServiceDecorator>(scope.ServiceProvider.GetRequiredService<ITeamService>());
    }

    private sealed class AnonymousAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class InvitingTeamService : StubTeamService
    {
        protected override Task<ITeam> GetTeamAsync(string teamKey)
            => Task.FromResult<ITeam>(teamKey == TeamKey ? new InvitingTeam() : null);

        protected override Task<string> GetTeamKeyByInviteKeyInternalAsync(string inviteKey)
            => Task.FromResult(inviteKey == Token ? TeamKey : null);
    }

    private sealed record InvitingTeam : ITeam<StubMember>
    {
        public string Key => TeamKey;
        public string Name => "Test Team";
        public string Icon => null;

        public StubMember[] Members { get; init; } =
        [
            new()
            {
                Key = "member-key",
                AccessLevel = AccessLevel.User,
                State = MembershipState.Invited,
                Invitation = new Invitation { EMail = EMail, InviteKey = Token, InviteTime = DateTime.UtcNow }
            }
        ];
    }
}
