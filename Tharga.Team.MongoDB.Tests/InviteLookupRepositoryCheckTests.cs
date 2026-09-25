using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tharga.Team;
using Tharga.Team.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The store half of Tharga/Team#286: a host replacing the team repository without implementing
/// <c>GetByInviteKeyAsync</c> mints links that never resolve. The service-side check cannot see it, because
/// the repository base overrides the service's half.
/// </summary>
public class InviteLookupRepositoryCheckTests
{
    private interface IProbe
    {
        Task<string> FindAsync() => Task.FromResult<string>(null);
    }

    private sealed class ProbeUsingDefault : IProbe;

    private sealed class ProbeImplementing : IProbe
    {
        public Task<string> FindAsync() => Task.FromResult("found");
    }

    [Fact]
    public void UsesDefault_TrueOnlyWhenTheImplementationLeavesTheDefaultBody()
    {
        Assert.True(TeamRepositoryCompleteness.UsesDefault(typeof(ProbeUsingDefault), typeof(IProbe), nameof(IProbe.FindAsync)));
        Assert.False(TeamRepositoryCompleteness.UsesDefault(typeof(ProbeImplementing), typeof(IProbe), nameof(IProbe.FindAsync)));
    }

    [Fact]
    public void UsesDefault_NotAnImplementation_IsFalse()
    {
        Assert.False(TeamRepositoryCompleteness.UsesDefault(typeof(string), typeof(IProbe), nameof(IProbe.FindAsync)));
        Assert.False(TeamRepositoryCompleteness.UsesDefault(null, typeof(IProbe), nameof(IProbe.FindAsync)));
    }

    /// <summary>The built-in repository implements the lookup, so a host on it must never be told otherwise.</summary>
    [Fact]
    public void TheBuiltInRepository_ImplementsTheLookup()
    {
        Assert.False(TeamRepositoryCompleteness.UsesDefault(
            typeof(TeamRepository<DefaultTeamEntity, DefaultTeamMember>),
            typeof(ITeamRepository<DefaultTeamEntity, DefaultTeamMember>),
            nameof(ITeamRepository<DefaultTeamEntity, DefaultTeamMember>.GetByInviteKeyAsync)));
    }

    [Fact]
    public async Task ARepositoryWithoutTheLookup_IsLoggedAsAnError_NamingItAndTheMember()
    {
        var logger = new CapturingLogger();

        await RunAsync(new RepositoryWithoutInviteLookup(), logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(nameof(RepositoryWithoutInviteLookup), entry.Message);
        Assert.Contains("GetByInviteKeyAsync", entry.Message);
    }

    [Fact]
    public async Task ARepositoryWithTheLookup_LogsNothing()
    {
        var logger = new CapturingLogger();

        await RunAsync(new RepositoryWithInviteLookup(), logger);

        Assert.Empty(logger.Entries);
    }

    /// <summary>A diagnostic must never be why an application fails to boot.</summary>
    [Fact]
    public async Task ARepositoryThatCannotBeResolved_DoesNotFailStartup()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITeamRepository<DefaultTeamEntity, DefaultTeamMember>>(_ => throw new InvalidOperationException("no store"));
        var logger = new CapturingLogger();

        await new InviteLookupRepositoryCheck<DefaultTeamEntity, DefaultTeamMember>(services.BuildServiceProvider(), logger)
            .StartAsync(CancellationToken.None);

        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    private static Task RunAsync(ITeamRepository<DefaultTeamEntity, DefaultTeamMember> repository, CapturingLogger logger)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);

        return new InviteLookupRepositoryCheck<DefaultTeamEntity, DefaultTeamMember>(services.BuildServiceProvider(), logger)
            .StartAsync(CancellationToken.None);
    }

    private sealed class CapturingLogger : ILogger<InviteLookupRepositoryCheck<DefaultTeamEntity, DefaultTeamMember>>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private class RepositoryWithoutInviteLookup : ITeamRepository<DefaultTeamEntity, DefaultTeamMember>
    {
        public IAsyncEnumerable<DefaultTeamEntity> GetTeamsByUserAsync(string userKey) => throw new NotImplementedException();
        public Task<DefaultTeamEntity> GetAsync(string teamKey) => throw new NotImplementedException();
        public Task AddAsync(DefaultTeamEntity teamEntity) => throw new NotImplementedException();
        public Task DeleteAsync(string teamKey) => throw new NotImplementedException();
        public Task RenameAsync(string teamKey, string name) => throw new NotImplementedException();
        public Task SetIconAsync(string teamKey, string reference) => throw new NotImplementedException();
        public Task SetLastSeenAsync(string teamKey, string userKey, DateTime utcNow) => throw new NotImplementedException();
        public Task AddMemberAsync(string teamKey, DefaultTeamMember member) => throw new NotImplementedException();
        public Task RemoveMemberAsync(string teamKey, string userKey) => throw new NotImplementedException();
        public Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel) => throw new NotImplementedException();
        public Task SetMemberSuspendedAsync(string teamKey, string userKey, DateTime? suspendedAt, string suspendedBy) => throw new NotImplementedException();
        public Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles) => throw new NotImplementedException();
        public Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides) => throw new NotImplementedException();
        public Task SetMemberNameAsync(string teamKey, string userKey, string name) => throw new NotImplementedException();
        public Task<ITeam> SetInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept) => throw new NotImplementedException();
        public Task SetConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null) => throw new NotImplementedException();
        public Task SetCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => throw new NotImplementedException();
        public IAsyncEnumerable<DefaultTeamEntity> GetTeamsByConsentAsync(string[] roles) => throw new NotImplementedException();
    }

    private sealed class RepositoryWithInviteLookup : RepositoryWithoutInviteLookup, ITeamRepository<DefaultTeamEntity, DefaultTeamMember>
    {
        public Task<DefaultTeamEntity> GetByInviteKeyAsync(string inviteKey) => Task.FromResult<DefaultTeamEntity>(null);
    }
}
