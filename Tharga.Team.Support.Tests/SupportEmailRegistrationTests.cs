using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The mail settings reach the transport's own options type, and a configuration that would silently discard
/// every reply is refused at startup.
/// </summary>
public class SupportEmailRegistrationTests
{
    [Fact]
    public void MailSettings_AreProjectedOntoTheTransportOptions()
    {
        var options = Configure(o =>
        {
            o.Email.Imap.Host = "imap.example.com";
            o.Email.Imap.UserName = "reader";
            o.Email.Imap.Password = "secret";
            o.Email.Imap.Port = 993;
            o.Email.Smtp.Host = "smtp.example.com";
            o.Email.Smtp.UseSsl = false;
            o.Email.FromAddress = "support@fortdocs.se";
            o.Email.FromName = "FortDocs Support";
            o.Email.Folder = "Support";
            o.Email.PollInterval = TimeSpan.FromSeconds(30);
            o.Email.Recipients = ["fortdocs.se"];
        });

        Assert.Equal("imap.example.com", options.Imap.Host);
        Assert.Equal("reader", options.Imap.UserName);
        Assert.Equal("secret", options.Imap.Password);
        Assert.Equal(993, options.Imap.Port);
        Assert.Equal("smtp.example.com", options.Smtp.Host);
        Assert.False(options.Smtp.UseSsl);
        Assert.Equal("support@fortdocs.se", options.FromAddress);
        Assert.Equal("FortDocs Support", options.FromName);
        Assert.Equal("Support", options.Folder);
        Assert.Equal(TimeSpan.FromSeconds(30), options.PollInterval);
        Assert.Equal(["fortdocs.se"], options.Recipients);
    }

    /// <summary>
    /// Configuring nothing must leave email entirely off, exactly as an unset Slack channel leaves cases on
    /// the site.
    /// </summary>
    [Fact]
    public void ConfiguringNothing_LeavesTheTransportUnconfigured()
    {
        var options = Configure(_ => { });

        Assert.Null(options.Imap.Host);
        Assert.Null(options.Smtp.Host);
        Assert.Empty(options.Recipients);
        Assert.True(new RecipientFilter(options.Recipients).AcceptsEverything);
    }

    [Fact]
    public void AFilterThatWouldRejectTheSendingAddress_IsRefusedAtRegistration()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Configure(o =>
        {
            o.Email.FromAddress = "support@fortdocs.se";
            o.Email.Recipients = ["eplicta.se"];
        }));

        Assert.Contains("support@fortdocs.se", exception.Message);
        Assert.Contains("eplicta.se", exception.Message);
    }

    [Fact]
    public void AFilterCoveringTheSendingAddress_IsAccepted()
    {
        var options = Configure(o =>
        {
            o.Email.FromAddress = "support+ignored@fortdocs.se";
            o.Email.Recipients = ["fortdocs.se"];
        });

        Assert.Equal(["fortdocs.se"], options.Recipients);
    }

    /// <summary>
    /// No filter is the single-site case, where every address is accepted and there is nothing to disagree
    /// with.
    /// </summary>
    [Fact]
    public void ASendingAddressWithNoFilter_IsAccepted()
    {
        var options = Configure(o => o.Email.FromAddress = "support@fortdocs.se");

        Assert.Equal("support@fortdocs.se", options.FromAddress);
    }

    private static MailOptions Configure(Action<SupportCaseOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddThargaSupportCases(configure);

        return services.BuildServiceProvider().GetRequiredService<IOptions<MailOptions>>().Value;
    }
}
