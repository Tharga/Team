using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Deriving a subject from the message, when a case is raised without one.
/// </summary>
/// <remarks>
/// <b>This fails by reading badly rather than by throwing</b>, which is why the cases below are mostly about
/// shape: a subject full of line breaks, one cut mid-word, or an empty one all render as a broken row in a
/// list rather than as an error anybody sees.
/// </remarks>
public class SubjectFromMessageTests
{
    [Fact]
    public void AShortMessage_BecomesTheSubjectUnchanged()
    {
        Assert.Equal("The export is empty.", SubjectFromMessage.Derive("The export is empty."));
    }

    [Fact]
    public void AShortMessage_IsNotMarkedAsTruncated()
    {
        Assert.DoesNotContain("…", SubjectFromMessage.Derive("Short enough."));
    }

    [Fact]
    public void ALongMessage_IsCutAtAWordBoundary_AndMarked()
    {
        var body = "The nightly export finished without an error but produced a file with no rows in it at all.";

        var subject = SubjectFromMessage.Derive(body);

        Assert.Equal("The nightly export finished without an error but…", subject);
        Assert.True(subject.Length <= SupportCaseLimits.DerivedSubjectLength + 1);
    }

    /// <summary>
    /// A cut that lands mid-word reads as a typo rather than as a summary.
    /// </summary>
    [Fact]
    public void TheCut_NeverLandsInsideAWord()
    {
        var body = string.Join(' ', Enumerable.Repeat("alpha", 40));

        var subject = SubjectFromMessage.Derive(body).TrimEnd('…');

        Assert.All(subject.Split(' '), word => Assert.Equal("alpha", word));
    }

    /// <summary>
    /// The ordering that matters: collapse first, measure second. A message opening with a blank line would
    /// otherwise derive an empty subject.
    /// </summary>
    [Fact]
    public void LeadingBlankLines_DoNotProduceAnEmptySubject()
    {
        Assert.Equal("The export is empty.", SubjectFromMessage.Derive("\n\n   The export is empty."));
    }

    [Fact]
    public void HardWrappedNewlines_BecomeSingleSpaces()
    {
        Assert.Equal("One two three", SubjectFromMessage.Derive("One\ntwo\r\nthree"));
    }

    [Fact]
    public void RunsOfWhitespace_AreCollapsed()
    {
        Assert.Equal("One two", SubjectFromMessage.Derive("One     \t   two"));
    }

    /// <summary>
    /// No boundary to cut at. Half a word is a poor subject; nothing at all is a worse one.
    /// </summary>
    [Fact]
    public void ASingleWordLongerThanTheLimit_IsCutAnyway()
    {
        var subject = SubjectFromMessage.Derive(new string('x', 120));

        Assert.Equal(new string('x', SupportCaseLimits.DerivedSubjectLength) + "…", subject);
    }

    [Fact]
    public void NothingToDeriveFrom_IsEmpty()
    {
        Assert.Equal(string.Empty, SubjectFromMessage.Derive(null));
        Assert.Equal(string.Empty, SubjectFromMessage.Derive(""));
        Assert.Equal(string.Empty, SubjectFromMessage.Derive("   \n  "));
    }

    [Fact]
    public void AMessageExactlyAtTheLimit_IsNotTruncated()
    {
        var body = new string('x', SupportCaseLimits.DerivedSubjectLength);

        Assert.Equal(body, SubjectFromMessage.Derive(body));
    }
}
