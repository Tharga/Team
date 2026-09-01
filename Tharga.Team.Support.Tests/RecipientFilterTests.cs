using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Which mail an instance answers for, when one mailbox serves more than one site.
/// </summary>
/// <remarks>
/// <b>The plus-addressing cases are the ones that matter most.</b> A case is corresponded with at
/// <c>support+{caseId}@…</c>, so a filter that compared local parts literally would reject every reply to the
/// toolkit's own mail — and the symptom would be "inbound does not work", not "the filter is wrong".
/// </remarks>
public class RecipientFilterTests
{
    [Fact]
    public void AnUnconfiguredFilter_AcceptsEverything()
    {
        var filter = new RecipientFilter(null);

        Assert.True(filter.AcceptsEverything);
        Assert.True(filter.Accepts("anyone@anywhere.example"));
        Assert.True(filter.AcceptsAny(["anyone@anywhere.example"]));
    }

    [Fact]
    public void AFilterOfOnlyBlankEntries_AcceptsEverything()
    {
        var filter = new RecipientFilter(["", "   ", null]);

        Assert.True(filter.AcceptsEverything);
        Assert.True(filter.Accepts("anyone@anywhere.example"));
    }

    [Fact]
    public void ADomainEntry_AcceptsAnyLocalPartOnThatDomain()
    {
        var filter = new RecipientFilter(["fortdocs.se"]);

        Assert.True(filter.Accepts("support@fortdocs.se"));
        Assert.True(filter.Accepts("billing@fortdocs.se"));
    }

    [Fact]
    public void ADomainEntry_RejectsTheOtherSite()
    {
        var filter = new RecipientFilter(["fortdocs.se"]);

        Assert.False(filter.Accepts("support@eplicta.se"));
    }

    [Fact]
    public void AnAddressEntry_AcceptsOnlyThatAddress()
    {
        var filter = new RecipientFilter(["support@fortdocs.se"]);

        Assert.True(filter.Accepts("support@fortdocs.se"));
        Assert.False(filter.Accepts("billing@fortdocs.se"));
    }

    [Fact]
    public void APlusAddressedReplyTo_IsAcceptedByADomainEntry()
    {
        var filter = new RecipientFilter(["fortdocs.se"]);

        Assert.True(filter.Accepts("support+64f1c2@fortdocs.se"));
    }

    [Fact]
    public void APlusAddressedReplyTo_IsAcceptedByTheAddressItWasDerivedFrom()
    {
        var filter = new RecipientFilter(["support@fortdocs.se"]);

        Assert.True(filter.Accepts("support+64f1c2@fortdocs.se"));
    }

    [Fact]
    public void MatchingIgnoresCase_OnBothSides()
    {
        Assert.True(new RecipientFilter(["FortDocs.SE"]).Accepts("Support@fortdocs.se"));
        Assert.True(new RecipientFilter(["support@fortdocs.se"]).Accepts("SUPPORT@FORTDOCS.SE"));
    }

    [Fact]
    public void ADomainEntryWrittenWithALeadingAt_IsAccepted()
    {
        Assert.True(new RecipientFilter(["@fortdocs.se"]).Accepts("support@fortdocs.se"));
    }

    [Fact]
    public void AHeaderCarryingADisplayName_IsMatchedOnItsAddress()
    {
        var filter = new RecipientFilter(["fortdocs.se"]);

        Assert.True(filter.Accepts("\"FortDocs Support\" <support@fortdocs.se>"));
    }

    [Fact]
    public void SeveralEntries_AreAllHonoured()
    {
        var filter = new RecipientFilter(["fortdocs.se", "help@eplicta.se"]);

        Assert.True(filter.Accepts("anything@fortdocs.se"));
        Assert.True(filter.Accepts("help@eplicta.se"));
        Assert.False(filter.Accepts("other@eplicta.se"));
    }

    [Fact]
    public void AcceptsAny_TakesTheMailWhenOneOfItsRecipientsMatches()
    {
        var filter = new RecipientFilter(["fortdocs.se"]);

        Assert.True(filter.AcceptsAny(["someone@eplicta.se", "support@fortdocs.se"]));
        Assert.False(filter.AcceptsAny(["someone@eplicta.se", "other@example.com"]));
        Assert.False(filter.AcceptsAny([]));
        Assert.False(filter.AcceptsAny(null));
    }

    [Fact]
    public void SomethingThatIsNotAnAddress_IsRejected()
    {
        var filter = new RecipientFilter(["fortdocs.se"]);

        Assert.False(filter.Accepts("fortdocs.se"));
        Assert.False(filter.Accepts("@fortdocs.se"));
        Assert.False(filter.Accepts("support@"));
        Assert.False(filter.Accepts(""));
        Assert.False(filter.Accepts(null));
    }
}
