using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>The background sender: nothing is claimed, and that is said once, until a sending address and server are configured.</summary>
public class UserMailSenderTests
{
    private sealed class CountingLogger : ILogger<UserMailSender>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static (UserMailSender Sender, UserMailFixture Fixture, CountingLogger Logger) Sender(Action<UserMailOptions> configure)
    {
        var fixture = new UserMailFixture(configure);
        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.Dispatcher());
        var logger = new CountingLogger();

        return (new UserMailSender(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Options.Create(fixture.Options), logger), fixture, logger);
    }

    [Fact]
    public async Task WithoutASendingAddress_NothingIsSent_AndThatIsLoggedOnce()
    {
        var (sender, fixture, logger) = Sender(o => o.Mail.FromAddress = null);
        await fixture.QueueAsync();

        Assert.Equal(0, await sender.RunOnceAsync(default));
        Assert.Equal(0, await sender.RunOnceAsync(default));

        Assert.Empty(fixture.Mail.Sent);
        Assert.Equal(UserMailStatus.Queued, fixture.Outbox.Single.Status);
        Assert.Equal(0, fixture.Outbox.Single.Attempts);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("Mail.FromAddress", warning.Message);
    }

    [Fact]
    public async Task WithoutAnSmtpHost_NothingIsSent()
    {
        var (sender, fixture, logger) = Sender(o => o.Mail.Smtp.Host = null);
        await fixture.QueueAsync();

        Assert.Equal(0, await sender.RunOnceAsync(default));
        Assert.Empty(fixture.Mail.Sent);
        Assert.Contains("Mail.Smtp.Host", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public async Task Configured_APass_SendsWhatIsDue_UnderThisInstancesName()
    {
        var (sender, fixture, logger) = Sender(null);
        await fixture.QueueAsync();
        await fixture.QueueAsync();

        Assert.Equal(2, await sender.RunOnceAsync(default));
        Assert.Equal(2, fixture.Mail.Sent.Count);
        Assert.Empty(logger.Entries);
        Assert.Contains(Environment.MachineName, sender.Claimant);
    }

    [Fact]
    public void EveryInstance_ClaimsUnderANameOfItsOwn()
    {
        Assert.NotEqual(Sender(null).Sender.Claimant, Sender(null).Sender.Claimant);
    }
}
