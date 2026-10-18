using Tharga.Team.Email;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// A file name from a sender is data from a stranger: no path, no control characters, no direction overrides,
/// a sensible length, and never empty.
/// </summary>
public class AttachmentFileNameTests
{
    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Users\someone\report.pdf", "report.pdf")]
    [InlineData(@"..\..\boot.ini", "boot.ini")]
    [InlineData("folder/", "attachment.pdf")]
    [InlineData("  .hidden  ", "hidden")]
    [InlineData("rapport ärende 7.docx", "rapport ärende 7.docx")]
    public void APath_IsCutToItsLastSegment(string given, string expected)
    {
        Assert.Equal(expected, AttachmentFileName.Sanitize(given, "application/pdf"));
    }

    [Fact]
    public void ControlCharacters_AreRemoved()
    {
        Assert.Equal("reportpdf.txt", AttachmentFileName.Sanitize("report\r\n\tpdf\0.txt", "text/plain"));
    }

    /// <summary>A right-to-left override makes <c>invoice[RLO]fdp.exe</c> display as a PDF; removed, it reads as what it is.</summary>
    [Fact]
    public void DirectionOverrides_AreRemoved()
    {
        Assert.Equal("invoicefdp.exe", AttachmentFileName.Sanitize("invoice\u202Efdp.exe", "application/octet-stream"));
    }

    [Theory]
    [InlineData("a<b>c:d\"e|f?g*h.txt", "abcdefgh.txt")]
    public void CharactersNoFileSystemAccepts_AreRemoved(string given, string expected)
    {
        Assert.Equal(expected, AttachmentFileName.Sanitize(given, "text/plain"));
    }

    [Theory]
    [InlineData(null, "application/pdf", "attachment.pdf")]
    [InlineData("", "image/png", "attachment.png")]
    [InlineData("\u0001\u0002", "text/plain", "attachment.txt")]
    [InlineData(null, "application/x-made-up", "attachment")]
    [InlineData(null, null, "attachment")]
    public void NothingLeft_FallsBackToANameFromTheContentType(string given, string contentType, string expected)
    {
        Assert.Equal(expected, AttachmentFileName.Sanitize(given, contentType));
    }

    [Fact]
    public void ALongName_IsShortened_KeepingItsExtension()
    {
        var name = AttachmentFileName.Sanitize(new string('x', 500) + ".pdf", "application/pdf");

        Assert.Equal(AttachmentFileName.MaxLength, name.Length);
        Assert.EndsWith(".pdf", name);
    }

    [Theory]
    [InlineData("Invoice 42", "Invoice 42.eml")]
    [InlineData("Invoice 42.eml", "Invoice 42.eml")]
    [InlineData("forwarded.EML", "forwarded.EML")]
    [InlineData(null, "attachment.eml")]
    public void AForwardedMail_AlwaysEndsInEml(string given, string expected)
    {
        Assert.Equal(expected, AttachmentFileName.SanitizeWithExtension(given, "message/rfc822", ".eml"));
    }

    [Theory]
    [InlineData("application/pdf", "application/pdf")]
    [InlineData("Application/PDF", "application/pdf")]
    [InlineData("text/plain; charset=UTF-8; name=\"x.txt\"", "text/plain; charset=utf-8")]
    [InlineData("image/png; name=\"../../x.png\"", "image/png")]
    [InlineData("not a type", "application/octet-stream")]
    [InlineData("", "application/octet-stream")]
    [InlineData(null, "application/octet-stream")]
    public void ADeclaredContentType_IsKeptWithoutAnythingButItsCharset(string declared, string expected)
    {
        Assert.Equal(expected, AttachmentContentType.Normalize(declared));
    }
}
