using Microsoft.Extensions.Options;
using Tharga.Mcp;
using Tharga.Team.Mcp;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Mcp.Tests;

public class TeamSystemResourceProviderTests
{
    private readonly IApiKeyAdministrationService _apiKeyService = Substitute.For<IApiKeyAdministrationService>();
    private readonly ITenantRoleRegistry _roleRegistry = Substitute.For<ITenantRoleRegistry>();
    private readonly CompositeAuditLogger _auditLogger;

    public TeamSystemResourceProviderTests()
    {
        _auditLogger = new CompositeAuditLogger(
            Enumerable.Empty<IAuditLogger>(),
            Options.Create(new AuditOptions()));
    }

    private TeamMcpContext MakeContext(bool isDeveloper)
        => TestMcpContextFactory.Create(isDeveloper: isDeveloper, scope: McpScope.System);

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    /// <remarks>
    /// Renamed: it used to say "non-developer returns empty", which is no longer the whole truth — a
    /// caller holding <c>audit:read</c> without the role now sees the audit resource, and a test whose
    /// name overstates its scenario is how the next reader concludes the opposite.
    /// </remarks>
    [Fact]
    public async Task ListResourcesAsync_NonDeveloperWhoCanReadNothing_ReturnsEmpty()
    {
        // No oversight service, so audit is unreadable for anyone here.
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        var result = await sut.ListResourcesAsync(MakeContext(isDeveloper: false), TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListResourcesAsync_Developer_ReturnsAllAvailableResources()
    {
        // Audit is listed only when the caller could read it, so the service that decides that has to be
        // present. Registering the logger alone used to be enough, which was the defect: it says the
        // feature exists, not that this caller may use it.
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger, Readable());

        var result = await sut.ListResourcesAsync(MakeContext(isDeveloper: true), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.Uri == TeamSystemResourceProvider.SystemKeysUri);
        Assert.Contains(result, r => r.Uri == TeamSystemResourceProvider.RolesUri);
        Assert.Contains(result, r => r.Uri == TeamSystemResourceProvider.AuditUri);
    }

    [Fact]
    public async Task ListResourcesAsync_OmitsAuditWhenAuditLoggerNotRegistered()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, auditLogger: null);

        var result = await sut.ListResourcesAsync(MakeContext(isDeveloper: true), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result, r => r.Uri == TeamSystemResourceProvider.AuditUri);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadResourceAsync_NonDeveloper_Throws()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.ReadResourceAsync(TeamSystemResourceProvider.RolesUri, MakeContext(isDeveloper: false), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadResourceAsync_UnknownUri_Throws()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReadResourceAsync("team://system/unknown", MakeContext(isDeveloper: true), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadResourceAsync_SystemKeys_RedactsRawApiKeyAndHash()
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns("key-1");
        key.Name.Returns("mcp-gate");
        key.ApiKey.Returns("SHOULD_NOT_BE_EXPOSED");
        key.SystemScopes.Returns(new[] { "mcp:discover" });
        key.CreatedBy.Returns("daniel");
        _apiKeyService.GetSystemKeysAsync().Returns(ToAsyncEnumerable(key));

        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        var content = await sut.ReadResourceAsync(TeamSystemResourceProvider.SystemKeysUri, MakeContext(isDeveloper: true), TestContext.Current.CancellationToken);

        Assert.NotNull(content.Text);
        Assert.Contains("mcp-gate", content.Text);
        Assert.Contains("daniel", content.Text);
        Assert.DoesNotContain("SHOULD_NOT_BE_EXPOSED", content.Text);
        Assert.DoesNotContain("ApiKeyHash", content.Text);
        Assert.Equal("application/json", content.MimeType);
    }

    [Fact]
    public async Task ReadResourceAsync_Roles_ReturnsRoleNames()
    {
        var role = new TenantRoleDefinition("Editor", new[] { "feature:read", "feature:write" });
        _roleRegistry.All.Returns(new[] { role });

        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        var content = await sut.ReadResourceAsync(TeamSystemResourceProvider.RolesUri, MakeContext(isDeveloper: true), TestContext.Current.CancellationToken);

        Assert.Contains("Editor", content.Text);
        Assert.Contains("feature:read", content.Text);
    }

    /// <remarks>
    /// Reads through <see cref="IAuditOversightService"/> now, not the logger. That service carries
    /// <c>[RequireScope(audit:read)]</c> as a system grant, so the authorization lives with it and this
    /// provider performs no audit check of its own — where it previously asked whether the caller held a
    /// host-configurable role, a rule neither the UI nor REST used.
    /// </remarks>
    [Fact]
    public async Task ReadResourceAsync_Audit_ReturnsQueryResult()
    {
        var oversight = Substitute.For<IAuditOversightService>();
        oversight.QueryAllAsync(Arg.Any<AuditQuery>()).Returns(new AuditQueryResult());

        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger, oversight);

        var content = await sut.ReadResourceAsync(TeamSystemResourceProvider.AuditUri, MakeContext(isDeveloper: true), TestContext.Current.CancellationToken);

        Assert.NotNull(content.Text);
        Assert.Contains("items", content.Text);
        Assert.Equal("application/json", content.MimeType);
    }

    /// <summary>
    /// The provider no longer decides audit access. With no oversight service registered it fails loudly
    /// rather than falling back to reading the logger unchecked, which is the shape that let the surfaces
    /// diverge.
    /// </summary>
    [Fact]
    public async Task ReadResourceAsync_Audit_WithoutTheOversightService_Throws()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReadResourceAsync(TeamSystemResourceProvider.AuditUri, MakeContext(isDeveloper: true), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Audit is readable <b>without</b> the role, because the service decides it.
    /// </summary>
    /// <remarks>
    /// Replaces a source scan that tried to prove the same thing by looking for the word
    /// <c>IsDeveloper</c> in the file. That was brittle in both directions — it matched the XML docs and
    /// the checks that still legitimately guard the *other* system resources — so the claim is asserted
    /// by behaviour instead.
    /// <para>
    /// It also catches the real bug the scan found: the role check ran before the switch, so a holder of
    /// system <c>audit:read</c> without the role was still refused, which is precisely the divergence
    /// moving the gate into the service was meant to end.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadResourceAsync_Audit_DoesNotRequireTheRole()
    {
        var oversight = Substitute.For<IAuditOversightService>();
        oversight.QueryAllAsync(Arg.Any<AuditQuery>()).Returns(new AuditQueryResult());

        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger, oversight);

        var content = await sut.ReadResourceAsync(
            TeamSystemResourceProvider.AuditUri, MakeContext(isDeveloper: false), TestContext.Current.CancellationToken);

        Assert.NotNull(content.Text);
    }

    /// <summary>The other system resources still do require it — this narrowed one gate, not all of them.</summary>
    [Fact]
    public async Task ReadResourceAsync_OtherSystemResources_StillRequireTheRole()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.ReadResourceAsync(
            TeamSystemResourceProvider.RolesUri, MakeContext(isDeveloper: false), TestContext.Current.CancellationToken));
    }

    /// <summary>An oversight service that admits the caller.</summary>
    private static IAuditOversightService Readable()
    {
        var service = Substitute.For<IAuditOversightService>();
        service.QueryAllAsync(Arg.Any<AuditQuery>()).Returns(new AuditQueryResult());
        return service;
    }

    /// <summary>One that refuses, exactly as ScopeProxy would for a caller without the grant.</summary>
    private static IAuditOversightService Refusing()
    {
        var service = Substitute.For<IAuditOversightService>();
        service.QueryAllAsync(Arg.Any<AuditQuery>())
            .Returns<AuditQueryResult>(_ => throw new UnauthorizedAccessException("Missing required scope 'audit:read'."));
        return service;
    }

    /// <summary>
    /// <b>Discovery matches readability.</b> A caller who cannot read audit does not see it listed —
    /// advertising a resource they may not have is its own class of bug, and the spec names it so.
    /// </summary>
    [Fact]
    public async Task ListResourcesAsync_OmitsAudit_WhenTheCallerCannotReadIt()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger, Refusing());

        var result = await sut.ListResourcesAsync(MakeContext(isDeveloper: true), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result, r => r.Uri == TeamSystemResourceProvider.AuditUri);
    }

    /// <summary>
    /// And the other direction, which was equally wrong: a caller holding <c>audit:read</c> without the
    /// role could read audit but was shown nothing at all, because the whole listing was behind the role.
    /// </summary>
    [Fact]
    public async Task ListResourcesAsync_ListsAudit_ForAScopeHolderWithoutTheRole()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger, Readable());

        var result = await sut.ListResourcesAsync(MakeContext(isDeveloper: false), TestContext.Current.CancellationToken);

        Assert.Contains(result, r => r.Uri == TeamSystemResourceProvider.AuditUri);
        // ...and still nothing else: this narrowed one gate, not all of them.
        Assert.Single(result);
    }

    [Fact]
    public void Scope_IsSystem()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);
        Assert.Equal(McpScope.System, sut.Scope);
    }
}
