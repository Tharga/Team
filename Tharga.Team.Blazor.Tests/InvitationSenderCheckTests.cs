using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// SMTP settings with no sender would look exactly like a host that chose the copy-the-link fallback, so startup says which it is.
/// </summary>
public class InvitationSenderCheckTests
{
    private sealed class CapturingLogger : ILogger<InvitationSenderCheck>
    {
        public List<string> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => Entries.Add(formatter(state, exception));
    }

    private sealed class FakeSender : ITeamEmailSender
    {
        public Task SendInviteAsync(string recipientEmail, string recipientName, string inviteLink, string teamName) => Task.CompletedTask;
    }

    [Fact]
    public async Task WithSettingsAndNoSender_StartupSaysWhatToAdd()
    {
        var logger = new CapturingLogger();

        await new InvitationSenderCheck(new ServiceCollection().BuildServiceProvider(), logger).StartAsync(CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("AddThargaEmailInvitations", entry);
        Assert.Contains("Tharga.Team.Email", entry);
    }

    [Fact]
    public async Task WithASender_StartupIsQuiet()
    {
        var logger = new CapturingLogger();
        var services = new ServiceCollection();
        services.AddScoped<ITeamEmailSender, FakeSender>();

        await new InvitationSenderCheck(services.BuildServiceProvider(), logger).StartAsync(CancellationToken.None);

        Assert.Empty(logger.Entries);
    }
}
