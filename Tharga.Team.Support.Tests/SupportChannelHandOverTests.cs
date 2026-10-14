using System.Security.Claims;
using NSubstitute;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A conversation reaches a support channel when a person is asked for — at once when the member asks for one,
/// and at the hand-over when the assistant answered first.
/// </summary>
public class SupportChannelHandOverTests
{
    private const string TeamKey = "team-a";

    [Fact]
    public async Task AskingForAPersonStraightAway_OpensTheChannelAtOnce()
    {
        var (service, channel) = Build();

        await service.RaiseCaseAsync(TeamKey, "Subject", "Help me.");

        await channel.Received(1).OpenAsync(Arg.Any<SupportCase>(), "Help me.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConversationWithTheAssistant_StaysOutOfTheChannel()
    {
        var (service, channel) = Build();

        await service.RaiseCaseAsync(TeamKey, "Subject", "How do I export?", SupportAssistance.Assistant);
        await service.RunAssistantAsync(TeamKey, (await Only(service)).Id);

        await channel.DidNotReceiveWithAnyArgs().OpenAsync(default, default, default);
    }

    [Fact]
    public async Task AskingForAPersonLater_OpensTheChannelWithTheConversationSoFar()
    {
        var (service, channel) = Build();
        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "How do I export?", SupportAssistance.Assistant);
        await service.RunAssistantAsync(TeamKey, raised.Id);

        await service.RequestHumanAsync(TeamKey, raised.Id);

        await channel.Received(1).OpenAsync(
            Arg.Is<SupportCase>(x => x.Id == raised.Id),
            Arg.Is<string>(x => x.Contains("How do I export?") && x.Contains("Use the export button.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskingForAPersonTwice_OpensTheChannelOnce()
    {
        var (service, channel) = Build();
        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "How do I export?", SupportAssistance.Assistant);

        await service.RequestHumanAsync(TeamKey, raised.Id);
        await service.RequestHumanAsync(TeamKey, raised.Id);

        await channel.ReceivedWithAnyArgs(1).OpenAsync(default, default, default);
    }

    private static async Task<SupportCase> Only(ISupportCaseService service)
        => Assert.Single((await service.GetMyCasesAsync(TeamKey)).Items);

    private static (ISupportCaseService Service, ISupportChannel Channel) Build()
    {
        var channel = Substitute.For<ISupportChannel>();
        channel.ChannelType.Returns(SupportChannelType.Slack);
        channel.OpenAsync(default, default, default).ReturnsForAnyArgs(new SupportChannelBinding { ChannelType = SupportChannelType.Slack, ExternalId = "1.0" });

        var responder = Substitute.For<ISupportResponder>();
        responder.AnswerAsync(default, default, default).ReturnsForAnyArgs(new SupportAnswer { Body = "Use the export button." });

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "alice"),
            new(ClaimTypes.Name, "Alice"),
            new(TeamClaimTypes.TeamKey, TeamKey)
        };

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return (new SupportCaseService(new InMemorySupportCaseStore(), authorizer, TimeProvider.System, [channel], responder: responder), channel);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
