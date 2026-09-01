using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Trimming the quoted thread and signature off a reply.
/// </summary>
/// <remarks>
/// <b>The bias is deliberate and asymmetric.</b> Keeping too much makes a transcript read badly; cutting too
/// much silently loses what somebody said. So an unrecognised layout keeps the whole body, and the last test
/// here is the one that matters most.
/// </remarks>
public class QuotedTextTests
{
    [Fact]
    public void AQuoteIntroducedByAnAttributionLine_IsCut()
    {
        var body = "Yes, that fixed it.\n\nOn Mon, 1 Sep 2026 at 09:00, Support wrote:\n> Have you tried again?";

        Assert.Equal("Yes, that fixed it.", QuotedText.Trim(body));
    }

    [Theory]
    [InlineData("Den 1 september 2026 skrev Support:")]
    [InlineData("On 1 Sep 2026, Support Agent wrote:")]
    public void AttributionLines_InOtherLocales_AreCut(string attribution)
    {
        Assert.Equal("Thanks.", QuotedText.Trim($"Thanks.\n\n{attribution}\n> earlier"));
    }

    [Fact]
    public void ASignature_IsCutAtTheStandardDelimiter()
    {
        Assert.Equal("Thanks.", QuotedText.Trim("Thanks.\n\n-- \nA User\nAcme AB\n+46 8 123456"));
    }

    [Fact]
    public void AQuotedBlock_IsCutEvenWithNoAttribution()
    {
        Assert.Equal("No change.", QuotedText.Trim("No change.\n\n> Have you tried again?\n> Support"));
    }

    [Theory]
    [InlineData("-----Original Message-----")]
    [InlineData("________________________________")]
    [InlineData("---------- Forwarded message ---------")]
    public void ClientSeparators_AreCut(string separator)
    {
        Assert.Equal("See below.", QuotedText.Trim($"See below.\n\n{separator}\nFrom: Support"));
    }

    [Fact]
    public void AReplyWithNothingQuoted_IsUntouched()
    {
        Assert.Equal("Just this.", QuotedText.Trim("Just this."));
    }

    [Fact]
    public void BlankLinesBeforeTheCut_AreTrimmedOff()
    {
        Assert.Equal("Done.", QuotedText.Trim("Done.\n\n\n> quoted"));
    }

    [Fact]
    public void MultipleParagraphsBeforeTheQuote_AreAllKept()
    {
        var body = "First para.\n\nSecond para.\n\nOn Mon, Support wrote:\n> old";

        Assert.Equal("First para.\n\nSecond para.", QuotedText.Trim(body));
    }

    [Fact]
    public void NothingAtAll_IsEmpty()
    {
        Assert.Equal(string.Empty, QuotedText.Trim(null));
        Assert.Equal(string.Empty, QuotedText.Trim("   "));
    }

    /// <summary>
    /// <b>The safety net.</b> If a reply is written entirely inside the quote — top-posted badly, or a client
    /// this does not recognise — cutting would store an empty entry, which reads as the person having sent
    /// nothing. Keeping the lot is ugly and recoverable; losing it is neither.
    /// </summary>
    [Fact]
    public void AReplyThatIsAllQuote_IsKeptWhole()
    {
        var body = "> Have you tried again?\n> Support";

        Assert.Equal(body, QuotedText.Trim(body));
    }

    [Fact]
    public void WindowsLineEndings_AreHandled()
    {
        Assert.Equal("Yes.", QuotedText.Trim("Yes.\r\n\r\n-- \r\nA User"));
    }
}
