using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>A team's notice defaults: the catalogue's until set, stored only where they differ, audited against the team, and reached only with <c>notices:manage</c> on that team.</summary>
public class UserNoticeTeamDefaultsTests
{
    private readonly UserNoticeFixture _fixture = new();
    private readonly RecordingAuditLogger _audit = new();

    [Fact]
    public async Task ATeamsDefaults_AreTheCataloguesUntilSet()
    {
        var defaults = await _fixture.TeamDefaults.GetTeamDefaultsAsync(UserNoticeFixture.Alpha);

        Assert.Equal(["case.assigned", "case.opened", "member.joined", "content.published"], defaults.Select(x => x.Notice.Key));
        Assert.All(defaults, x => Assert.False(x.IsSet));
        Assert.Equal(UserNoticeDelivery.Digest, defaults[1].Delivery);
        Assert.False(defaults[3].Enabled);
    }

    [Fact]
    public async Task ADefaultSet_IsStoredWhereItDiffers_AndAuditedAgainstTheTeam()
    {
        var service = _fixture.TeamDefaults;

        var set = await service.SetTeamDefaultAsync(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key, true, UserNoticeDelivery.AtOnce);

        Assert.True(set.IsSet);
        Assert.Equal(new UserNoticeSetting { Delivery = UserNoticeDelivery.AtOnce }, _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key)]);
        var entry = Assert.Single(_fixture.Mail.Audit.Entries);
        Assert.Equal(UserNoticeAudit.DefaultAction, entry.Action);
        Assert.Equal(UserNoticeFixture.Alpha, entry.TeamKey);
        Assert.Equal("AtOnce", entry.Metadata[UserNoticeAudit.Delivery]);
        Assert.DoesNotContain(await service.GetTeamDefaultsAsync(UserNoticeFixture.Beta), x => x.IsSet);

        var back = await service.SetTeamDefaultAsync(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key, true, UserNoticeDelivery.Digest);

        Assert.False(back.IsSet);
        Assert.Empty(_fixture.Store.TeamDefaults);
        Assert.Equal(2, _fixture.Mail.Audit.Entries.Count);
    }

    [Fact]
    public async Task SettingWhatIsAlreadyInForce_IsNotAudited()
    {
        await _fixture.TeamDefaults.SetTeamDefaultAsync(UserNoticeFixture.Alpha, UserNoticeFixture.Joined.Key, true, UserNoticeDelivery.AtOnce);

        Assert.Empty(_fixture.Mail.Audit.Entries);
        Assert.Empty(_fixture.Store.TeamDefaults);
    }

    [Fact]
    public async Task ADeliveryTheNoticeDoesNotAllow_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.TeamDefaults.SetTeamDefaultAsync(UserNoticeFixture.Alpha, UserNoticeFixture.Published.Key, true, UserNoticeDelivery.AtOnce));
    }

    [Fact]
    public async Task TheScopeOnTheTeam_ReadsAndSetsItsDefaults()
    {
        var service = Resolve(Principal(UserNoticeFixture.Alpha, new Claim(TeamClaimTypes.Scope, NoticeScopes.Manage)));

        await service.SetTeamDefaultAsync(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key, false, UserNoticeDelivery.Digest);

        Assert.False(_fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key)].Enabled);
        Assert.NotEmpty(await service.GetTeamDefaultsAsync(UserNoticeFixture.Alpha));
        Assert.DoesNotContain(_audit.Entries, x => x.EventType == AuditEventType.ScopeDenial);
    }

    [Fact]
    public async Task TheScopeOnAnotherTeam_GrantsNothing()
    {
        var service = Resolve(Principal(UserNoticeFixture.Alpha, new Claim(TeamClaimTypes.Scope, NoticeScopes.Manage)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetTeamDefaultsAsync(UserNoticeFixture.Beta));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetTeamDefaultAsync(UserNoticeFixture.Beta, UserNoticeFixture.Opened.Key, false, UserNoticeDelivery.Digest));
        Assert.Empty(_fixture.Store.TeamDefaults);
    }

    [Fact]
    public async Task NoScope_OrTheSameNameAsASystemScope_GrantsNothing_AndTheRefusalIsAudited()
    {
        var none = Resolve(Principal(UserNoticeFixture.Alpha));
        var system = Resolve(Principal(UserNoticeFixture.Alpha, new Claim(TeamClaimTypes.SystemScope, NoticeScopes.Manage)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => none.SetTeamDefaultAsync(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key, false, UserNoticeDelivery.Digest));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => system.SetTeamDefaultAsync(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key, false, UserNoticeDelivery.Digest));

        Assert.Empty(_fixture.Store.TeamDefaults);
        Assert.Contains(_audit.Entries, x => x.EventType == AuditEventType.ScopeDenial && x.MethodName == nameof(IUserNoticeTeamService.SetTeamDefaultAsync));
    }

    [Fact]
    public void TheScope_IsGrantedToAdministratorsByDefault_AndNotToUsers()
    {
        var scopes = Scopes(_ => { });

        Assert.Contains(NoticeScopes.Manage, scopes.GetScopesForAccessLevel(AccessLevel.Administrator));
        Assert.Contains(NoticeScopes.Manage, scopes.GetScopesForAccessLevel(AccessLevel.Owner));
        Assert.DoesNotContain(NoticeScopes.Manage, scopes.GetScopesForAccessLevel(AccessLevel.User));
    }

    [Fact]
    public void TheScope_FollowsTheHostsLevel_OrIsGrantOnly()
    {
        Assert.Contains(NoticeScopes.Manage, Scopes(o => o.ManageAccessLevel = AccessLevel.User).GetScopesForAccessLevel(AccessLevel.User));

        var grantOnly = Scopes(o => o.ManageAccessLevel = null);
        Assert.DoesNotContain(NoticeScopes.Manage, grantOnly.GetScopesForAccessLevel(AccessLevel.Owner));
        Assert.True(grantOnly.All.Single(x => x.Name == NoticeScopes.Manage).GrantOnly);
    }

    [Fact]
    public async Task PurgingATeam_RemovesItsDefaults_AndNoOtherTeams()
    {
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key)] = new UserNoticeSetting { Enabled = false };
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Beta, UserNoticeFixture.Opened.Key)] = new UserNoticeSetting { Enabled = false };

        var removed = await new UserNoticePurgeParticipant(_fixture.Store).PurgeTeamDataAsync(UserNoticeFixture.Alpha);

        Assert.Equal(1, removed);
        Assert.Equal([(UserNoticeFixture.Beta, UserNoticeFixture.Opened.Key)], _fixture.Store.TeamDefaults.Keys);
    }

    private IUserNoticeTeamService Resolve(ClaimsPrincipal principal)
    {
        var services = Registered(_ => { });
        services.AddSingleton<ITeamPrincipalAccessor>(new FixedPrincipal(principal));
        services.AddSingleton(new CompositeAuditLogger([_audit], Options.Create(new AuditOptions())));
        services.AddSingleton<IUserNoticePreferenceStore>(_fixture.Store);

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IUserNoticeTeamService>();
    }

    private static IScopeRegistry Scopes(Action<UserNoticeOptions> configure)
        => Registered(configure).BuildServiceProvider().GetRequiredService<IScopeRegistry>();

    private static ServiceCollection Registered(Action<UserNoticeOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.AddCategory(new UserMailCategory { Key = UserNoticeFixture.Work, Name = "Work" });
        });
        services.AddThargaUserNotices(o =>
        {
            o.Add(UserNoticeFixture.Assigned);
            o.Add(UserNoticeFixture.Opened);
            configure(o);
        });
        return services;
    }

    private static ClaimsPrincipal Principal(string teamKey, params Claim[] claims)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Ann Admin"), new Claim(TeamClaimTypes.TeamKey, teamKey), .. claims], "Cookies"));

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
