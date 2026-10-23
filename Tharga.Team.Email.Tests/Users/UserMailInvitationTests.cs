using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Email.Invitations;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>With mail to users registered, an invitation goes through its outbox to the invited address alone, in the account category, is recorded and shows in the history of whoever holds the address, and the inviter still learns at once whether it went.</summary>
public class UserMailInvitationTests
{
    private const string Invited = "Bea.Berg@Example.org";
    private const string Link = "https://app.example.com/invite/team-1/abc";

    private readonly UserMailFixture _fixture = new();

    private UserMailInvitationSender Sender(ITextResolver texts = null)
        => new(new UserMailQueue(_fixture.Categories, _fixture.Outbox, _fixture.Wrapped, _fixture.Time, _fixture.Dispatcher(texts)), new UserMailText(texts), _fixture.Wrapped);

    private static TeamInviteMail Invite(string name = "Bea Berg") => new()
    {
        RecipientEmail = Invited,
        RecipientName = name,
        InviteLink = Link,
        TeamKey = "team-1",
        TeamName = "Alpha"
    };

    [Fact]
    public async Task AnInvitation_IsSentAtOnce_ToTheAddressAlone_InTheAccountCategory_AndRecorded()
    {
        await Sender().SendInviteAsync(Invite());

        var item = _fixture.Outbox.Single;
        Assert.Null(item.Mail.RecipientUserKey);
        Assert.Equal("bea.berg@example.org", item.RecipientAddress);
        Assert.Equal(UserMailCategories.AccountKey, item.Mail.Category);
        Assert.Equal(UserMailKind.Invitation, item.Mail.Kind);
        Assert.Equal(UserMailStatus.Sent, item.Status);
        Assert.Equal(1, item.Attempts);
        Assert.NotNull(item.MessageId);

        var sent = Assert.Single(_fixture.Mail.Sent);
        var to = MailboxAddress.Parse(sent.To);
        Assert.Equal(("Bea Berg", "bea.berg@example.org"), (to.Name, to.Address));
        Assert.Equal("You've been invited to join Alpha", sent.Subject);
        Assert.Contains("Hi Bea Berg,", sent.Body);
        Assert.Contains("\"Alpha\"", sent.Body);
        Assert.Contains(Link, sent.Body);
        Assert.Null(sent.UnsubscribeUrl);
        Assert.Equal(_fixture.Options.ReplyTo, sent.ReplyTo);
    }

    [Fact]
    public async Task AnInvitationWithNoName_GreetsTheAddress()
    {
        await Sender().SendInviteAsync(Invite(name: " "));

        Assert.Contains($"Hi {Invited},", Assert.Single(_fixture.Mail.Sent).Body);
    }

    [Fact]
    public async Task TheInvitationsText_IsTheHosts()
    {
        var texts = new DictionaryTextResolver(new Dictionary<string, string>
        {
            [UserMailTexts.InvitationSubjectKey] = "Du är inbjuden till {0}",
            [UserMailTexts.InvitationBodyKey] = "Hej {0}! Gå med i {1} här: {2}"
        });

        await Sender(texts).SendInviteAsync(Invite());

        var sent = Assert.Single(_fixture.Mail.Sent);
        Assert.Equal("Du är inbjuden till Alpha", sent.Subject);
        Assert.Equal($"Hej Bea Berg! Gå med i Alpha här: {Link}", sent.Body);
    }

    [Fact]
    public async Task AnInvitationTheServerRefuses_IsReportedToTheInviter_AndNeverTriedAgain()
    {
        _fixture.Mail.Respond = _ => MailSendResult.Failed("421 Service not available");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => Sender().SendInviteAsync(Invite()));

        Assert.Contains("421 Service not available", refusal.Message);
        Assert.Equal(UserMailStatus.Failed, _fixture.Outbox.Single.Status);
        Assert.Equal("421 Service not available", _fixture.Outbox.Single.Reason);

        _fixture.Time.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, await _fixture.Dispatcher().RunAsync("background", TestContext.Current.CancellationToken));
        Assert.Single(_fixture.Mail.Sent);
    }

    [Fact]
    public async Task AnInvitationThePolicyWithholds_IsReportedWithWhy()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Staging");
        _fixture.Policy = new OutboundMailPolicy(Options.Create(new OutboundMailPolicyOptions()), environment);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => Sender().SendInviteAsync(Invite()));

        Assert.Contains(UserMailDispatcher.WithheldReason, refusal.Message);
        Assert.Equal(UserMailStatus.Failed, _fixture.Outbox.Single.Status);
        Assert.Empty(_fixture.Mail.Sent);
    }

    [Fact]
    public async Task WithMailToUsersNotConfigured_TheInvitationIsRefused_AndNothingIsRecorded()
    {
        _fixture.Options.Mail.FromAddress = null;

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => Sender().SendInviteAsync(Invite()));

        Assert.Contains("Mail.FromAddress", refusal.Message);
        Assert.Empty(_fixture.Outbox.Items);
    }

    [Fact]
    public async Task AnAddressThatCannotBeMailed_IsRefused_AndNothingIsSent_OnlyTheRefusalIsRecorded()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sender().SendInviteAsync(Invite() with { RecipientEmail = "not an address" }));

        Assert.True(Assert.Single(_fixture.Outbox.Items).Refused);
        Assert.Empty(_fixture.Mail.Sent);
    }

    [Fact]
    public async Task AnInvitationBeingSent_IsNotClaimedByTheBackgroundSender()
    {
        var mail = _fixture.Queue.Accept(new UserMail { RecipientAddress = Invited, Category = UserMailCategories.AccountKey, Subject = "Join", Body = "Join us.", Kind = UserMailKind.Invitation });
        await _fixture.Outbox.AddClaimedAsync(mail, UserMailFixture.Start, "inviter", TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, await _fixture.Dispatcher().RunAsync("background", TestContext.Current.CancellationToken));
        Assert.Empty(_fixture.Mail.Sent);
    }

    [Fact]
    public async Task OnceTheInvitedPersonHasAUserRecordWithThatAddress_TheInvitationIsInTheirHistory()
    {
        await Sender().SendInviteAsync(Invite());
        _fixture.Recipients.Add(new UserMailRecipient { Key = "user-2", EMail = " BEA.berg@example.org ", Name = "Bea Berg" });
        _fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await _fixture.Queue.QueueAsync(new UserMail { RecipientUserKey = "user-2", Category = UserMailFixture.News, Subject = "Welcome", Body = "Welcome." });

        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(_ =>
        {
            var user = Substitute.For<IUser>();
            user.Key.Returns("user-2");
            return Task.FromResult(user);
        });
        var history = new UserMailHistoryService(_fixture.Queue, _fixture.Recipients, new TeamAuthorizer(new NobodyElse()), users);

        var page = await history.GetMineAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([UserMailKind.Mail, UserMailKind.Invitation], page.Items.Select(x => x.Kind));
        Assert.Equal("You've been invited to join Alpha", page.Items[1].Subject);
        Assert.Equal(UserMailStatus.Sent, page.Items[1].Status);
        Assert.Equal("bea.berg@example.org", page.Items[1].Address);
    }

    [Fact]
    public async Task AnInvitationToAnotherAddress_IsNotInAUsersHistory()
    {
        await Sender().SendInviteAsync(Invite());

        var page = await _fixture.Queue.ListForRecipientAsync(UserMailFixture.UserKey, UserMailFixture.Address, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData(UserMailCategories.AccountKey)]
    [InlineData(UserMailCategories.ServiceKey)]
    public async Task AMailToAnAddressAlone_IsAcceptedInARequiredCategory_AndSentThere_WithNoFooter(string category)
    {
        await _fixture.Queue.QueueAsync(new UserMail { RecipientAddress = " Cia@Example.org ", RecipientName = "Cia", Category = category, Subject = "Hello", Body = "Hello." });

        Assert.Equal(UserMailStatus.Sent, await _fixture.Dispatcher().SendNextAsync("me", default));

        var sent = Assert.Single(_fixture.Mail.Sent);
        Assert.Equal("cia@example.org", MailboxAddress.Parse(sent.To).Address);
        Assert.Null(sent.UnsubscribeUrl);
        Assert.Equal("Hello.", sent.Body);
        Assert.Equal("cia@example.org", _fixture.Outbox.Single.RecipientAddress);
    }

    [Fact]
    public async Task AMailToAnAddressAlone_IsRefusedInACategoryThatCanBeTurnedOff()
    {
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(new UserMail { RecipientAddress = "cia@example.org", Category = UserMailFixture.News, Subject = "Hello", Body = "Hello." }));

        Assert.Contains(UserMailFixture.News, refusal.Message);
        Assert.True(Assert.Single(_fixture.Outbox.Items).Refused);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("cia")]
    [InlineData("Cia <cia@example.org>")]
    public async Task AMailNamingNeitherAUserNorAnAddressItCanGoTo_IsRefused(string address)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(new UserMail { RecipientAddress = address, Category = UserMailCategories.AccountKey, Subject = "Hello", Body = "Hello." }));
    }

    [Fact]
    public void AUserKey_WinsOverAnAddress()
    {
        var accepted = _fixture.Queue.Accept(new UserMail { RecipientUserKey = UserMailFixture.UserKey, RecipientAddress = "cia@example.org", RecipientName = "Cia", Category = UserMailFixture.News, Subject = "Hello", Body = "Hello." });

        Assert.Equal(UserMailFixture.UserKey, accepted.RecipientUserKey);
        Assert.Null(accepted.RecipientAddress);
        Assert.Null(accepted.RecipientName);
    }

    [Fact]
    public void WithMailToUsers_InvitationsGoThroughItsOutbox()
    {
        Assert.IsType<UserMailInvitationSender>(Resolve(services => services.AddThargaUserMail()));
    }

    [Fact]
    public void ThePlainSmtpSender_IsReplaced_WhicheverIsRegisteredFirst()
    {
        var before = Registered(services =>
        {
            services.AddThargaEmailInvitations();
            services.AddThargaUserMail();
        });
        var after = Registered(services =>
        {
            services.AddThargaUserMail();
            services.AddThargaEmailInvitations();
        });

        Assert.All([before, after], x => Assert.Null(Assert.Single(x).ImplementationType));
        Assert.IsType<UserMailInvitationSender>(Resolve(services =>
        {
            services.AddThargaEmailInvitations();
            services.AddThargaUserMail();
        }));
    }

    [Fact]
    public void ASenderTheHostRegistered_IsKept_WhicheverIsRegisteredFirst()
    {
        Assert.IsType<HostSender>(Resolve(services =>
        {
            services.AddScoped<ITeamEmailSender, HostSender>();
            services.AddThargaUserMail();
        }));
        Assert.IsType<HostSender>(Resolve(services =>
        {
            services.AddThargaUserMail();
            services.AddScoped<ITeamEmailSender, HostSender>();
        }));
    }

    [Fact]
    public void WithoutMailToUsers_InvitationsUseThePlainSmtpSender()
    {
        Assert.IsType<InvitationMailSender>(Resolve(services => services.AddThargaEmailInvitations()));
    }

    private static ServiceDescriptor[] Registered(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);

        return [.. services.Where(x => x.ServiceType == typeof(ITeamEmailSender))];
    }

    private static ITeamEmailSender Resolve(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        register(services);
        services.AddSingleton<IUserMailOutbox>(new InMemoryUserMailOutbox());
        services.AddSingleton<IUserMailPreferenceStore>(new InMemoryUserMailPreferenceStore());
        services.AddSingleton<IUserMailRecipientDirectory>(new InMemoryUserMailRecipients());

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ITeamEmailSender>();
    }

    private sealed class HostSender : ITeamEmailSender
    {
        public Task SendInviteAsync(TeamInviteMail mail) => Task.CompletedTask;
    }

    private sealed class NobodyElse : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(new ClaimsPrincipal(new ClaimsIdentity()));
    }
}
