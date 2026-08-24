using System.Security.Cryptography;
using System.Text;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The only thing standing between the inbound endpoint and the open internet.
/// </summary>
/// <remarks>
/// <b>Every one of these is a way the endpoint could be wide open.</b> The signature is the credential —
/// Slack cannot present anything else — so a verifier that says yes too easily is not a bug in a helper, it
/// is an unauthenticated write endpoint.
/// </remarks>
public class SlackSignatureVerifierTests
{
    private const string Secret = "8f742231b10e8888abcd99yyyzzz85a5";
    private const string Body = """{"type":"event_callback","event":{"type":"message","text":"hello"}}""";

    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AGenuineSignature_IsAccepted()
    {
        var timestamp = Unix(Now);

        Assert.True(SlackSignatureVerifier.IsValid(Secret, timestamp, Body, Sign(Secret, timestamp, Body), Now));
    }

    [Fact]
    public void AWrongSecret_IsRefused()
    {
        var timestamp = Unix(Now);

        Assert.False(SlackSignatureVerifier.IsValid(Secret, timestamp, Body, Sign("some-other-secret", timestamp, Body), Now));
    }

    /// <summary>
    /// The whole point of signing the body: a tampered payload must not verify against a captured signature.
    /// </summary>
    [Fact]
    public void ATamperedBody_IsRefused()
    {
        var timestamp = Unix(Now);
        var signature = Sign(Secret, timestamp, Body);

        var tampered = Body.Replace("hello", "delete everything");

        Assert.False(SlackSignatureVerifier.IsValid(Secret, timestamp, tampered, signature, Now));
    }

    /// <summary>
    /// Without a freshness window a correctly-signed request is replayable forever, which makes capturing one
    /// as good as holding the secret.
    /// </summary>
    [Fact]
    public void ACorrectlySignedButStaleRequest_IsRefused()
    {
        var old = Now - TimeSpan.FromMinutes(10);
        var timestamp = Unix(old);

        // The signature is genuine; only the age is wrong.
        Assert.True(SlackSignatureVerifier.IsValid(Secret, timestamp, Body, Sign(Secret, timestamp, Body), old));
        Assert.False(SlackSignatureVerifier.IsValid(Secret, timestamp, Body, Sign(Secret, timestamp, Body), Now));
    }

    /// <summary>A clock ahead of ours is as suspicious as one behind, and is also refused.</summary>
    [Fact]
    public void ARequestFromTheFuture_IsRefused()
    {
        var future = Now + TimeSpan.FromMinutes(10);
        var timestamp = Unix(future);

        Assert.False(SlackSignatureVerifier.IsValid(Secret, timestamp, Body, Sign(Secret, timestamp, Body), Now));
    }

    /// <summary>
    /// A host that has not configured the secret cannot verify anything, so it must refuse rather than
    /// accept. This is the failure that would silently open the endpoint.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithNoSigningSecret_EverythingIsRefused(string secret)
    {
        var timestamp = Unix(Now);

        Assert.False(SlackSignatureVerifier.IsValid(secret, timestamp, Body, Sign(Secret, timestamp, Body), Now));
    }

    [Theory]
    [InlineData(null, "v0=abc")]
    [InlineData("not-a-number", "v0=abc")]
    [InlineData("1756036800", null)]
    public void MalformedHeaders_AreRefused(string timestamp, string signature)
    {
        Assert.False(SlackSignatureVerifier.IsValid(Secret, timestamp, Body, signature, Now));
    }

    private static string Unix(DateTimeOffset moment) => moment.ToUnixTimeSeconds().ToString();

    private static string Sign(string secret, string timestamp, string body)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"v0:{timestamp}:{body}"));

        return $"v0={Convert.ToHexStringLower(hash)}";
    }
}
