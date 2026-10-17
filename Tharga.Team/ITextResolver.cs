namespace Tharga.Team;

/// <summary>Resolves a user-facing string outside a component, such as in a mail or a server-rendered page, by its key with its English default.</summary>
/// <remarks>
/// <c>Tharga.Team.Blazor</c> registers one that asks the host's <c>IThargaTextProvider</c>, so a host that translates the
/// components translates these strings too. Without one registered, the English defaults are used.
/// </remarks>
public interface ITextResolver
{
    /// <summary>The string for <paramref name="key"/> in the current culture, or <paramref name="englishDefault"/> when there is no translation.</summary>
    Task<string> GetAsync(string key, string englishDefault);
}
