using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Building the per-case reply address and reading it back.
/// </summary>
/// <remarks>
/// The two halves are one convention, so they are tested against each other: a build that disagrees with its
/// parse produces replies that arrive and match nothing, which looks exactly like a mailbox not being read.
/// </remarks>
public class PerCaseAddressTests
{
    private const string From = "support@fortdocs.se";

    [Fact]
    public void AnAddressIsBuiltFromTheSenderAndTheCase()
    {
        Assert.Equal("support+case-1@fortdocs.se", PerCaseAddress.Build(From, "case-1"));
    }

    [Fact]
    public void WhatIsBuilt_ReadsBackAsTheSameCase()
    {
        var address = PerCaseAddress.Build(From, "64f1c2ab");

        Assert.Equal("64f1c2ab", PerCaseAddress.CaseIdIn([address], From));
    }

    [Fact]
    public void TheCaseIsFound_AmongSeveralRecipients()
    {
        Assert.Equal("case-1", PerCaseAddress.CaseIdIn(["someone@example.com", "support+case-1@fortdocs.se"], From));
    }

    [Fact]
    public void MatchingIgnoresCase()
    {
        Assert.Equal("case-1", PerCaseAddress.CaseIdIn(["Support+case-1@FortDocs.se"], From));
    }

    [Fact]
    public void APlainRecipient_CarriesNoCase()
    {
        Assert.Null(PerCaseAddress.CaseIdIn(["support@fortdocs.se"], From));
    }

    /// <summary>
    /// Another mailbox on the same domain using plus-addressing for its own purposes is not this convention.
    /// </summary>
    [Fact]
    public void ADifferentLocalPart_CarriesNoCase()
    {
        Assert.Null(PerCaseAddress.CaseIdIn(["billing+case-1@fortdocs.se"], From));
    }

    [Fact]
    public void ADifferentDomain_CarriesNoCase()
    {
        Assert.Null(PerCaseAddress.CaseIdIn(["support+case-1@eplicta.se"], From));
    }

    [Fact]
    public void AnEmptyTag_CarriesNoCase()
    {
        Assert.Null(PerCaseAddress.CaseIdIn(["support+@fortdocs.se"], From));
    }

    /// <summary>
    /// A tag holding the separator could not be read back, so refusing to build beats emitting an address
    /// that parses as a different case.
    /// </summary>
    [Fact]
    public void ACaseIdThatCouldNotBeReadBack_IsRefused()
    {
        Assert.Null(PerCaseAddress.Build(From, "a+b"));
        Assert.Null(PerCaseAddress.Build(From, "a@b"));
    }

    [Fact]
    public void NothingToWorkFrom_IsNull()
    {
        Assert.Null(PerCaseAddress.Build(null, "case-1"));
        Assert.Null(PerCaseAddress.Build(From, null));
        Assert.Null(PerCaseAddress.Build("not-an-address", "case-1"));
        Assert.Null(PerCaseAddress.CaseIdIn(null, From));
        Assert.Null(PerCaseAddress.CaseIdIn(["support+case-1@fortdocs.se"], null));
    }
}
