using System.Globalization;

namespace Tharga.Team.Email.Users;

/// <summary>Resolves user mail's own strings through the host's <see cref="ITextResolver"/>, falling back to English whenever it has nothing or fails.</summary>
internal sealed class UserMailText(ITextResolver resolver = null)
{
    public async Task<string> GetAsync(string key, string englishDefault)
    {
        if (resolver == null || string.IsNullOrEmpty(key)) return englishDefault;

        try
        {
            var value = await resolver.GetAsync(key, englishDefault);
            return string.IsNullOrWhiteSpace(value) ? englishDefault : value;
        }
        catch
        {
            return englishDefault;
        }
    }

    /// <summary>The resolved template with <paramref name="args"/> in its placeholders; a translation that does not format falls back to the English.</summary>
    public async Task<string> FormatAsync(string key, string englishDefault, params object[] args)
    {
        var template = await GetAsync(key, englishDefault);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, englishDefault, args);
        }
    }

    public Task<string> NameAsync(UserMailCategory category) => GetAsync(category.NameKey, category.Name);

    public async Task<string> DescriptionAsync(UserMailCategory category)
        => string.IsNullOrWhiteSpace(category.Description) ? null : await GetAsync(category.DescriptionKey, category.Description);
}
