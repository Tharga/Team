namespace Tharga.Team.Blazor.Framework;

/// <summary>The keys of the text a support answer mail carries outside components, listed here so a host can translate them from <see cref="ThargaTextKeys.All"/>.</summary>
public static class SupportMailText
{
    /// <summary>The line above each earlier entry an answer quotes; <c>{0}</c> is when, <c>{1}</c> who.</summary>
    public static readonly TextKey QuoteAttribution = new(SupportMailTexts.QuoteAttributionKey, SupportMailTexts.QuoteAttribution);

    /// <summary>Who an earlier entry written by support is attributed to, when the host has not named its support.</summary>
    public static readonly TextKey SupportName = new(SupportMailTexts.SupportNameKey, SupportMailTexts.SupportName);

    /// <summary>Every key here.</summary>
    public static readonly TextKey[] All = [QuoteAttribution, SupportName];
}
