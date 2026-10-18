using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The attachment limits, one rule in both directions: a received mail keeps what fits and names what it left
/// out, a file written on the site is refused.
/// </summary>
public class SupportAttachmentLimitCheckTests
{
    private static readonly SupportAttachmentLimits Limits = new() { MaxFileBytes = 10, MaxTotalBytes = 15, MaxCount = 3 };

    private static InboundAttachment File(string name, int size) => new(name, "application/octet-stream", new byte[size]);

    [Fact]
    public void FilesWithinTheLimits_AreAllKept_InOrder()
    {
        var (kept, notes) = SupportAttachmentLimitCheck.Screen([File("a", 5), File("b", 5)], Limits);

        Assert.Equal(["a", "b"], kept.Select(x => x.FileName).ToArray());
        Assert.Empty(notes);
    }

    [Fact]
    public void AFileTooLarge_IsLeftOut_AndTheNoteSaysWhichAndWhy()
    {
        var (kept, notes) = SupportAttachmentLimitCheck.Screen([File("huge.zip", 11), File("small.txt", 1)], Limits);

        Assert.Equal(["small.txt"], kept.Select(x => x.FileName).ToArray());
        var note = Assert.Single(notes);
        Assert.Contains("huge.zip", note);
        Assert.Contains("11 bytes", note);
        Assert.Contains("10 bytes", note);
    }

    [Fact]
    public void FilesPastTheCount_AreLeftOut_NamedOneByOne()
    {
        var (kept, notes) = SupportAttachmentLimitCheck.Screen([File("1", 1), File("2", 1), File("3", 1), File("4", 1), File("5", 1)], Limits);

        Assert.Equal(3, kept.Count);
        Assert.Equal(2, notes.Count);
        Assert.Contains("4", notes[0]);
        Assert.Contains("at most 3 files", notes[0]);
    }

    [Fact]
    public void AFileTakingTheTotalPastItsLimit_IsLeftOut_ButALaterSmallerOneStillFits()
    {
        var (kept, notes) = SupportAttachmentLimitCheck.Screen([File("first", 10), File("second", 6), File("third", 5)], Limits);

        Assert.Equal(["first", "third"], kept.Select(x => x.FileName).ToArray());
        Assert.Contains("second", Assert.Single(notes));
    }

    [Fact]
    public void TooManyUploads_AreRefused()
    {
        var uploads = Enumerable.Range(0, 4).Select(x => Upload($"{x}.txt", 1)).ToArray();

        var refusal = Assert.Throws<InvalidOperationException>(() => SupportAttachmentLimitCheck.RequireWithin(uploads, Limits));

        Assert.Contains("at most 3 files", refusal.Message);
    }

    [Fact]
    public void AnUploadDeclaringTooMuch_IsRefused_BeforeAnythingIsRead()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => SupportAttachmentLimitCheck.RequireWithin([Upload("big.bin", 11)], Limits));

        Assert.Contains("big.bin", refusal.Message);
    }

    [Fact]
    public void UploadsTogetherDeclaringTooMuch_AreRefused()
    {
        Assert.Throws<InvalidOperationException>(() => SupportAttachmentLimitCheck.RequireWithin([Upload("a", 8), Upload("b", 8)], Limits));
    }

    [Fact]
    public void AnUploadThatCannotSayItsSize_IsLeftToBeMeasured()
    {
        SupportAttachmentLimitCheck.RequireWithin([new SupportAttachmentUpload { FileName = "s", ContentType = "x/y", Content = new UnsizedStream() }], Limits);
    }

    [Theory]
    [InlineData(512, "512 bytes")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(25L * 1024 * 1024, "25 MB")]
    public void ASize_ReadsAsAPersonWouldSayIt(long bytes, string expected)
    {
        Assert.Equal(expected, SupportAttachmentLimitCheck.FormatSize(bytes));
    }

    [Fact]
    public void TheMailOptionDefaults_AreTheContractDefaults()
    {
        Assert.Equal(SupportAttachmentLimits.Default, SupportAttachmentLimitCheck.LimitsOf(new MailOptions()));
    }

    [Fact]
    public void ConfiguredMailOptions_AreTheLimits()
    {
        var limits = SupportAttachmentLimitCheck.LimitsOf(new MailOptions { MaxAttachmentBytes = 1, MaxAttachmentTotalBytes = 2, MaxAttachmentCount = 3 });

        Assert.Equal(new SupportAttachmentLimits { MaxFileBytes = 1, MaxTotalBytes = 2, MaxCount = 3 }, limits);
    }

    private static SupportAttachmentUpload Upload(string name, int size)
        => new() { FileName = name, ContentType = "application/octet-stream", Content = new MemoryStream(new byte[size]) };

    private sealed class UnsizedStream : MemoryStream
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
