using Tharga.Team.Email;
using Tharga.Team.Support.Cases;
using Xunit;

namespace Tharga.Team.Support.Tests;

public class SupportAnswerSendersTests
{
    private const string Main = "support@example.net";
    private const string Alias = "help@example.net";
    private const string Elsewhere = "info@example.org";
    private const string Staff = "anna@example.net";

    private static MailOptions Options(string from, string[] sendAs = null, string[] domains = null)
        => new() { FromAddress = from, FromName = "Example Support", SendAs = [.. (sendAs ?? []).Select(x => new MailSendAs { Address = x })], SendAsDomains = domains ?? [] };

    private static SupportMailboxes Boxes(MailOptions primary, params SupportMailbox[] more) => new(primary, more);

    [Fact]
    public void Allowed_ListsTheMailboxAddressAndItsSendAsAddresses_OnDifferentDomains()
    {
        var boxes = Boxes(Options(Main, [Alias, Elsewhere]));

        var allowed = SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, null);

        Assert.Equal([Main, Alias, Elsewhere], allowed.Select(x => x.Address));
        Assert.All(allowed, x => Assert.False(x.Personal));
    }

    [Fact]
    public void Allowed_IncludesEveryMailboxsAddresses_WithTheCasesMailboxFirst()
    {
        var other = new SupportMailbox("Other", Options("desk@example.org"));
        var boxes = Boxes(Options(Main), other);

        var allowed = SupportAnswerSenders.Allowed(boxes.All,other, null);

        Assert.Equal(["desk@example.org", Main], allowed.Select(x => x.Address));
        Assert.Same(other, allowed[0].Mailbox);
    }

    [Fact]
    public void Allowed_AddsTheStaffAddress_OnlyWhenItsDomainIsDeclared()
    {
        var declared = Boxes(Options(Main, domains: ["example.net"]));
        var undeclared = Boxes(Options(Main));

        Assert.Contains(SupportAnswerSenders.Allowed(declared.All, declared.Primary, new SupportStaffSender(Staff, "Anna")), x => x.Personal && x.Address == Staff && x.Name == "Anna");
        Assert.DoesNotContain(SupportAnswerSenders.Allowed(undeclared.All, undeclared.Primary, new SupportStaffSender(Staff, "Anna")), x => x.Personal);
    }

    [Fact]
    public void Allowed_AddsTheStaffAddress_WhenItIsAnExplicitSendAsAddress()
    {
        var boxes = Boxes(Options(Main, [Staff]));

        var allowed = SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, new SupportStaffSender(Staff, "Anna"));

        Assert.Single(allowed, x => x.Address == Staff);
    }

    [Fact]
    public void Allowed_NeverTurnsASupportAddressIntoAPersonalOne()
    {
        var boxes = Boxes(Options(Main, domains: ["example.net"]));

        var allowed = SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, new SupportStaffSender(Main, "Anna"));

        Assert.Single(allowed);
        Assert.False(allowed[0].Personal);
    }

    [Fact]
    public void Allowed_DomainMatchingIgnoresCaseAndALeadingAt()
    {
        var boxes = Boxes(Options(Main, domains: ["@Example.NET"]));

        Assert.Contains(SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, new SupportStaffSender("Anna@example.net", null)), x => x.Personal);
    }

    [Fact]
    public void Default_IsTheStaffAddress_WhenTheyMaySendAsIt()
    {
        var boxes = Boxes(Options(Main, [Alias], ["example.net"]));
        var allowed = SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, new SupportStaffSender(Staff, "Anna"));

        Assert.Equal(Staff, SupportAnswerSenders.Default(allowed, boxes.Primary, Alias).Address);
    }

    [Fact]
    public void Default_IsTheAddressTheCustomerWroteTo_WhenItIsASendAsAddress()
    {
        var boxes = Boxes(Options(Main, [Alias]));
        var allowed = SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, null);

        Assert.Equal(Alias, SupportAnswerSenders.Default(allowed, boxes.Primary, "HELP@example.net").Address);
    }

    [Fact]
    public void Default_IsTheMailboxAddress_WhenTheCustomerWroteToAnAddressItCannotSendAs()
    {
        var boxes = Boxes(Options(Main, [Alias]));
        var allowed = SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, null);

        Assert.Equal(Main, SupportAnswerSenders.Default(allowed, boxes.Primary, "unlisted@example.com").Address);
        Assert.Equal(Main, SupportAnswerSenders.Default(allowed, boxes.Primary, null).Address);
    }

    [Fact]
    public void Choose_AnAddressInNoMailboxsList_IsRefused()
    {
        var boxes = Boxes(Options(Main, [Alias]));
        var allowed = SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, null);
        var fallback = SupportAnswerSenders.Default(allowed, boxes.Primary, null);

        Assert.Null(SupportAnswerSenders.Choose(allowed, fallback, "stranger@example.com"));
    }

    [Fact]
    public void Choose_AnAllowedAddress_IsTheOneThatSends_AndNoChoiceIsTheDefault()
    {
        var boxes = Boxes(Options(Main, [Alias]));
        var allowed = SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, null);
        var fallback = SupportAnswerSenders.Default(allowed, boxes.Primary, null);

        Assert.Equal(Alias, SupportAnswerSenders.Choose(allowed, fallback, Alias).Address);
        Assert.Same(fallback, SupportAnswerSenders.Choose(allowed, fallback, " "));
    }

    [Fact]
    public void Formatted_CarriesTheNameWhenThereIsOne()
    {
        var boxes = Boxes(Options(Main));

        Assert.Equal("\"Example Support\" <support@example.net>", SupportAnswerSenders.Allowed(boxes.All,boxes.Primary, null)[0].Formatted);
        Assert.Equal("a@example.com", SupportAnswerSenders.Format("a@example.com", null));
    }
}
