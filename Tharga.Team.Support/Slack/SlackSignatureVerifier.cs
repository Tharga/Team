using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tharga.Team.Support.Slack;

/// <summary>
/// Verifies that a request really came from Slack.
/// </summary>
/// <remarks>
/// <b>This is the only thing authenticating the inbound endpoint.</b> It is deliberately public and carries
/// no scope check -- Slack cannot present a credential, so the signature is the credential. Get this wrong
/// and the endpoint accepts anything.
/// <para>
/// Slack signs <c>v0:{timestamp}:{raw body}</c> with the app's signing secret. <b>Raw body means raw.</b>
/// Hashing a deserialized-and-reserialized object produces a different string and never matches, and it
/// fails in a way that looks exactly like a wrong secret -- so the caller must capture the bytes before any
/// model binding touches them.
/// </para>
/// </remarks>
public static class SlackSignatureVerifier
{
    /// <summary>How far out of date a request may be before it is refused.</summary>
    /// <remarks>
    /// Without this the signature alone makes every captured request replayable forever. Five minutes is
    /// Slack's own recommendation and is generous enough for clock skew.
    /// </remarks>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private const string VersionPrefix = "v0";

    /// <summary>
    /// Whether <paramref name="signature"/> is Slack's signature over <paramref name="rawBody"/>.
    /// </summary>
    /// <param name="signingSecret">The app's signing secret. Verification fails when it is missing.</param>
    /// <param name="timestamp">The <c>X-Slack-Request-Timestamp</c> header, in unix seconds.</param>
    /// <param name="rawBody">The request body exactly as received.</param>
    /// <param name="signature">The <c>X-Slack-Signature</c> header, of the form <c>v0=...</c>.</param>
    /// <param name="now">Current time, for the freshness window.</param>
    public static bool IsValid(string signingSecret, string timestamp, string rawBody, string signature, DateTimeOffset now)
    {
        // No secret means the host cannot verify anything. Refusing is the only safe answer: accepting
        // unverified requests would be worse than having no endpoint.
        if (string.IsNullOrEmpty(signingSecret)) return false;
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature) || rawBody == null) return false;

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) return false;

        var age = now - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (age > MaxAge || age < -MaxAge) return false;

        var expected = Compute(signingSecret, timestamp, rawBody);

        // Constant-time. A byte-by-byte comparison that returns early leaks how much of the signature was
        // correct, which is enough to forge one a byte at a time.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    private static string Compute(string signingSecret, string timestamp, string rawBody)
    {
        var basestring = $"{VersionPrefix}:{timestamp}:{rawBody}";

        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(signingSecret),
            Encoding.UTF8.GetBytes(basestring));

        return $"{VersionPrefix}={Convert.ToHexStringLower(hash)}";
    }
}
