using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Who may be mailed, by environment (Tharga/Team#290). Outside production, mail reaches only the override
/// address or an allowed domain; with neither configured, nothing is sent.
/// </summary>
public class OutboundMailPolicyTests
{
    private const string Subject = "You've been invited to join Acme";
    private const string Customer = "kund@kommun.se";
    private const string Colleague = "anna@eplicta.se";
    private const string Override = "test-inbox@eplicta.se";

    private static OutboundMailPolicy Policy(string environment, string address = null, params string[] allowedDomains)
    {
        IHostEnvironment host = null;
        if (environment != null)
        {
            host = Substitute.For<IHostEnvironment>();
            host.EnvironmentName.Returns(environment);
        }

        return new OutboundMailPolicy(
            Options.Create(new OutboundMailPolicyOptions { Address = address, AllowedDomains = allowedDomains }),
            host);
    }

    [Fact]
    public void InProduction_EveryoneIsMailedUnchanged()
    {
        var decision = Policy(Environments.Production).Decide(Customer, Subject);

        Assert.Equal(OutboundMailAction.Deliver, decision.Action);
        Assert.Equal(Customer, decision.Recipient);
        Assert.Equal(Subject, decision.Subject);
    }

    [Fact]
    public void OutsideProduction_AnAllowedDomainIsMailedUnchanged()
    {
        var decision = Policy(Environments.Staging, Override, "eplicta.se").Decide(Colleague, Subject);

        Assert.Equal(OutboundMailAction.Deliver, decision.Action);
        Assert.Equal(Colleague, decision.Recipient);
        Assert.Equal(Subject, decision.Subject);
    }

    [Fact]
    public void SeveralDomainsCanBeAllowed()
    {
        var policy = Policy(Environments.Staging, Override, "eplicta.se", "fortdocs.se");

        Assert.Equal(OutboundMailAction.Deliver, policy.Decide("per@fortdocs.se", Subject).Action);
        Assert.Equal(OutboundMailAction.Deliver, policy.Decide(Colleague, Subject).Action);
        Assert.Equal(OutboundMailAction.Redirect, policy.Decide(Customer, Subject).Action);
    }

    /// <summary>The redirected mail still says who it was for, or nobody can tell what they are testing.</summary>
    [Fact]
    public void OutsideProduction_AnyoneElseIsRedirected_AndTheSubjectNamesTheIntendedRecipient()
    {
        var decision = Policy(Environments.Staging, Override, "eplicta.se").Decide(Customer, Subject);

        Assert.Equal(OutboundMailAction.Redirect, decision.Action);
        Assert.Equal(Override, decision.Recipient);
        Assert.Equal($"[Staging -> {Customer}] {Subject}", decision.Subject);
    }

    /// <summary>Fail closed: a test environment with SMTP credentials and nothing else must not mail customers.</summary>
    [Fact]
    public void OutsideProduction_WithNoOverrideAddress_NothingIsSent()
    {
        var decision = Policy(Environments.Development).Decide(Customer, Subject);

        Assert.Equal(OutboundMailAction.Withhold, decision.Action);
        Assert.False(decision.ShouldSend);
    }

    [Fact]
    public void OutsideProduction_WithOnlyAllowedDomains_OthersAreWithheld()
    {
        var policy = Policy(Environments.Development, address: null, "eplicta.se");

        Assert.Equal(OutboundMailAction.Deliver, policy.Decide(Colleague, Subject).Action);
        Assert.Equal(OutboundMailAction.Withhold, policy.Decide(Customer, Subject).Action);
    }

    /// <summary>An environment that cannot be identified is not assumed to be production.</summary>
    [Fact]
    public void WithNoHostEnvironment_ItIsTreatedAsNotProduction()
    {
        var decision = Policy(environment: null).Decide(Customer, Subject);

        Assert.Equal(OutboundMailAction.Withhold, decision.Action);
    }

    /// <summary>An undefined pipeline variable arrives as its own literal name, which looks like a configured value.</summary>
    [Fact]
    public void AnUnexpandedVariable_CountsAsUnset()
    {
        var policy = Policy(Environments.Staging, "$(MailOverrideAddress)", "$(AllowedDomain)");

        Assert.Equal(OutboundMailAction.Withhold, policy.Decide(Customer, Subject).Action);
        Assert.Equal(OutboundMailAction.Withhold, policy.Decide("someone@$(AllowedDomain)", Subject).Action);
    }

    [Fact]
    public void DomainsMatchCaseInsensitively()
    {
        var decision = Policy(Environments.Staging, Override, "EPLICTA.se").Decide("Anna@Eplicta.SE", Subject);

        Assert.Equal(OutboundMailAction.Deliver, decision.Action);
    }

    /// <summary>A subdomain is a different domain; admitting it silently would widen the list without saying so.</summary>
    [Fact]
    public void DomainsMatchExactly_NotSubdomainsOrSuffixes()
    {
        var policy = Policy(Environments.Staging, Override, "eplicta.se");

        Assert.Equal(OutboundMailAction.Redirect, policy.Decide("anna@mail.eplicta.se", Subject).Action);
        Assert.Equal(OutboundMailAction.Redirect, policy.Decide("anna@noteplicta.se", Subject).Action);
    }

    [Fact]
    public void AnAllowedDomainMayBeWrittenWithALeadingAt()
    {
        var decision = Policy(Environments.Staging, Override, "@eplicta.se").Decide(Colleague, Subject);

        Assert.Equal(OutboundMailAction.Deliver, decision.Action);
    }
}
