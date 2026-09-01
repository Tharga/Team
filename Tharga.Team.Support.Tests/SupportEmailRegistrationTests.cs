using System.Reflection;
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
    private static void SetEverything(SupportCaseOptions o)
    {
        o.Email.Imap.Host = "imap.example.com";
        o.Email.Imap.Port = 993;
        o.Email.Imap.UseSsl = false;
        o.Email.Imap.UserName = "reader";
        o.Email.Imap.Password = "reader-secret";
        o.Email.Smtp.Host = "smtp.example.com";
        o.Email.Smtp.Port = 587;
        o.Email.Smtp.UseSsl = false;
        o.Email.Smtp.UserName = "sender";
        o.Email.Smtp.Password = "sender-secret";
        o.Email.FromAddress = "support@fortdocs.se";
        o.Email.FromName = "FortDocs Support";
        o.Email.Folder = "Support";
        o.Email.PollInterval = TimeSpan.FromSeconds(30);
        o.Email.Timeout = TimeSpan.FromSeconds(45);
        o.Email.Recipients = ["fortdocs.se"];
    }

    [Fact]
    public void MailSettings_AreProjectedOntoTheTransportOptions()
    {
        var options = Configure(SetEverything);

        Assert.Equal("imap.example.com", options.Imap.Host);
        Assert.Equal("reader", options.Imap.UserName);
        Assert.Equal("reader-secret", options.Imap.Password);
        Assert.Equal(993, options.Imap.Port);
        Assert.Equal("smtp.example.com", options.Smtp.Host);
        Assert.Equal("sender", options.Smtp.UserName);
        Assert.False(options.Smtp.UseSsl);
        Assert.Equal("support@fortdocs.se", options.FromAddress);
        Assert.Equal("FortDocs Support", options.FromName);
        Assert.Equal("Support", options.Folder);
        Assert.Equal(TimeSpan.FromSeconds(30), options.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Timeout);
        Assert.Equal(["fortdocs.se"], options.Recipients);
    }

    /// <summary>
    /// Every settable mail option reaches the transport — including one added after this was written.
    /// </summary>
    /// <remarks>
    /// <b>A hand-written projection is how options quietly stop working</b>, and it has shipped twice in this
    /// repository already (Tharga/Team#177): the copy is written against the properties that exist that day,
    /// a property is added later, and it is accepted from the host and silently discarded. The registration
    /// now copies by reflection, and this asserts the result rather than the mechanism — so it holds even if
    /// someone replaces the copy with assignments again.
    /// <para>
    /// It works by leaving nothing at its default: a property added to <see cref="MailOptions"/> or
    /// <see cref="MailServerOptions"/> and not set in <c>SetEverything</c> fails here, which is the prompt to
    /// set it and confirm it survives the projection.
    /// </para>
    /// </remarks>
    [Fact]
    public void EverySettableMailOption_IsForwarded()
    {
        var projected = Configure(SetEverything);

        var defaults = new MailOptions();

        AssertNothingLeftAtDefault(projected, defaults);
        AssertNothingLeftAtDefault(projected.Imap, defaults.Imap);
        AssertNothingLeftAtDefault(projected.Smtp, defaults.Smtp);
    }

    private static void AssertNothingLeftAtDefault<T>(T projected, T defaults) where T : class
    {
        var settable = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.CanRead && x.SetMethod?.IsPublic == true)
            .ToArray();

        Assert.NotEmpty(settable);

        foreach (var property in settable)
        {
            var carried = property.GetValue(projected);
            var untouched = property.GetValue(defaults);

            Assert.False(Equals(carried, untouched),
                $"{typeof(T).Name}.{property.Name} still holds its default after projection. Either the " +
                "registration does not forward it, or the test does not set it — both are defects.");
        }
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
