using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

/// <summary>
/// An entry written inside a Blazor Server circuit names the person behind the activity (Tharga/Team#572): after an authenticated request, before a
/// declared background actor, into the awaits of a handler that outlive the activity, and never once the circuit has closed.
/// </summary>
public class AuditCircuitCallerTests
{
    private const string TeamKey = "acme";

    private static ClaimsPrincipal Person(string name, string subject, bool authenticated = true)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(DirectoryClaimTypes.ObjectId, $"{subject}-oid"),
            new Claim(TeamClaimTypes.TeamKey, TeamKey)
        ], authenticated ? "Cookies" : null));

    private static AuditCircuitCaller.Holder Circuit(ClaimsPrincipal principal, UserIdentityResolver resolver = null)
    {
        var holder = new AuditCircuitCaller.Holder();
        holder.Update(principal, resolver);
        return holder;
    }

    private static AuditEntry Entry(IHttpContextAccessor request = null)
        => AuditHelper.BuildEntry(request, "team", "invite", "AddMemberAsync", 1, true);

    private static IHttpContextAccessor Request(ClaimsPrincipal user)
        => new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };

    [Fact]
    public void WithNoRequest_ThePersonBehindTheCircuitActivity_IsRecorded()
    {
        using (AuditCircuitCaller.Use(Circuit(Person("Ada", "ada-sub"))))
        {
            var entry = Entry();

            Assert.Equal(AuditCallerType.User, entry.CallerType);
            Assert.Equal(AuditCallerSource.Web, entry.CallerSource);
            Assert.Equal("Ada", entry.CallerIdentity);
            Assert.Equal("ada-sub", entry.CallerUserIdentity);
            Assert.Equal(TeamKey, entry.TeamKey);
        }
    }

    [Fact]
    public void TheSubject_IsReadWithTheResolverOfTheHostTheCircuitBelongsTo()
    {
        using (AuditCircuitCaller.Use(Circuit(Person("Ada", "ada-sub"), new UserIdentityResolver([DirectoryClaimTypes.ObjectId]))))
        {
            Assert.Equal("ada-sub-oid", Entry().CallerUserIdentity);
        }
    }

    [Fact]
    public void AnAuthenticatedRequest_StillWins()
    {
        using (AuditCircuitCaller.Use(Circuit(Person("Ada", "ada-sub"))))
        {
            Assert.Equal("Bob", Entry(Request(Person("Bob", "bob-sub"))).CallerIdentity);
        }
    }

    [Fact]
    public void ARequestWithNobodySignedIn_GivesWayToTheCircuit()
    {
        using (AuditCircuitCaller.Use(Circuit(Person("Ada", "ada-sub"))))
        {
            Assert.Equal("Ada", Entry(Request(new ClaimsPrincipal(new ClaimsIdentity()))).CallerIdentity);
        }
    }

    /// <summary>A person, when there is one, wins over an ambient actor, as an authenticated request already does.</summary>
    [Fact]
    public void ThePersonBehindTheCircuit_WinsOverADeclaredActor()
    {
        var actors = new AuditContextAccessor();
        using (actors.Push(new AuditActor("nightly-job")))
        using (AuditCircuitCaller.Use(Circuit(Person("Ada", "ada-sub"))))
        {
            var entry = Entry();

            Assert.Equal("Ada", entry.CallerIdentity);
            Assert.Equal(AuditCallerType.User, entry.CallerType);
        }
    }

    [Fact]
    public void WithNoCircuit_ADeclaredActor_IsRecordedAsBefore()
    {
        var actors = new AuditContextAccessor();
        using (actors.Push(new AuditActor("nightly-job")))
        {
            Assert.Equal("nightly-job", Entry().CallerIdentity);
        }
    }

    /// <summary>ASP.NET Core wraps only the start of an event handler, so what the handler does after its first await must still name the person.</summary>
    [Fact]
    public async Task TheAwaitsOfAHandler_ThatOutliveTheActivity_StillNameThePerson()
    {
        var later = new TaskCompletionSource();
        Task<AuditEntry> handler;

        using (AuditCircuitCaller.Use(Circuit(Person("Ada", "ada-sub"))))
        {
            handler = HandlerAsync(later.Task);
        }

        later.SetResult();

        Assert.Equal("Ada", (await handler).CallerIdentity);
    }

    [Fact]
    public async Task OnceTheCircuitHasClosed_WorkStartedDuringAnActivity_NamesNobody()
    {
        var later = new TaskCompletionSource();
        var circuit = Circuit(Person("Ada", "ada-sub"));
        Task<AuditEntry> timer;

        using (AuditCircuitCaller.Use(circuit))
        {
            timer = HandlerAsync(later.Task);
        }

        circuit.Clear();
        later.SetResult();

        var entry = await timer;
        Assert.Null(entry.CallerIdentity);
        Assert.Equal(AuditCallerType.Unknown, entry.CallerType);
    }

    [Fact]
    public void AfterTheActivity_TheFlowItReturnsTo_NamesNobody()
    {
        using (AuditCircuitCaller.Use(Circuit(Person("Ada", "ada-sub"))))
        {
        }

        Assert.Null(Entry().CallerIdentity);
    }

    [Fact]
    public void ACircuitWithNobodySignedIn_NamesNobody()
    {
        using (AuditCircuitCaller.Use(Circuit(Person("Ada", "ada-sub", authenticated: false))))
        {
            var entry = Entry();

            Assert.Null(entry.CallerIdentity);
            Assert.Equal(AuditCallerType.Unknown, entry.CallerType);
        }
    }

    [Fact]
    public void ACircuitWhoseStateCouldNotBeRead_NamesNobody()
    {
        using (AuditCircuitCaller.Use(Circuit(null)))
        {
            Assert.Null(Entry().CallerIdentity);
        }
    }

    private static async Task<AuditEntry> HandlerAsync(Task later)
    {
        await later;
        return Entry();
    }
}
