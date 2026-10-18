namespace Tharga.Team;

/// <summary>The keys and English defaults of the text a support answer mail carries: who wrote each earlier entry it quotes.</summary>
/// <remarks>Resolved through <see cref="ITextResolver"/>, which <c>Tharga.Team.Blazor</c> answers from the host's text provider, and listed among the toolkit's text keys there.</remarks>
public static class SupportMailTexts
{
    /// <summary>The key of <see cref="QuoteAttribution"/>.</summary>
    public const string QuoteAttributionKey = "team.support.mail.wrote";

    /// <summary>The line above each earlier entry an answer quotes; <c>{0}</c> is when it was written, <c>{1}</c> who wrote it.</summary>
    public const string QuoteAttribution = "On {0}, {1} wrote:";

    /// <summary>The key of <see cref="SupportName"/>.</summary>
    public const string SupportNameKey = "team.support.mail.supportName";

    /// <summary>Who an earlier entry written by support is attributed to, when the host has not named its support.</summary>
    public const string SupportName = "Support";
}
