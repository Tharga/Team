using Microsoft.Extensions.Logging;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Tests for <see cref="UnregisteredRoleScopeCheck"/> — the startup warning that restores typo safety for
/// scopes named on code-registered tenant roles (<see href="https://github.com/Tharga/Team/issues/232">Tharga/Team#232</see>).
/// </summary>
public class UnregisteredRoleScopeCheckTests
{
    private sealed class CapturingLogger : ILogger<UnregisteredRoleScopeCheck>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }

    private static async Task<CapturingLogger> Run(ScopeRegistry scopes, TenantRoleRegistry roles)
    {
        var logger = new CapturingLogger();
        await new UnregisteredRoleScopeCheck(roles, scopes, logger).StartAsync(CancellationToken.None);
        return logger;
    }

    [Fact]
    public async Task Warns_When_A_Role_Names_An_Unregistered_Scope()
    {
        var scopes = new ScopeRegistry();
        scopes.Register("case:read", AccessLevel.Administrator);

        var roles = new TenantRoleRegistry();
        roles.Register("CaseOfficer", "case:raed");

        var logger = await Run(scopes, roles);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("CaseOfficer", warning);
        Assert.Contains("case:raed", warning);
    }

    [Fact]
    public async Task Silent_When_Every_Role_Scope_Is_Registered()
    {
        var scopes = new ScopeRegistry();
        scopes.Register("case:read", AccessLevel.Administrator);

        var roles = new TenantRoleRegistry();
        roles.Register("CaseOfficer", "case:read");

        Assert.Empty((await Run(scopes, roles)).Warnings);
    }

    [Fact]
    public async Task Silent_For_A_GrantOnly_Scope_Because_It_Is_Registered()
    {
        // The whole point of RegisterGrantOnly: the scope is excluded from every automatic grant, yet still
        // present in the registry, so naming it on a role is validated rather than taken on trust.
        var scopes = new ScopeRegistry();
        scopes.RegisterGrantOnly("case:read");

        var roles = new TenantRoleRegistry();
        roles.Register("CaseOfficer", "case:read");

        Assert.Empty((await Run(scopes, roles)).Warnings);
    }

    [Fact]
    public async Task Does_Not_Throw_On_The_Documented_Role_Only_Workaround()
    {
        // Pre-3.14 hosts obtain grant-only behaviour by never registering the scope. That must keep working.
        var scopes = new ScopeRegistry();
        var roles = new TenantRoleRegistry();
        roles.Register("CaseOfficer", "case:read");

        var logger = await Run(scopes, roles);

        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task Reports_Every_Offending_Scope_Not_Just_The_First()
    {
        var scopes = new ScopeRegistry();
        var roles = new TenantRoleRegistry();
        roles.Register("CaseOfficer", ["case:raed", "case:wirte"]);

        Assert.Equal(2, (await Run(scopes, roles)).Warnings.Count);
    }

    [Fact]
    public async Task Tolerates_A_Host_With_No_Roles_Or_Scopes_Configured()
    {
        var logger = new CapturingLogger();

        await new UnregisteredRoleScopeCheck(null, null, logger).StartAsync(CancellationToken.None);

        Assert.Empty(logger.Warnings);
    }
}
