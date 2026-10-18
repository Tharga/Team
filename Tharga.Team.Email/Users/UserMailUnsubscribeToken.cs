using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>The credential an unsubscribe link carries: one user and one category, or for contact mail one address and one category, protected by Data Protection under a purpose of its own.</summary>
/// <remarks>
/// <b>It does not expire</b>, because a mail is read whenever its recipient gets to it. It carries a version instead,
/// so raising <see cref="UserMailOptions.UnsubscribeTokenVersion"/> invalidates every link already sent. Every
/// instance must share the Data Protection key ring, or a link made on one is refused by another.
/// <para>
/// <b>The two kinds cannot stand in for each other.</b> A user's token names a user key and no address, a contact's an
/// address and no user key, and each is read only as what it is, so a contact's link never touches a user's choices and a
/// user's never opts out an address. A token made before addresses existed has no address and reads as a user's, as it was.
/// </para>
/// </remarks>
internal sealed class UserMailUnsubscribeToken(IDataProtectionProvider provider, IOptions<UserMailOptions> options)
{
    internal const string Purpose = "Tharga.Team.UserMail.Unsubscribe";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);
    private readonly int _version = options.Value.UnsubscribeTokenVersion;

    /// <summary>A token for one user and one category.</summary>
    public string Create(string userKey, string categoryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);

        return Protect(new Payload(_version, userKey, categoryKey));
    }

    /// <summary>A token for one address and one contact category; the address is kept trimmed and in lower case.</summary>
    public string CreateForAddress(string address, string categoryKey)
    {
        var normalized = UserMailAddresses.Normalize(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalized, nameof(address));
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);

        return Protect(new Payload(_version, null, categoryKey, normalized));
    }

    /// <summary>The user and category a user's token names, or false when it was not made here, was altered, is of another version, or names an address.</summary>
    public bool TryRead(string token, out string userKey, out string categoryKey)
    {
        userKey = null;
        categoryKey = null;

        var payload = Read(token);
        if (payload == null || string.IsNullOrWhiteSpace(payload.UserKey) || payload.Address != null) return false;

        userKey = payload.UserKey;
        categoryKey = payload.CategoryKey;
        return true;
    }

    /// <summary>The address and category a contact's token names, or false when it was not made here, was altered, is of another version, or names a user.</summary>
    public bool TryReadAddress(string token, out string address, out string categoryKey)
    {
        address = null;
        categoryKey = null;

        var payload = Read(token);
        if (payload == null || string.IsNullOrWhiteSpace(payload.Address) || payload.UserKey != null) return false;

        address = payload.Address;
        categoryKey = payload.CategoryKey;
        return true;
    }

    private string Protect(Payload payload) => _protector.Protect(JsonSerializer.Serialize(payload));

    /// <summary>The payload of a token made here, of this version and naming a category; otherwise null.</summary>
    private Payload Read(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        Payload payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token.Trim()));
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }

        return payload == null || payload.Version != _version || string.IsNullOrWhiteSpace(payload.CategoryKey) ? null : payload;
    }

    private sealed record Payload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("u"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string UserKey,
        [property: JsonPropertyName("c")] string CategoryKey,
        [property: JsonPropertyName("a"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string Address = null);
}
