using System.Security.Cryptography;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Makes the opaque key in a team's address: random, lowercase letters and digits only.
/// </summary>
/// <remarks>
/// Lowercase because mail servers treat the local part's case unreliably, and letters and digits only because
/// the address uses <c>-</c> before the suffix and <c>+</c> before an item, so a key must contain neither.
/// </remarks>
internal static class MailKeyGenerator
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 64;

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static string New(int length)
    {
        if (length is < MinimumLength or > MaximumLength) throw new ArgumentOutOfRangeException(nameof(length), length, $"A mail key has {MinimumLength} to {MaximumLength} characters.");

        return new string(RandomNumberGenerator.GetItems<char>(Alphabet, length));
    }
}
