using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The circuit handler that publishes the person behind each inbound activity, so an entry a component writes names them (Tharga/Team#572).
/// </summary>
public class AuditCallerCircuitHandlerTests
{
    private static ClaimsPrincipal Person(string name)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, $"{name}-sub"), new Claim(DirectoryClaimTypes.ObjectId, $"{name}-oid")], "Cookies"));

    private static AuditEntry Entry() => AuditHelper.BuildEntry(null, "team", "invite", "AddMemberAsync", 1, true);

    private sealed class StateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public ClaimsPrincipal User { get; set; } = user;

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
    }

    private sealed class FailingStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => throw new InvalidOperationException("no authentication state here");
    }

    [Fact]
    public async Task AnEntryWrittenDuringAnActivity_NamesThePerson()
    {
        var handler = new AuditCallerCircuitHandler(new StateProvider(Person("Ada")));
        AuditEntry entry = null;

        await handler.CreateInboundActivityHandler(_ =>
        {
            entry = Entry();
            return Task.CompletedTask;
        })(null);

        Assert.Equal("Ada", entry.CallerIdentity);
        Assert.Equal(AuditCallerType.User, entry.CallerType);
    }

    [Fact]
    public async Task TheHostsIdentityResolver_ReadsTheSubject()
    {
        var handler = new AuditCallerCircuitHandler(new StateProvider(Person("Ada")), new UserIdentityResolver([DirectoryClaimTypes.ObjectId]));
        AuditEntry entry = null;

        await handler.CreateInboundActivityHandler(_ =>
        {
            entry = Entry();
            return Task.CompletedTask;
        })(null);

        Assert.Equal("Ada-oid", entry.CallerUserIdentity);
    }

    /// <summary>ASP.NET Core wraps only the start of an event handler: what it writes after its first await must still name the person.</summary>
    [Fact]
    public async Task WhatAHandlerWritesAfterTheActivityReturns_StillNamesThePerson()
    {
        var handler = new AuditCallerCircuitHandler(new StateProvider(Person("Ada")));
        var later = new TaskCompletionSource();
        Task<AuditEntry> rest = null;

        await handler.CreateInboundActivityHandler(_ =>
        {
            rest = WriteAfterAsync(later.Task);
            return Task.CompletedTask;
        })(null);

        later.SetResult();

        Assert.Equal("Ada", (await rest).CallerIdentity);
    }

    [Fact]
    public async Task OnceTheCircuitCloses_WorkStartedInAnActivity_NamesNobody()
    {
        var handler = new AuditCallerCircuitHandler(new StateProvider(Person("Ada")));
        var later = new TaskCompletionSource();
        Task<AuditEntry> timer = null;

        await handler.CreateInboundActivityHandler(_ =>
        {
            timer = WriteAfterAsync(later.Task);
            return Task.CompletedTask;
        })(null);

        await handler.OnCircuitClosedAsync(null, CancellationToken.None);
        later.SetResult();

        Assert.Null((await timer).CallerIdentity);
    }

    [Fact]
    public async Task EachActivity_ReadsTheClaimsAsTheyAreThen()
    {
        var state = new StateProvider(Person("Ada"));
        var handler = new AuditCallerCircuitHandler(state);
        var seen = new List<string>();
        var activity = handler.CreateInboundActivityHandler(_ =>
        {
            seen.Add(Entry().CallerIdentity);
            return Task.CompletedTask;
        });

        await activity(null);
        state.User = Person("Ada Lovelace");
        await activity(null);

        Assert.Equal(["Ada", "Ada Lovelace"], seen);
    }

    [Fact]
    public async Task AfterTheActivity_TheFlowItReturnsTo_NamesNobody()
    {
        var handler = new AuditCallerCircuitHandler(new StateProvider(Person("Ada")));

        await handler.CreateInboundActivityHandler(_ => Task.CompletedTask)(null);

        Assert.Null(Entry().CallerIdentity);
    }

    [Fact]
    public async Task AnActivityThatThrows_StillRestoresTheFlow()
    {
        var handler = new AuditCallerCircuitHandler(new StateProvider(Person("Ada")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.CreateInboundActivityHandler(_ => throw new InvalidOperationException("boom"))(null));

        Assert.Null(Entry().CallerIdentity);
    }

    /// <summary>Recording who did it must never be the reason an interaction fails.</summary>
    [Fact]
    public async Task AStateThatCannotBeRead_NamesNobody_AndTheActivityStillRuns()
    {
        var handler = new AuditCallerCircuitHandler(new FailingStateProvider());
        AuditEntry entry = null;

        await handler.CreateInboundActivityHandler(_ =>
        {
            entry = Entry();
            return Task.CompletedTask;
        })(null);

        Assert.NotNull(entry);
        Assert.Null(entry.CallerIdentity);
    }

    [Fact]
    public void TheHandler_IsOutermost_SoEveryOtherHandlerRunsWithTheCallerPublished()
    {
        Assert.Equal(int.MinValue, new AuditCallerCircuitHandler(new StateProvider(null)).Order);
    }

    [Fact]
    public void TheHandler_IsRegisteredOnce_WithoutSimulation_ThoughTheHostRegistersTheLibraryTwice()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddThargaTeamBlazor(o => o.RegisterTeamService<StubTeamService, StubUserService, StubMember>());
        services.AddThargaTeamBlazor(o => o.RegisterTeamService<StubTeamService, StubUserService, StubMember>());

        var handlers = services.Where(x => x.ServiceType == typeof(CircuitHandler) && x.ImplementationType == typeof(AuditCallerCircuitHandler)).ToArray();

        Assert.Single(handlers);
        Assert.Equal(ServiceLifetime.Scoped, handlers[0].Lifetime);
    }

    private static async Task<AuditEntry> WriteAfterAsync(Task later)
    {
        await later;
        return Entry();
    }
}
