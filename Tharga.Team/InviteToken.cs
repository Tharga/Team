using System.Security.Cryptography;

namespace Tharga.Team;

/// <summary>
/// Generates the code that authorizes accepting an invitation.
/// </summary>
/// <remarks>
/// <b>This value is a bearer credential.</b> Anyone holding it can join the team it names, so it is generated
/// from <see cref="RandomNumberGenerator"/> and nothing else. <c>Guid.NewGuid()</c> is the tempting
/// alternative and is not equivalent: its version and variant bits are fixed, so it carries 122 bits rather
/// than 128, and nothing in its contract promises unpredictability — only uniqueness.
/// <para>
/// <b>Short is the point.</b> Twenty-two characters against the ninety-odd of the base64-JSON code it
/// replaces, and it discloses nothing: the previous format carried the team key in plain sight, readable by
/// any mail relay, helpdesk ticket or forwarded message that touched the link (Tharga/Team#249).
/// </para>
/// </remarks>
public static class InviteToken
{
    /// <summary>128 bits. Enough that two tokens colliding is not a case worth designing for.</summary>
    public const int ByteCount = 16;

    /// <summary>Length of the generated token, in characters.</summary>
    public const int Length = 22;

    /// <summary>A new token.</summary>
    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(ByteCount);

        // base64url: URL-safe without escaping, and no padding to be stripped by something in the middle.
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
