using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Email.Mailbox;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The support mailboxes on a host's health endpoint: one result per configured mailbox by name, the IMAP folder opened
/// and the SMTP server signed in to without reading or sending anything, a failure reported with its reason, and
/// nothing ever thrown.
/// </summary>
public class SupportMailboxHealthTests
{
    private const string FromAddress = "support@example.net";

    private static async Task<IReadOnlyList<SupportMailboxHealth>> CheckAsync(Action<SupportCaseOptions> configure, CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();
        services.AddThargaSupportCases(configure);
        await using var provider = services.BuildServiceProvider();

        return await provider.GetRequiredService<ISupportMailboxHealth>().CheckAsync(cancellationToken);
    }

    [Fact]
    public async Task WithNoMailboxConfigured_NothingIsReported()
    {
        var health = await CheckAsync(_ => { });

        Assert.Empty(health);
    }

    [Fact]
    public async Task AMailboxThatCannotBeRead_IsReported_WithTheReason()
    {
        var health = await CheckAsync(o =>
        {
            o.Email.Imap.Host = "127.0.0.1";
            o.Email.Imap.Port = 1;
            o.Email.Timeout = TimeSpan.FromMilliseconds(500);
        });

        var primary = Assert.Single(health);
        Assert.Equal(SupportCaseOptions.PrimaryMailboxName, primary.Name);
        Assert.True(primary.Reading.IsConfigured);
        Assert.False(primary.Reading.IsReachable);
        Assert.False(string.IsNullOrWhiteSpace(primary.Reading.Error));
        Assert.False(primary.Sending.IsConfigured);
        Assert.False(primary.IsHealthy);
    }

    [Fact]
    public async Task AnSmtpServerThatCannotBeReached_IsReported_WithTheReason()
    {
        var health = await CheckAsync(o =>
        {
            o.Email.Smtp.Host = "127.0.0.1";
            o.Email.Smtp.Port = 1;
            o.Email.FromAddress = FromAddress;
            o.Email.Timeout = TimeSpan.FromMilliseconds(500);
        });

        var primary = Assert.Single(health);
        Assert.False(primary.Reading.IsConfigured);
        Assert.True(primary.Sending.IsConfigured);
        Assert.False(primary.Sending.IsReachable);
        Assert.False(string.IsNullOrWhiteSpace(primary.Sending.Error));
        Assert.False(primary.IsHealthy);
    }

    [Fact]
    public async Task AReachableSmtpServer_IsHealthy_AndNothingIsSentToIt()
    {
        using var server = new FakeSmtpServer();

        var health = await CheckAsync(o =>
        {
            o.Email.Smtp.Host = "127.0.0.1";
            o.Email.Smtp.Port = server.Port;
            o.Email.Smtp.UseSsl = false;
            o.Email.FromAddress = FromAddress;
            o.Email.Timeout = TimeSpan.FromSeconds(5);
        });

        var primary = Assert.Single(health);
        Assert.Null(primary.Sending.Error);
        Assert.True(primary.Sending.IsConfigured);
        Assert.True(primary.Sending.IsReachable);
        Assert.True(primary.IsHealthy);
        Assert.DoesNotContain(server.Commands, x => x.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase) || x.StartsWith("DATA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EveryConfiguredMailbox_IsReportedByName_ThePrimaryFirst_AndOneThatFailsLeavesTheOthersAlone()
    {
        using var server = new FakeSmtpServer();

        var health = await CheckAsync(o =>
        {
            o.Email.Imap.Host = "127.0.0.1";
            o.Email.Imap.Port = 1;
            o.Email.Timeout = TimeSpan.FromMilliseconds(500);
            o.AddMailbox("billing", m =>
            {
                m.Smtp.Host = "127.0.0.1";
                m.Smtp.Port = server.Port;
                m.Smtp.UseSsl = false;
                m.FromAddress = "billing@example.net";
                m.Timeout = TimeSpan.FromSeconds(5);
            });
            o.AddMailbox("unused", _ => { });
        });

        Assert.Equal([SupportCaseOptions.PrimaryMailboxName, "billing"], health.Select(x => x.Name));
        Assert.False(health[0].IsHealthy);
        Assert.True(health[1].IsHealthy);
    }

    [Fact]
    public async Task ACancelledCheck_ReportsTheFailure_InsteadOfThrowing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var health = await CheckAsync(o =>
        {
            o.Email.Imap.Host = "127.0.0.1";
            o.Email.Imap.Port = 1;
        }, cancelled.Token);

        Assert.False(Assert.Single(health).IsHealthy);
    }

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, false, true, true, true)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, false, true, true, false)]
    public void AMailbox_IsHealthy_WhenEverythingConfiguredWorks(bool reads, bool canRead, bool sends, bool canSend, bool expected)
    {
        var health = new SupportMailboxHealth("x", new MailboxHealth(reads, canRead), new MailboxHealth(sends, canSend));

        Assert.Equal(expected, health.IsHealthy);
    }

    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<string> _commands = [];

        public FakeSmtpServer()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string[] Commands
        {
            get { lock (_commands) return [.. _commands]; }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await ServeAsync(client);
                }
            }
            catch (Exception)
            {
                return;
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            await using var writer = new StreamWriter(stream) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 localhost ESMTP ready");

            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                lock (_commands) _commands.Add(line);

                if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }

                await writer.WriteLineAsync("250 localhost");
            }
        }
    }
}
