using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Connecting more than one mailbox to support: names that stay stable keys in stored data, and every mailbox
/// reaching the registration with its own settings.
/// </summary>
public class SupportMailboxOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMailboxWithNoName_IsRefused(string name)
    {
        var options = new SupportCaseOptions();

        Assert.Throws<ArgumentException>(() => options.AddMailbox(name, _ => { }));
    }

    [Theory]
    [InlineData("hosted")]
    [InlineData("HOSTED")]
    [InlineData(" hosted ")]
    public void ANameAlreadyUsed_IsRefused_WhateverItsCaseOrPadding(string again)
    {
        var options = new SupportCaseOptions().AddMailbox("hosted", _ => { });

        var exception = Assert.Throws<ArgumentException>(() => options.AddMailbox(again, _ => { }));
        Assert.Contains("hosted", exception.Message);
    }

    /// <summary>The primary's name is what its existing cases and read position are stored under, so nothing else may take it.</summary>
    [Theory]
    [InlineData(SupportCaseOptions.PrimaryMailboxName)]
    [InlineData("Primary")]
    public void ThePrimarysName_IsRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => new SupportCaseOptions().AddMailbox(name, _ => { }));
    }

    [Fact]
    public void AMailboxWithNothingToConfigureIt_IsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new SupportCaseOptions().AddMailbox("hosted", null));
    }

    [Fact]
    public void AddedMailboxes_ReachTheRegistration_AfterThePrimary_WithTheirOwnSettings()
    {
        var mailboxes = Resolve(o =>
        {
            o.Email.FromAddress = "support@example.com";
            o.AddMailbox("hosted", m =>
            {
                m.Imap.Host = "imap.example.net";
                m.Smtp.Host = "smtp.example.net";
                m.Smtp.UserName = "help@example.net";
                m.FromAddress = "help@example.net";
                m.FromName = "Help";
                m.PerCaseReplyTo = true;
                m.PollInterval = TimeSpan.FromMinutes(5);
            }).AddMailbox("archive", m => m.Imap.Host = "imap.example.org");
        });

        Assert.Equal([SupportCaseOptions.PrimaryMailboxName, "hosted", "archive"], mailboxes.All.Select(x => x.Name));
        Assert.Equal("support@example.com", mailboxes.Primary.Options.FromAddress);

        var hosted = mailboxes.Find("hosted");
        Assert.Equal(("imap.example.net", "smtp.example.net", "help@example.net", "help@example.net", "Help", true, TimeSpan.FromMinutes(5)),
            (hosted.Options.Imap.Host, hosted.Options.Smtp.Host, hosted.Options.Smtp.UserName, hosted.Options.FromAddress, hosted.Options.FromName, hosted.Options.PerCaseReplyTo, hosted.Options.PollInterval));
    }

    /// <summary>A binding stored before mailboxes were named means the primary; one naming a mailbox no longer configured finds none.</summary>
    [Theory]
    [InlineData(null, SupportCaseOptions.PrimaryMailboxName)]
    [InlineData("", SupportCaseOptions.PrimaryMailboxName)]
    [InlineData(SupportCaseOptions.PrimaryMailboxName, SupportCaseOptions.PrimaryMailboxName)]
    [InlineData("Hosted", "hosted")]
    [InlineData("removed", null)]
    public void AMailboxIsFoundByTheNameABindingCarries(string stored, string expected)
    {
        var mailboxes = Resolve(o => o.AddMailbox("hosted", _ => { }));

        Assert.Equal(expected, mailboxes.Find(stored)?.Name);
    }

    [Fact]
    public void AnAddedMailboxWhoseFilterRejectsItsOwnAddress_IsRefusedAtRegistration_ByName()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(o => o.AddMailbox("hosted", m =>
        {
            m.FromAddress = "help@example.net";
            m.Recipients = ["example.com"];
        })));

        Assert.Contains("hosted", exception.Message);
        Assert.Contains("help@example.net", exception.Message);
    }

    /// <summary>A host may keep its mail on hosted mailboxes alone and leave the primary unset.</summary>
    [Fact]
    public void WithOnlyAnAddedMailbox_TheChannelAndThePollerAreRegistered()
    {
        var services = new ServiceCollection();
        services.AddThargaSupportCases(o => o.AddMailbox("hosted", m =>
        {
            m.Imap.Host = "imap.example.net";
            m.Smtp.Host = "smtp.example.net";
            m.FromAddress = "help@example.net";
        }));

        Assert.Single(services, x => x.ServiceType == typeof(ISupportChannel) && x.ImplementationType == typeof(EmailSupportChannel));
        Assert.Single(services, x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(SupportMailPoller));
        Assert.Contains(services, x => x.ServiceType == typeof(ISupportMailClients));
    }

    [Fact]
    public void AnAddedMailboxThatOnlySends_StartsNoPoller()
    {
        var services = new ServiceCollection();
        services.AddThargaSupportCases(o => o.AddMailbox("hosted", m =>
        {
            m.Smtp.Host = "smtp.example.net";
            m.FromAddress = "help@example.net";
        }));

        Assert.Single(services, x => x.ServiceType == typeof(ISupportChannel));
        Assert.DoesNotContain(services, x => x.ImplementationType == typeof(SupportMailPoller));
    }

    /// <summary>The toolkit reads no configuration itself; a host binds a section onto the mailbox it adds.</summary>
    [Fact]
    public void AMailboxBoundFromConfiguration_CarriesItsServersAndAddresses()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Support:Mailboxes:hosted:Imap:Host"] = "imap.example.net",
            ["Support:Mailboxes:hosted:Imap:UserName"] = "help@example.net",
            ["Support:Mailboxes:hosted:Imap:Password"] = "app-password",
            ["Support:Mailboxes:hosted:Smtp:Host"] = "smtp.example.net",
            ["Support:Mailboxes:hosted:Smtp:Port"] = "587",
            ["Support:Mailboxes:hosted:FromAddress"] = "help@example.net",
            ["Support:Mailboxes:hosted:PerCaseReplyTo"] = "true",
            ["Support:Mailboxes:hosted:PollInterval"] = "00:02:00",
            ["Support:Mailboxes:hosted:Recipients:0"] = "example.net"
        }).Build();

        var mailboxes = Resolve(o =>
        {
            foreach (var section in configuration.GetSection("Support:Mailboxes").GetChildren())
                o.AddMailbox(section.Key, m => section.Bind(m));
        });

        var hosted = mailboxes.Find("hosted").Options;
        Assert.Equal(("imap.example.net", "help@example.net", "app-password", "smtp.example.net", 587), (hosted.Imap.Host, hosted.Imap.UserName, hosted.Imap.Password, hosted.Smtp.Host, hosted.Smtp.Port));
        Assert.Equal(("help@example.net", true, TimeSpan.FromMinutes(2)), (hosted.FromAddress, hosted.PerCaseReplyTo, hosted.PollInterval));
        Assert.Equal(["example.net"], hosted.Recipients);
    }

    /// <summary>A file an answer may carry must be one every sending mailbox accepts, since the answer goes out through the case's.</summary>
    [Fact]
    public void AnAnswersFileLimits_AreTheStrictestOfTheMailboxesThatSend()
    {
        var mailboxes = Resolve(o =>
        {
            o.Email.Smtp.Host = "smtp.example.com";
            o.AddMailbox("hosted", m =>
            {
                m.Smtp.Host = "smtp.example.net";
                m.MaxAttachmentBytes = 10;
                m.MaxAttachmentCount = 30;
            }).AddMailbox("reader", m =>
            {
                m.Imap.Host = "imap.example.org";
                m.MaxAttachmentBytes = 1;
                m.MaxAttachmentTotalBytes = 1;
                m.MaxAttachmentCount = 1;
            });
        });

        var limits = mailboxes.AnswerLimits();

        Assert.Equal((10L, SupportAttachmentLimits.Default.MaxTotalBytes, SupportAttachmentLimits.Default.MaxCount), (limits.MaxFileBytes, limits.MaxTotalBytes, limits.MaxCount));
    }

    [Fact]
    public void WithNoMailboxThatSends_AnAnswersFileLimits_AreThePrimarys()
    {
        var mailboxes = Resolve(o =>
        {
            o.Email.MaxAttachmentCount = 4;
            o.AddMailbox("reader", m => m.MaxAttachmentCount = 2);
        });

        Assert.Equal(4, mailboxes.AnswerLimits().MaxCount);
    }

    private static SupportMailboxes Resolve(Action<SupportCaseOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddThargaSupportCases(configure);

        return services.BuildServiceProvider().GetRequiredService<SupportMailboxes>();
    }
}
