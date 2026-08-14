using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// That an entry written from inside a circuit records the simulation it was written under.
/// </summary>
/// <remarks>
/// An interactive component runs in a SignalR circuit where there is no <c>HttpContext</c>, so the
/// enricher's only source returned null and the metadata was dropped from every entry a Blazor Server host
/// wrote — including all of the ones made during a demo, which is when it is wanted. Tharga/Team#220.
/// <para>
/// The principal is the fallback rather than a second cookie read because the simulation is already stamped
/// onto it as <c>AccessSimulationCookie.ClaimType</c>, for this same reason: the revalidator runs in a
/// circuit and has no cookie either.
/// </para>
/// </remarks>
public class AccessSimulationInCircuitTests
{
    private static AccessSimulation Simulation(string label) => new()
    {
        Kind = AccessSimulationKind.User,
        Label = label,
        Scopes = ["orders:read"]
    };

    private static ClaimsPrincipal PrincipalWith(string simulationValue)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "alice") };
        if (simulationValue != null) claims.Add(new Claim(AccessSimulationCookie.ClaimType, simulationValue));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static IHttpContextAccessor NoHttpContext()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(x => x.HttpContext).Returns((HttpContext)null);
        return accessor.Object;
    }

    private static IHttpContextAccessor HttpContextWith(string simulationValue)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(x => x.HttpContext).Returns(new DefaultHttpContext { User = PrincipalWith(simulationValue) });
        return accessor.Object;
    }

    private static AuditEntry Entry() => new()
    {
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ServiceCall,
        Feature = "team",
        Action = "invite",
        CallerIdentity = "Alice"
    };

    private sealed class FakeAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(user));
    }

    private sealed class FailingAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => throw new InvalidOperationException("no authentication state here");
    }

    // --- the enricher's fallback ---

    [Fact]
    public void WithNoHttpContext_TheCircuitPrincipalIsRead()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var metadata = new Dictionary<string, string>();

        using (principals.Push(PrincipalWith(AccessSimulationCookie.Write(Simulation("Bob")))))
        {
            new AccessSimulationAuditEnricher(NoHttpContext(), principals).Enrich(Entry(), metadata);
        }

        Assert.Equal("true", metadata[AccessSimulationMetadataKeys.Active]);
        Assert.Equal(nameof(AccessSimulationKind.User), metadata[AccessSimulationMetadataKeys.Kind]);
        Assert.Equal("Bob", metadata[AccessSimulationMetadataKeys.Target]);
    }

    /// <summary>
    /// The HTTP path is unchanged: where there is a context it is the answer, so controllers and SSR keep
    /// reading the principal the request was authenticated with.
    /// </summary>
    [Fact]
    public void AnHttpContextIsPreferredOverTheCircuitPrincipal()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var metadata = new Dictionary<string, string>();

        using (principals.Push(PrincipalWith(AccessSimulationCookie.Write(Simulation("Circuit")))))
        {
            new AccessSimulationAuditEnricher(HttpContextWith(AccessSimulationCookie.Write(Simulation("Http"))), principals)
                .Enrich(Entry(), metadata);
        }

        Assert.Equal("Http", metadata[AccessSimulationMetadataKeys.Target]);
    }

    [Fact]
    public void WithNeitherSource_NothingIsAdded()
    {
        var metadata = new Dictionary<string, string>();

        new AccessSimulationAuditEnricher(NoHttpContext(), new AccessSimulationPrincipalAccessor()).Enrich(Entry(), metadata);

        Assert.Empty(metadata);
    }

    /// <summary>A hand-edited value reaches the circuit path too, and must not make the audit path throw.</summary>
    [Fact]
    public void AMalformedSimulationOnTheCircuitPath_AddsNothingAndDoesNotThrow()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var metadata = new Dictionary<string, string>();

        using (principals.Push(PrincipalWith("not-base64!!")))
        {
            new AccessSimulationAuditEnricher(NoHttpContext(), principals).Enrich(Entry(), metadata);
        }

        Assert.Empty(metadata);
    }

    // --- publishing the principal for the length of an inbound activity ---

    [Fact]
    public async Task AnInboundActivity_PublishesTheCircuitPrincipal()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var user = PrincipalWith(AccessSimulationCookie.Write(Simulation("Bob")));
        var handler = new AccessSimulationCircuitHandler(new FakeAuthenticationStateProvider(user), principals);

        ClaimsPrincipal seen = null;
        var activity = handler.CreateInboundActivityHandler(_ =>
        {
            seen = principals.Current;
            return Task.CompletedTask;
        });

        await activity(null);

        Assert.Same(user, seen);
    }

    [Fact]
    public async Task TheCircuitPrincipalIsReleasedWhenTheActivityEnds()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var handler = new AccessSimulationCircuitHandler(
            new FakeAuthenticationStateProvider(PrincipalWith(null)), principals);

        await handler.CreateInboundActivityHandler(_ => Task.CompletedTask)(null);

        Assert.Null(principals.Current);
    }

    /// <summary>
    /// A leaked principal would attribute one caller's simulation to the next activity on the same flow,
    /// so the release has to survive the activity throwing.
    /// </summary>
    [Fact]
    public async Task TheCircuitPrincipalIsReleasedWhenTheActivityThrows()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var handler = new AccessSimulationCircuitHandler(
            new FakeAuthenticationStateProvider(PrincipalWith(null)), principals);

        var activity = handler.CreateInboundActivityHandler(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => activity(null));
        Assert.Null(principals.Current);
    }

    /// <summary>
    /// Recording context must never be the reason an interaction fails. Without the guard, every inbound
    /// activity in the circuit would now depend on a provider the feature only wants to read.
    /// </summary>
    [Fact]
    public async Task AFailingAuthenticationStateProvider_DoesNotBreakTheActivity()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var handler = new AccessSimulationCircuitHandler(new FailingAuthenticationStateProvider(), principals);

        var ran = false;
        await handler.CreateInboundActivityHandler(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        })(null);

        Assert.True(ran);
    }

    /// <summary>
    /// Restores rather than clears, for the same reason <see cref="AuditContextAccessor"/> does: a nested
    /// scope must hand back to its parent instead of dropping what the outer one was carrying.
    /// </summary>
    [Fact]
    public void ANestedScopeRestoresTheOuterPrincipal()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var outer = PrincipalWith(AccessSimulationCookie.Write(Simulation("Outer")));
        var inner = PrincipalWith(AccessSimulationCookie.Write(Simulation("Inner")));

        using (principals.Push(outer))
        {
            using (principals.Push(inner))
            {
                Assert.Same(inner, principals.Current);
            }

            Assert.Same(outer, principals.Current);
        }

        Assert.Null(principals.Current);
    }

    // --- the acceptance criterion, end to end ---

    /// <summary>
    /// The whole point of the issue: an entry written by a component during a demo says a simulation was
    /// active. Everything above tests one half; this is the two halves joined, with no HttpContext anywhere.
    /// </summary>
    [Fact]
    public async Task AnEntryWrittenDuringACircuitActivity_RecordsTheSimulation()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var user = PrincipalWith(AccessSimulationCookie.Write(Simulation("Bob")));
        var handler = new AccessSimulationCircuitHandler(new FakeAuthenticationStateProvider(user), principals);
        var enricher = new AccessSimulationAuditEnricher(NoHttpContext(), principals);

        var entry = Entry();
        var metadata = new Dictionary<string, string>();

        await handler.CreateInboundActivityHandler(_ =>
        {
            enricher.Enrich(entry, metadata);
            return Task.CompletedTask;
        })(null);

        Assert.Equal("true", metadata[AccessSimulationMetadataKeys.Active]);
        Assert.Equal("Bob", metadata[AccessSimulationMetadataKeys.Target]);
        Assert.Equal("Alice", entry.CallerIdentity);
    }

    /// <summary>
    /// The self-check for the test above: the same circuit, the same enricher, no simulation — and nothing
    /// is written. Otherwise "records the simulation" could mean it records unconditionally.
    /// </summary>
    [Fact]
    public async Task AnEntryWrittenOutsideASimulation_RecordsNothing()
    {
        var principals = new AccessSimulationPrincipalAccessor();
        var handler = new AccessSimulationCircuitHandler(
            new FakeAuthenticationStateProvider(PrincipalWith(null)), principals);
        var enricher = new AccessSimulationAuditEnricher(NoHttpContext(), principals);

        var metadata = new Dictionary<string, string>();

        await handler.CreateInboundActivityHandler(_ =>
        {
            enricher.Enrich(Entry(), metadata);
            return Task.CompletedTask;
        })(null);

        Assert.Empty(metadata);
    }
}
