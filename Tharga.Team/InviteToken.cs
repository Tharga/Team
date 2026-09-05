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
    /// <summary>
    /// Nine bytes — 72 bits, and twelve base64url characters with no padding to strip.
    /// </summary>
    /// <remarks>
    /// <b>Chosen against the attack rather than by rounding to a familiar number.</b> The realistic attack is
    /// online guessing against the whole pool of outstanding invitations at once, not against one code: any
    /// hit joins a team, and resolving tells the caller which team, so a hit announces itself. At 72 bits,
    /// a hundred thousand distinct sources guessing continuously against a pool of a thousand live
    /// invitations reach even odds in roughly six million years.
    /// <para>
    /// Shorter was considered and rejected with numbers. At 36 bits — six characters — the same attack
    /// succeeds in under an hour, and rate limiting does not rescue it: a per-source throttle is strong
    /// against one attacker and close to worthless against a distributed one. The saving was ten characters
    /// of a link whose host and path are most of its length.
    /// </para>
    /// </remarks>
    public const int ByteCount = 9;

    /// <summary>Length of the generated token, in characters.</summary>
    public const int Length = 12;

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
