using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The scope-bearing interfaces that check access themselves, rather than through <c>ScopeProxy</c>.
/// </summary>
/// <remarks>
/// <b>On these the <c>[RequireScope]</c> attributes are documentation.</b> Nothing reads them at runtime, so a
/// method that is added and attributed gets no check unless someone also writes one by hand. That is why every
/// entry is proven by <see cref="ScopeEnforcementProofTests"/> rather than trusted. Adding an entry is a
/// decision, not a fix: prefer <c>AddTeamService</c> or <c>AddSystemService</c>.
/// </remarks>
internal static class SelfCheckingServices
{
    /// <param name="Contract">The scope-bearing interface.</param>
    /// <param name="CheckedBy">What the toolkit's registration puts in front of callers, and where the checks live.</param>
    /// <param name="Why">Why it is not behind the proxy.</param>
    internal sealed record Entry(Type Contract, Type CheckedBy, string Why);

    internal static readonly Entry[] All =
    [
        new(typeof(ITeamManagementService), typeof(TeamManagementService<>),
            "Reads are checked in the service itself, changes by AuthorizationTeamServiceDecorator over ITeamService. " +
            "One instance serves five facets, and moving them onto the proxy is option 3 of Plan 01 §3b, held back as the riskiest."),
        new(typeof(IUserService), typeof(AuthorizationUserServiceDecorator),
            "Self-service calls pass and the rest need users:manage. The proxy refuses an unattributed method, and the sign-in path calls the self-service ones."),
        new(typeof(IUserManagementService), typeof(AuthorizationUserManagementServiceDecorator),
            "Every call needs users:manage. It could move onto the proxy as IIconSettingsService did, but the proxy also writes an access trace per call, which would change what the audit log records."),
    ];

    internal static Entry For(Type contract) => All.SingleOrDefault(e => e.Contract == contract);
}
