using System.Reflection;
using Tharga.Team.Blazor.Features.Audit;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Opening filter values on <see cref="AuditLogView"/> — what the reader sees first and may then change,
/// as against <see cref="AuditPinnedFilter"/>, which hides its control and locks the query.
/// </summary>
public class AuditInitialFilterTests
{
    [Fact]
    public void AnOpeningValue_IsUsedWhenNothingIsPinned()
    {
        Assert.Equal(["case"], AuditLogView.InitialUnlessPinned(["case"], isPinned: false));
    }

    /// <summary>
    /// The property the pin depends on. An opening value is one the reader can change, so a surviving
    /// default on a pinned dimension would be an escape route out of the scope the pin imposes.
    /// </summary>
    [Fact]
    public void APinnedDimension_DiscardsTheOpeningValue()
    {
        Assert.Empty(AuditLogView.InitialUnlessPinned(["case"], isPinned: true));
    }

    [Fact]
    public void NoOpeningValue_IsEmptyRatherThanNull()
    {
        Assert.Empty(AuditLogView.InitialUnlessPinned<string>(null, isPinned: false));
    }

    [Fact]
    public void TheRuleIsTheSameForEventTypes()
    {
        AuditEventType[] initial = [AuditEventType.DataChange];

        Assert.Equal(initial, AuditLogView.InitialUnlessPinned(initial, isPinned: false));
        Assert.Empty(AuditLogView.InitialUnlessPinned(initial, isPinned: true));
    }

    /// <summary>
    /// <b>The two types must not converge.</b> A pin's scoping dimensions say which tenant, key or person a
    /// view is confined to; offering any of them as an opening value would hand the reader a control that
    /// widens the view past that confinement — the exact hole the pin exists to close. If a soft version of
    /// one is ever genuinely wanted, that is a decision to take deliberately, not by adding a property.
    /// </summary>
    [Fact]
    public void TheOpeningFilterOffersNoSoftVersionOfAScopingPin()
    {
        string[] scopingDimensions =
        [
            nameof(AuditPinnedFilter.CallerKeyId),
            nameof(AuditPinnedFilter.CallerType),
            nameof(AuditPinnedFilter.TeamKey),
            nameof(AuditPinnedFilter.CallerIdentity),
            nameof(AuditPinnedFilter.CallerUserIdentity)
        ];

        var offered = typeof(AuditInitialFilter)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(x => x.Name)
            .Intersect(scopingDimensions)
            .ToArray();

        Assert.Empty(offered);
    }
}
