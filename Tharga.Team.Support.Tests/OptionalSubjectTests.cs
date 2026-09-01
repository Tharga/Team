using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Raising a case with and without a subject.
/// </summary>
/// <remarks>
/// <b>The derivation lives in the service, not in a component</b>, which is what these assert: a host writing
/// its own UI and passing no subject gets the same result as the shipped one, without having to know a
/// subject was expected.
/// </remarks>
public class OptionalSubjectTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";

    [Fact]
    public async Task ACaseRaisedWithNoSubject_TakesOneFromTheMessage()
    {
        var service = Build();

        var raised = await service.RaiseCaseAsync(TeamA, null, "The nightly export produced an empty file.");

        Assert.Equal("The nightly export produced an empty file.", raised.Subject);
    }

    [Fact]
    public async Task ACaseRaisedWithABlankSubject_TakesOneFromTheMessage()
    {
        var service = Build();

        var raised = await service.RaiseCaseAsync(TeamA, "   ", "The export is empty.");

        Assert.Equal("The export is empty.", raised.Subject);
    }

    [Fact]
    public async Task ASuppliedSubject_IsKept()
    {
        var service = Build();

        var raised = await service.RaiseCaseAsync(TeamA, "Export is empty", "Long body that is not the subject.");

        Assert.Equal("Export is empty", raised.Subject);
    }

    [Fact]
    public async Task ASuppliedSubject_IsTrimmed()
    {
        var service = Build();

        var raised = await service.RaiseCaseAsync(TeamA, "  Export is empty  ", "Body.");

        Assert.Equal("Export is empty", raised.Subject);
    }

    /// <summary>
    /// <see cref="SupportCase.Subject"/> is not nullable, and a case with a blank one renders as an empty row
    /// in every list that shows cases.
    /// </summary>
    [Fact]
    public async Task ACaseNeverEndsUpWithoutASubject()
    {
        var service = Build();

        var raised = await service.RaiseCaseAsync(TeamA, null, "Anything at all.");

        Assert.False(string.IsNullOrWhiteSpace(raised.Subject));
    }

    private static ISupportCaseService Build()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Alice),
            new(ClaimTypes.Name, Alice),
            new(TeamClaimTypes.TeamKey, TeamA)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(new InMemorySupportCaseStore(), authorizer, TimeProvider.System),
            authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
