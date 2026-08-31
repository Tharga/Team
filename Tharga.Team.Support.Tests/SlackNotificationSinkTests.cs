using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Notifications;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What the sink does with the router's decision — and, mostly, what it refuses to do to the operation
/// that triggered it.
/// </summary>
public class SlackNotificationSinkTests
{
    private static AuditEntry Entry(string feature = "team", string action = "create") => new()
    {
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ServiceCall,
        Feature = feature,
        Action = action,
        TeamKey = "team-1",
        CallerIdentity = "alice"
    };

    private static (SlackNotificationSink Sink, ISlackClient Client) Build(params NotificationRoute[] routes)
    {
        var client = Substitute.For<ISlackClient>();
        client.PostAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SlackPostResult.Ok());

        var router = new NotificationRouter(Options.Create(new NotificationOptions { Routes = routes }));
        return (new SlackNotificationSink(router, client), client);
    }

    [Fact]
    public async Task ARoutedEvent_ReachesSlack()
    {
        var (sink, client) = Build(new NotificationRoute { Event = "team:create", Channel = "#teams" });

        await sink.DispatchAsync(Entry());

        await client.Received(1).PostAsync("#teams", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnroutedEvent_NeverReachesSlack()
    {
        var (sink, client) = Build(new NotificationRoute { Event = "team:create", Channel = "#teams" });

        await sink.DispatchAsync(Entry(action: "delete"));

        await client.DidNotReceive().PostAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneEventRoutedTwice_PostsTwice()
    {
        var (sink, client) = Build(
            new NotificationRoute { Event = "team:create", Channel = "#teams" },
            new NotificationRoute { Event = "team:*", Channel = "#audit" });

        await sink.DispatchAsync(Entry());

        await client.Received(1).PostAsync("#teams", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await client.Received(1).PostAsync("#audit", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- a notification must never become the caller's problem ---

    /// <summary>
    /// A rejected post is reported and dropped. The team was still created; a Slack outage cannot be
    /// allowed to suggest otherwise.
    /// </summary>
    [Fact]
    public async Task ARejectedPost_DoesNotFailTheOperation()
    {
        var (sink, client) = Build(new NotificationRoute { Event = "team:create", Channel = "#teams" });
        client.PostAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SlackPostResult.Failed("channel_not_found"));

        await sink.DispatchAsync(Entry());
    }

    /// <summary>
    /// <see cref="ISlackClient"/> promises not to throw, but the sink does not rely on the promise —
    /// a substitute, a decorator or a future transport might break it.
    /// </summary>
    [Fact]
    public async Task ATransportThatThrows_DoesNotFailTheOperation()
    {
        var (sink, client) = Build(new NotificationRoute { Event = "team:create", Channel = "#teams" });
        client.PostAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<SlackPostResult>>(_ => throw new HttpRequestException("network down"));

        await sink.DispatchAsync(Entry());
    }

    /// <summary>One bad channel does not silence the others.</summary>
    [Fact]
    public async Task AFailedPost_DoesNotStopTheRemainingRoutes()
    {
        var (sink, client) = Build(
            new NotificationRoute { Event = "team:create", Channel = "#broken" },
            new NotificationRoute { Event = "team:create", Channel = "#working" });
        client.PostAsync("#broken", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<SlackPostResult>>(_ => throw new HttpRequestException("network down"));

        await sink.DispatchAsync(Entry());

        await client.Received(1).PostAsync("#working", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <see cref="SlackNotificationSink.Log"/> runs on the thread of the operation being audited, so it
    /// queues and returns. Nothing is draining the queue here, and the absence of a post is the proof
    /// that no HTTPS round trip happened inline.
    /// </summary>
    [Fact]
    public async Task Log_DoesNotPostOnTheCallersThread()
    {
        var (sink, client) = Build(new NotificationRoute { Event = "team:create", Channel = "#teams" });

        sink.Log(Entry());

        await client.DidNotReceive().PostAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>And what it queued is delivered once the pump runs.</summary>
    [Fact]
    public async Task WhatLogQueued_IsDeliveredWhenThePumpRuns()
    {
        var (sink, client) = Build(new NotificationRoute { Event = "team:create", Channel = "#teams" });

        sink.Log(Entry());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sink.StartAsync(cts.Token);
        while (client.ReceivedCalls().Count() == 0 && !cts.IsCancellationRequested) await Task.Delay(10, CancellationToken.None);
        await sink.StopAsync(CancellationToken.None);

        await client.Received(1).PostAsync("#teams", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANullEntry_IsIgnored()
    {
        var (sink, client) = Build(new NotificationRoute { Event = "*", Channel = "#teams" });

        sink.Log(null);
        await sink.DispatchAsync(null);

        await client.DidNotReceive().PostAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The sink is a write-only sink; audit reads are served by the audit store.</summary>
    [Fact]
    public async Task QueryAsync_ReturnsNothing()
    {
        var (sink, _) = Build(new NotificationRoute { Event = "*", Channel = "#teams" });

        var result = await sink.QueryAsync(new AuditQuery());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }
}
