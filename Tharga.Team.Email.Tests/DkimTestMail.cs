using System.Text;
using MimeKit;
using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// Mail signed the way a provider signs it, with keys made for the test, and read back as a mailbox hands it over.
/// </summary>
internal static class DkimTestMail
{
    public const string Selector = "s1";
    public const string Sender = "anna@example.com";
    public const string Body = "The export is empty.";

    private static readonly Lazy<AsymmetricCipherKeyPair> ExampleKey = new(NewKey);
    private static readonly Lazy<AsymmetricCipherKeyPair> OtherKey = new(NewKey);

    /// <summary>The key <c>example.com</c> publishes.</summary>
    public static AsymmetricCipherKeyPair Example => ExampleKey.Value;

    /// <summary>A key nobody publishes for <c>example.com</c> — what a forger signs with.</summary>
    public static AsymmetricCipherKeyPair Other => OtherKey.Value;

    /// <summary>A DNS TXT record publishing <paramref name="key"/>'s public half.</summary>
    public static string TxtRecord(AsymmetricCipherKeyPair key)
        => "v=DKIM1; k=rsa; p=" + Convert.ToBase64String(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(key.Public).GetDerEncoded());

    /// <summary>A mail from <paramref name="from"/>, signed by <paramref name="domain"/> with <paramref name="key"/>, as received.</summary>
    public static MimeMessage Signed(string domain, AsymmetricCipherKeyPair key = null, string from = Sender)
        => Received(SignedRaw(domain, key, from));

    /// <summary>The same mail as raw text, for tampering with before it is read.</summary>
    public static string SignedRaw(string domain, AsymmetricCipherKeyPair key = null, string from = Sender)
    {
        var message = Unsigned(from);
        message.Prepare(EncodingConstraint.SevenBit);

        var signer = new DkimSigner((key ?? Example).Private, domain, Selector)
        {
            HeaderCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
            BodyCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed
        };

        signer.Sign(message, [HeaderId.From, HeaderId.To, HeaderId.Subject, HeaderId.Date, HeaderId.MessageId]);

        return Raw(message);
    }

    public static MimeMessage Unsigned(string from = Sender)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse("support@fortdocs.se"));
        message.Subject = "Export is empty";
        message.MessageId = "first-1@example.com";
        message.Date = new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
        message.Body = new TextPart("plain") { Text = Body };

        return message;
    }

    public static MimeMessage Received(string raw) => MimeMessage.Load(new MemoryStream(Encoding.ASCII.GetBytes(raw)));

    private static string Raw(MimeMessage message)
    {
        using var stream = new MemoryStream();
        message.WriteTo(stream);

        return Encoding.ASCII.GetString(stream.ToArray());
    }

    private static AsymmetricCipherKeyPair NewKey()
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new KeyGenerationParameters(new SecureRandom(), 1024));

        return generator.GenerateKeyPair();
    }
}
