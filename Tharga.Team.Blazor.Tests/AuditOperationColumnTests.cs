using Tharga.Team.Blazor.Features.Audit;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The Operation column on <see cref="AuditLogView"/>, which renders the checked scope or — for an entry a
/// consumer wrote, where none was checked — that entry's own feature and action.
/// </summary>
/// <remarks>
/// Before this column the grid showed <c>ScopeChecked</c> alone, so a consumer's domain entry rendered as a
/// blank cell while the thin access trace beside it looked fully labelled: the row carrying the object
/// identity was the anonymous one (Tharga/Team#260).
/// </remarks>
public class AuditOperationColumnTests
{
    private static AuditEntry Entry(string feature = null, string action = null, string scopeChecked = null) =>
        new()
        {
            Timestamp = new DateTime(2026, 09, 09, 0, 0, 0, DateTimeKind.Utc),
            EventType = AuditEventType.ServiceCall,
            Feature = feature,
            Action = action,
            ScopeChecked = scopeChecked
        };

    /// <summary>
    /// <c>ScopeProxy</c> sets feature, action <i>and</i> the scope those two compose. The scope wins, so the
    /// column does not print the same fact twice.
    /// </summary>
    [Fact]
    public void AScopeProxyTrace_ShowsTheScopeItChecked()
    {
        var entry = Entry("case", "manage", "case:manage");

        Assert.Equal("case:manage", AuditLogView.GetOperation(entry));
    }

    /// <summary>The entry this column exists for: no scope was checked, so its own labels are all there is.</summary>
    [Fact]
    public void AConsumerEntry_ShowsItsFeatureAndAction()
    {
        var entry = Entry("case", "CaseClosed");

        Assert.Equal("case:CaseClosed", AuditLogView.GetOperation(entry));
    }

    /// <summary>
    /// <c>AccessLevelProxy</c> records an access-level expression as the scope and the CLR type name as the
    /// feature. The expression is the truthful answer to "what authorized this", so it must not be replaced
    /// by a type name that would read as a scope.
    /// </summary>
    [Fact]
    public void AnAccessLevelTrace_ShowsTheLevelExpression_NotTheTypeName()
    {
        var entry = Entry("ITeamService", "CloseCaseAsync", "AccessLevel>=Administrator");

        Assert.Equal("AccessLevel>=Administrator", AuditLogView.GetOperation(entry));
    }

    [Fact]
    public void WithOnlyAFeature_TheColonIsNotInvented()
    {
        Assert.Equal("case", AuditLogView.GetOperation(Entry("case")));
    }

    [Fact]
    public void WithOnlyAnAction_TheColonIsNotInvented()
    {
        Assert.Equal("CaseClosed", AuditLogView.GetOperation(Entry(action: "CaseClosed")));
    }

    /// <summary>An empty scope string is not a checked scope — the auth entries write no scope at all.</summary>
    [Fact]
    public void AnEmptyScope_FallsBackRatherThanRenderingBlank()
    {
        var entry = Entry("user", "signin", "");

        Assert.Equal("user:signin", AuditLogView.GetOperation(entry));
    }

    [Fact]
    public void WithNothingToShow_TheCellIsEmptyRatherThanAStrayColon()
    {
        Assert.Null(AuditLogView.GetOperation(Entry()));
    }

    [Fact]
    public void ANullEntry_DoesNotThrow()
    {
        Assert.Null(AuditLogView.GetOperation(null));
    }
}
