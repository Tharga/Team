using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Notifications;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The <c>{case.url}</c> placeholder, and what it does when there is nothing to link to.
/// </summary>
/// <remarks>
/// <b>The restraint is the feature.</b> An unset template must leave the rest of the message intact, because
/// the alternative — a link to <c>http://localhost/support/</c> — goes out in front of a customer. The two
/// tests about rendering nothing matter more than the one about rendering something.
/// </remarks>
public class CaseUrlPlaceholderTests
{
    private const string Template = "https://app.example.com/support/{caseId}";

    [Fact]
    public void TheCaseUrl_IsBuiltFromTheConfiguredTemplate()
    {
        var message = Single("Case raised. {case.url}", Template, caseId: "case-1");

        Assert.Equal("Case raised. https://app.example.com/support/case-1", message);
    }

    [Fact]
    public void WithNoTemplateConfigured_TheRestOfTheMessageSurvives()
    {
        var message = Single("Case raised by {actor}. {case.url}", caseUrlTemplate: null, caseId: "case-1");

        Assert.Equal("Case raised by alice. ", message);
    }

    /// <summary>
    /// A team event borrowing the same wording must not emit a link to a case that does not exist.
    /// </summary>
    [Fact]
    public void OnAnEntryThatIsNotAboutACase_ItRendersNothing()
    {
        var message = Single("Something happened. {case.url}", Template, caseId: null);

        Assert.Equal("Something happened. ", message);
    }

    [Fact]
    public void ACaseIdNeedingEscaping_IsEscaped()
    {
        var message = Single("{case.url}", Template, caseId: "a b/c");

        Assert.Equal("https://app.example.com/support/a%20b%2Fc", message);
    }

    /// <summary>
    /// The placeholder a host writes in the template is matched without regard to case, because
    /// <c>{caseid}</c> is what somebody types.
    /// </summary>
    [Fact]
    public void TheTemplatePlaceholder_IsMatchedIgnoringCase()
    {
        var message = Single("{case.url}", "https://example.com/{CASEID}", caseId: "case-1");

        Assert.Equal("https://example.com/case-1", message);
    }

    /// <summary>
    /// The case id has always been reachable — the router falls through to audit metadata for unknown names —
    /// so this pins the behaviour the URL placeholder was built on top of.
    /// </summary>
    [Fact]
    public void TheCaseIdItself_WasAlreadyReachableFromMetadata()
    {
        var message = Single("Case {support.case.id} raised.", caseUrlTemplate: null, caseId: "case-1");

        Assert.Equal("Case case-1 raised.", message);
    }

    [Fact]
    public void RaisingACase_IsNotifiedByABuiltInRoute()
    {
        var options = new NotificationOptions { DefaultChannel = "#support", CaseUrlTemplate = Template };

        var messages = new NotificationRouter(Options.Create(options)).Route(Entry("raise", "case-1"));

        var message = Assert.Single(messages);
        Assert.Equal("#support", message.Channel);
        Assert.Contains("https://app.example.com/support/case-1", message.Text);
    }

    private static string Single(string template, string caseUrlTemplate, string caseId)
    {
        var options = new NotificationOptions
        {
            DefaultChannel = "#support",
            CaseUrlTemplate = caseUrlTemplate,
            Routes = [new NotificationRoute { Event = "support:raise", Template = template }]
        };

        var messages = new NotificationRouter(Options.Create(options)).Route(Entry("raise", caseId));

        return Assert.Single(messages).Text;
    }

    private static AuditEntry Entry(string action, string caseId) => new()
    {
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ServiceCall,
        Feature = "support",
        Action = action,
        TeamKey = "acme",
        CallerIdentity = "alice",
        Success = true,
        Metadata = caseId == null
            ? null
            : new Dictionary<string, string>
            {
                [SupportAuditMetadataKeys.CaseId] = caseId,
                [SupportAuditMetadataKeys.CaseSubject] = "Export is empty"
            }
    };
}
