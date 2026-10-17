using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// A failed send is logged without the recipient's address: an address is personal data, and logs travel further
/// than mail does. The same finding CodeQL raised on PR #305 for the withheld-mail logs.
/// </summary>
public class MailClientLoggingTests
{
    private const string Customer = "kund@kommun.se";

    [Fact]
    public async Task AFailedSend_IsLoggedWithoutTheRecipientAddress()
    {
        var options = new MailOptions { FromAddress = "support@example.com", Timeout = TimeSpan.FromSeconds(5) };
        options.Smtp.Host = "127.0.0.1";
        options.Smtp.Port = 1;
        options.Smtp.UseSsl = false;
        var logger = new CapturingLogger();

        var result = await new MailClient(Options.Create(options), logger).SendAsync(new OutboundMail(Customer, "Re: printer", "body"));

        Assert.False(result.Success);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.DoesNotContain(Customer, entry.Message);
        Assert.DoesNotContain("kommun.se", entry.Message);
    }

    [Fact]
    public async Task AFailedRead_SaysWhy_KeepsThePosition_AndNeverNamesThePassword()
    {
        const string password = "app-password-123";
        var options = new MailOptions { Timeout = TimeSpan.FromSeconds(5) };
        options.Imap.Host = "127.0.0.1";
        options.Imap.Port = 1;
        options.Imap.UseSsl = false;
        options.Imap.UserName = "support@example.com";
        options.Imap.Password = password;
        var logger = new CapturingLogger();
        var position = new MailFetchPosition(7, 42);

        var result = await new MailClient(Options.Create(options), logger).FetchAsync(position, TestContext.Current.CancellationToken);

        Assert.Equal(position, result.Position);
        Assert.Empty(result.Mails);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.DoesNotContain(password, result.Error);
        Assert.All(logger.Entries, x => Assert.DoesNotContain(password, x.Message));
    }

    [Fact]
    public void AnEmptyFetch_CarriesNoError()
    {
        Assert.Null(MailFetchResult.Empty(MailFetchPosition.Start).Error);
    }

    private sealed class CapturingLogger : ILogger<MailClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
