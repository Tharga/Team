namespace Tharga.Team.Blazor.Framework;

/// <summary>Answers <see cref="ITextResolver"/> from the registered <see cref="IThargaTextProvider"/>, so text outside components is translated the same way.</summary>
internal sealed class ThargaTextResolver(IThargaTextProvider provider) : ITextResolver
{
    public async Task<string> GetAsync(string key, string englishDefault)
    {
        try
        {
            var value = await provider.GetAsync(new TextKey(key, englishDefault));
            return string.IsNullOrEmpty(value) ? englishDefault : value;
        }
        catch
        {
            return englishDefault;
        }
    }
}
