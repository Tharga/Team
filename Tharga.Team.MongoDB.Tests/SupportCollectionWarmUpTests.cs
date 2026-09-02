using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The support collections are opened at startup rather than by the first request that needs them.
/// </summary>
/// <remarks>
/// <b>Why this exists, measured on 2026-09-02:</b> the first inbound Slack event after a restart took
/// 3434 ms against a warm 43–57 ms, nearly all of it creating <c>SupportEventLedger</c> and assuring its
/// unique and TTL indexes. Slack allows three seconds, so that first delivery failed and was retried on
/// every deployment.
/// <para>
/// <b>What it does not do</b> is make the inbound path safe — <see cref="ISupportEventLedger"/> already does
/// that, by making Slack's retry idempotent. This removes a wasted delivery, not a risk of losing a message,
/// and conflating the two is how the ack-first machinery would get built for a problem that is already
/// solved.
/// </para>
/// </remarks>
public class SupportCollectionWarmUpTests
{
    [Fact]
    public void ATeamRepository_RegistersTheWarmUp()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o => o.RegisterTeamRepository<TestTeamEntity, TestMember>());

        Assert.Contains(services, s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(SupportCollectionWarmUp));
    }

    /// <summary>
    /// Nothing to warm without a team repository, because that is what registers the case store.
    /// </summary>
    [Fact]
    public void WithoutATeamRepository_NothingIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o => o.RegisterUserRepository<UserServiceRepositoryBaseRaceTests.TestUserEntity>());

        Assert.DoesNotContain(services, s => s.ImplementationType == typeof(SupportCollectionWarmUp));
    }

    /// <summary>
    /// A singleton, so it runs once per process — and it takes a scope factory rather than a collection,
    /// because the collections are transient and capturing one would be the captive dependency that has
    /// already stopped this repository's sample from starting.
    /// </summary>
    [Fact]
    public void ItIsASingletonThatOpensItsOwnScope()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o => o.RegisterTeamRepository<TestTeamEntity, TestMember>());

        var descriptor = services.Single(s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(SupportCollectionWarmUp));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        var takesScopeFactory = typeof(SupportCollectionWarmUp)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Any(p => p.ParameterType == typeof(IServiceScopeFactory));

        Assert.True(takesScopeFactory, "The warm-up must resolve the transient collections from its own scope.");
    }

    public record TestMember : TeamMemberBase;

    public record TestTeamEntity : TeamEntityBase<TestMember>;

}
