namespace Tharga.Team;

/// <summary>
/// The Gravatar default-image styles, which is what <see cref="IconSettings.GravatarStyle"/> holds.
/// </summary>
/// <remarks>
/// <b>The set is here rather than in prose so a consumer can bind to it.</b> It used to be written out in the
/// XML docs, in the icons article and in a hardcoded array in the sample's settings page — three copies, none
/// of them reachable from code, so anyone building their own settings UI retyped it from documentation and
/// found out about a typo when an avatar rendered wrong.
/// <para>
/// These are Gravatar's own <c>d</c> parameter values, so the strings are fixed by an external service rather
/// than chosen here. <see cref="Blank"/> renders nothing at all, deliberately.
/// </para>
/// </remarks>
public static class GravatarStyles
{
    /// <summary>A geometric pattern derived from the email. The default.</summary>
    public const string Identicon = "identicon";

    /// <summary>A generated monster.</summary>
    public const string MonsterId = "monsterid";

    /// <summary>A generated face.</summary>
    public const string Wavatar = "wavatar";

    /// <summary>A generated 8-bit character.</summary>
    public const string Retro = "retro";

    /// <summary>A generated robot.</summary>
    public const string RoboHash = "robohash";

    /// <summary>The "mystery person" silhouette — the same image for everyone.</summary>
    public const string MysteryPerson = "mp";

    /// <summary>A transparent image: no avatar at all.</summary>
    public const string Blank = "blank";

    /// <summary>What <see cref="IconSettings.GravatarStyle"/> falls back to when it is null or blank.</summary>
    public const string Default = Identicon;

    /// <summary>Every style, in the order a picker should offer them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Identicon,
        MonsterId,
        Wavatar,
        Retro,
        RoboHash,
        MysteryPerson,
        Blank
    ];

    /// <summary>Whether <paramref name="style"/> is one Gravatar understands.</summary>
    public static bool IsKnown(string style) => All.Contains(style);
}
