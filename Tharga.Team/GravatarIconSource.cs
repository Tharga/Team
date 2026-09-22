using System.Security.Cryptography;
using System.Text;

namespace Tharga.Team;

/// <summary>
/// Built-in <see cref="IIconSource"/> that resolves a <b>user</b> subject with an email to its Gravatar
/// image, when enabled via <see cref="IconSettings"/>. Registered after <see cref="StoredIconSource"/> and
/// any consumer sources, so an explicitly uploaded user icon (and custom sources) take precedence and
/// Gravatar is a fallback. Returns null for team subjects, users without an email, or when disabled.
/// </summary>
public sealed class GravatarIconSource : IIconSource
{
    private readonly IconSettings _settings;

    public GravatarIconSource(IconSettings settings = null)
    {
        _settings = settings ?? new IconSettings();
    }

    public Task<IconImage> ResolveAsync(IconSubject subject, CancellationToken cancellationToken = default)
    {
        if (!_settings.GravatarEnabled || subject?.Kind != IconKind.User || string.IsNullOrWhiteSpace(subject.EMail))
            return Task.FromResult<IconImage>(null);

        return Task.FromResult(new IconImage(AvatarUrl(subject.EMail, _settings.GravatarStyle)));
    }

    /// <summary>The Gravatar URL for <paramref name="email"/> at <paramref name="style"/>.</summary>
    /// <param name="email">The user's email.</param>
    /// <param name="style">One of <see cref="GravatarStyles.All"/>; null or blank means <see cref="GravatarStyles.Default"/>.</param>
    /// <returns>An absolute URL, or null when <paramref name="email"/> is missing.</returns>
    public static string AvatarUrl(string email, string style)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var resolved = string.IsNullOrWhiteSpace(style) ? GravatarStyles.Default : style;
        return $"https://www.gravatar.com/avatar/{Md5Hex(email.Trim().ToLowerInvariant())}?d={resolved}";
    }

    /// <summary>
    /// The URL that previews what <paramref name="style"/> looks like, rather than what
    /// <paramref name="email"/> looks like.
    /// </summary>
    /// <remarks>
    /// <b><c>f=y</c> is what makes this a preview of the style.</b> Gravatar serves the account's own photo
    /// whenever the email has one, whatever <c>d</c> asks for — so without forcing the default, every style
    /// previews identically for anybody who has a Gravatar, and a style picker reads as broken.
    /// </remarks>
    /// <param name="email">The email to preview against, usually the signed-in user's.</param>
    /// <param name="style">One of <see cref="GravatarStyles.All"/>.</param>
    /// <returns>An absolute URL, or null when <paramref name="email"/> is missing.</returns>
    public static string PreviewUrl(string email, string style)
    {
        var url = AvatarUrl(email, style);
        return url == null ? null : $"{url}&f=y";
    }

    private static string Md5Hex(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) builder.Append(b.ToString("x2"));
        return builder.ToString();
    }
}
