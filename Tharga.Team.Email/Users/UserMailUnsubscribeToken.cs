using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>The credential an unsubscribe link carries: one user and one category, protected by Data Protection under a purpose of its own.</summary>
/// <remarks>
/// <b>It does not expire</b>, because a mail is read whenever its recipient gets to it. It carries a version instead,
/// so raising <see cref="UserMailOptions.UnsubscribeTokenVersion"/> invalidates every link already sent. Every
/// instance must share the Data Protection key ring, or a link made on one is refused by another.
/// </remarks>
internal sealed class UserMailUnsubscribeToken(IDataProtectionProvider provider, IOptions<UserMailOptions> options)
{
    internal const string Purpose = "Tharga.Team.UserMail.Unsubscribe";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);
    private readonly int _version = options.Value.UnsubscribeTokenVersion;

    public string Create(string userKey, string categoryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);

        return _protector.Protect(JsonSerializer.Serialize(new Payload(_version, userKey, categoryKey)));
    }

    /// <summary>The user and category the token names, or false when it was not made here, was altered, or is of another version.</summary>
    public bool TryRead(string token, out string userKey, out string categoryKey)
    {
        userKey = null;
        categoryKey = null;

        if (string.IsNullOrWhiteSpace(token)) return false;

        Payload payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token.Trim()));
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return false;
        }

        if (payload == null || payload.Version != _version || string.IsNullOrWhiteSpace(payload.UserKey) || string.IsNullOrWhiteSpace(payload.CategoryKey)) return false;

        userKey = payload.UserKey;
        categoryKey = payload.CategoryKey;
        return true;
    }

    private sealed record Payload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("u")] string UserKey,
        [property: JsonPropertyName("c")] string CategoryKey);
}
