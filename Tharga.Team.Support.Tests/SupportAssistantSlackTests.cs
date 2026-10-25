using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Notifications;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A member talking to the assistant is not work for support, so it stays out of support's channel until they ask
/// for a person and write.
/// </summary>
public class SupportAssistantSlackTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Question = "The export is empty.";

    [Theory]
    [InlineData("raise")]
    [InlineData("reply")]
    public void TheDefaultRoutes_PostNothing_WhileTheAssistantHasTheConversation(string action)
    {
        Assert.Empty(Router().Route(Entry(action, withAssistant: "true")));
    }

    [Theory]
    [InlineData("raise", "false")]
    [InlineData("reply", "false")]
    [InlineData("raise", null)]
    [InlineData("reply", null)]
    public void TheDefaultRoutes_Post_OnceAPersonHasIt_OrWhenAnEntryDoesNotSay(string action, string withAssistant)
    {
        Assert.Single(Router().Route(Entry(action, withAssistant)));
    }

    [Fact]
    public void ARouteCanAskForTheAssistantsConversationsOnly()
    {
        var router = new NotificationRouter(Options.Create(new NotificationOptions
        {
            DefaultChannel = "#assistant",
            Routes = [new NotificationRoute { Event = "support:raise", WithAssistant = true }]
        }));

        Assert.Single(router.Route(Entry("raise", "true")));
        Assert.Empty(router.Route(Entry("raise", "false")));
    }

    [Fact]
    public async Task RaisingWithTheAssistant_IsAuditedAsWithIt_AndWithoutAsNot()
    {
        var (service, audit) = Build();

        await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);
        await service.RaiseCaseAsync(TeamA, null, Question);

        Assert.Equal(["true", "false"], audit.Entries.Select(x => x.Metadata[SupportAuditMetadataKeys.WithAssistant]));
    }

    [Fact]
    public async Task AReply_IsAuditedAsWithTheAssistant_UntilThePersonAsksForSomebody()
    {
        var (service, audit) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        await service.ReplyToCaseAsync(TeamA, raised.Id, "Still empty.");
        await service.RequestHumanAsync(TeamA, raised.Id);
        await service.ReplyToCaseAsync(TeamA, raised.Id, "Please look at it.");

        var replies = audit.Entries.Where(x => x.Action == "reply").Select(x => x.Metadata[SupportAuditMetadataKeys.WithAssistant]);
        Assert.Equal(["true", "false"], replies);
    }

    private static NotificationRouter Router() => new(Options.Create(new NotificationOptions { DefaultChannel = "#support" }));

    private static AuditEntry Entry(string action, string withAssistant)
    {
        var metadata = new Dictionary<string, string> { [SupportAuditMetadataKeys.CaseId] = "case-1", [SupportAuditMetadataKeys.CaseSubject] = Question };
        if (withAssistant != null) metadata[SupportAuditMetadataKeys.WithAssistant] = withAssistant;

        return new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.ServiceCall,
            Feature = "support",
            Action = action,
            TeamKey = TeamA,
            CallerIdentity = Alice,
            Success = true,
            Metadata = metadata
        };
    }

    private static (ISupportCaseService Service, CollectingAuditLogger Audit) Build()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Alice),
            new Claim(ClaimTypes.Name, Alice),
            new Claim(TeamClaimTypes.TeamKey, TeamA)
        ], "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));
        var audit = new CollectingAuditLogger();

        var service = new AuditingSupportCaseServiceDecorator(
            new AuthorizationSupportCaseServiceDecorator(
                new SupportCaseService(new InMemorySupportCaseStore(), authorizer, TimeProvider.System, null, null, new AnsweringResponder()),
                authorizer),
            new CompositeAuditLogger([audit], Options.Create(new AuditOptions())),
            new AuditEntryFactory(null));

        return (service, audit);
    }

    private sealed class AnsweringResponder : ISupportResponder
    {
        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
            => Task.FromResult(SupportAnswer.FromBody("Reindex the export."));
    }

    private sealed class CollectingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Log(AuditEntry entry) => Entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
