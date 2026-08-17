using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Mcp;
using Tharga.Team.Mcp;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Mcp.Tests;

/// <summary>
/// Pins the invariant the `Tharga.Mcp` 2.0.0 migration rests on.
/// </summary>
/// <remarks>
/// From 2.0.0, <see cref="IMcpContext"/> carries only <see cref="McpScope"/>. Caller identity and the
/// Developer role live on this bridge's own <see cref="TeamMcpContext"/> and are recovered with
/// <c>AsTeamContext()</c>, which returns <b>null for any context this bridge did not create</b> — no bridge
/// registered, or a different one.
/// <para>
/// Every gate in the providers therefore depends on null meaning "refuse", not "allow". Nothing else tests
/// that, and getting it backwards would silently open system-scope resources to an unidentified caller — so
/// these tests assert the refusal directly, with a foreign <see cref="IMcpContext"/> that reports the
/// highest scope. Scope alone must not be enough.
/// </para>
/// </remarks>
public class ForeignMcpContextFailsClosedTests
{
    /// <summary>A context from somewhere other than this bridge, claiming the most privileged scope.</summary>
    private sealed class ForeignContext : IMcpContext
    {
        public McpScope Scope => McpScope.System;
    }

    private readonly IApiKeyAdministrationService _apiKeyService = Substitute.For<IApiKeyAdministrationService>();
    private readonly ITenantRoleRegistry _roleRegistry = Substitute.For<ITenantRoleRegistry>();
    private readonly CompositeAuditLogger _auditLogger =
        new(Enumerable.Empty<IAuditLogger>(), Options.Create(new AuditOptions()));

    [Fact]
    public async Task SystemProvider_Read_WithForeignContext_Refuses()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.ReadResourceAsync(TeamSystemResourceProvider.SystemKeysUri, new ForeignContext(), CancellationToken.None));
    }

    [Fact]
    public async Task SystemProvider_Read_WithNullContext_Refuses()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.ReadResourceAsync(TeamSystemResourceProvider.SystemKeysUri, null, CancellationToken.None));
    }

    [Fact]
    public async Task SystemProvider_List_WithForeignContext_OmitsDeveloperGatedResources()
    {
        var sut = new TeamSystemResourceProvider(_apiKeyService, _roleRegistry, _auditLogger);

        var list = await sut.ListResourcesAsync(new ForeignContext(), CancellationToken.None);

        Assert.DoesNotContain(list, x => x.Uri == TeamSystemResourceProvider.SystemKeysUri);
        Assert.DoesNotContain(list, x => x.Uri == TeamSystemResourceProvider.RolesUri);
    }

    [Fact]
    public async Task TeamProvider_Read_WithForeignContext_Refuses()
    {
        var sut = new TeamResourceProvider(
            Substitute.For<ITeamManagementService>(),
            Substitute.For<IApiKeyAdministrationService>());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.ReadResourceAsync(TeamResourceProvider.TeamUri, new ForeignContext(), CancellationToken.None));
    }

    [Fact]
    public async Task TeamProvider_List_WithForeignContext_ReturnsEmpty()
    {
        var sut = new TeamResourceProvider(
            Substitute.For<ITeamManagementService>(),
            Substitute.For<IApiKeyAdministrationService>());

        var list = await sut.ListResourcesAsync(new ForeignContext(), CancellationToken.None);

        Assert.Empty(list);
    }

    [Fact]
    public async Task UserProvider_List_WithForeignContext_ReturnsEmpty()
    {
        var sut = new TeamUserResourceProvider(
            Substitute.For<IUserService>(),
            Substitute.For<ITeamManagementService>(),
            Substitute.For<ITeamDirectoryService>(),
            Substitute.For<IHttpContextAccessor>());

        var list = await sut.ListResourcesAsync(new ForeignContext(), CancellationToken.None);

        Assert.Empty(list);
    }
}
