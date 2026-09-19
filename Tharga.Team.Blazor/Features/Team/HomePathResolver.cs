namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// Turns a host's configured home route into the absolute address to navigate to.
/// </summary>
/// <remarks>
/// The sibling of <see cref="InvitePathResolver"/>, and separate from it because the two disagree about
/// the one case that matters: a blank value. An invitation link to the site root redeems nothing, so
/// <see cref="InvitePathResolver"/> falls back to a route; the site root is exactly what this one means by
/// blank, so falling back to a route here would send people somewhere nobody asked for.
/// <para>
/// Pure and static so the string handling can be asserted directly. A host writes <c>/start</c>,
/// <c>start</c> or <c>/start/</c> and means the same thing, and the base URI already ends in a slash.
/// </para>
/// </remarks>
internal static class HomePathResolver
{
    /// <summary>
    /// The address to navigate to for the site's landing page.
    /// </summary>
    /// <param name="homePath">The host's <c>HomePath</c>, in whatever shape they wrote it.</param>
    /// <param name="baseUri">The application's base URI, as <c>NavigationManager</c> reports it.</param>
    public static string Resolve(string homePath, string baseUri)
    {
        var trimmed = homePath?.Trim().Trim('/');

        return string.IsNullOrEmpty(trimmed) ? baseUri : $"{baseUri}{trimmed}";
    }
}
