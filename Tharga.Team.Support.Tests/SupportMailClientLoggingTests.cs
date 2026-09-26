using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A failed send is logged without the recipient's address: an address is personal data, and logs travel further
/// than mail does. The same finding CodeQL raised on PR #305 for the withheld-mail logs.
/// </summary>
public class SupportMailClientLoggingTests
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

        var result = await new SupportMailClient(Options.Create(options), logger).SendAsync(new OutboundMail(Customer, "Re: printer", "body"));

        Assert.False(result.Success);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.DoesNotContain(Customer, entry.Message);
        Assert.DoesNotContain("kommun.se", entry.Message);
    }

    private sealed class CapturingLogger : ILogger<SupportMailClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
