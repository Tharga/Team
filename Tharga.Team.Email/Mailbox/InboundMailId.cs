using System.Security.Cryptography;
using System.Text;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// The id of a received mail, derived from the delivery itself so the same delivery always gets the same id.
/// </summary>
/// <remarks>
/// <b>Message-Id and the address it was delivered to, together.</b> A mail sent to two teams arrives twice with
/// one Message-Id; each delivery is a mail of its own. A mail with no Message-Id — malformed, or from a sender
/// that leaves it out — is identified by its content instead, which is just as stable on a re-read.
/// </remarks>
internal static class InboundMailId
{
    public static string For(string messageId, string deliveredTo, byte[] raw)
    {
        var source = string.IsNullOrWhiteSpace(messageId)
            ? SHA256.HashData(raw ?? [])
            : Encoding.UTF8.GetBytes($"{messageId.Trim()}|{deliveredTo?.Trim().ToLowerInvariant()}");

        return Convert.ToHexStringLower(SHA256.HashData(source))[..32];
    }
}
