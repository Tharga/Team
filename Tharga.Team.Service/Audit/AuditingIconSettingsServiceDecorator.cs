using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Decorator that records changes to the site's icon settings. Reads pass through unlogged.
/// </summary>
/// <remarks>
/// <b>What changed is recorded, not just that something did.</b> These settings are site-wide, so "avatars
/// look different today" is a question somebody asks weeks later — and an entry saying only "settings saved"
/// answers none of it. The metadata carries the values as stored.
/// </remarks>
public class AuditingIconSettingsServiceDecorator : IIconSettingsService
{
    private readonly IIconSettingsService _inner;
    private readonly CompositeAuditLogger _auditLogger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private const string Feature = "iconsettings";

    public AuditingIconSettingsServiceDecorator(IIconSettingsService inner, CompositeAuditLogger auditLogger, IHttpContextAccessor httpContextAccessor)
    {
        _inner = inner;
        _auditLogger = auditLogger;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<IconSettingsState> GetAsync() => _inner.GetAsync();

    public async Task SaveAsync(IconSettingsState settings)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SaveAsync(settings);
            sw.Stop();
            Log(sw.ElapsedMilliseconds, true, settings);
        }
        catch (Exception e)
        {
            sw.Stop();
            Log(sw.ElapsedMilliseconds, false, settings, e.Message);
            throw;
        }
    }

    private void Log(long durationMs, bool success, IconSettingsState settings, string errorMessage = null)
    {
        var entry = AuditHelper.BuildEntry(_httpContextAccessor, Feature, "save", nameof(SaveAsync), durationMs, success, errorMessage, teamKey: null);
        entry = entry with
        {
            Metadata = new Dictionary<string, string>
            {
                { AuditMetadataKeys.IconGravatarEnabled, settings?.GravatarEnabled.ToString() },
                { AuditMetadataKeys.IconGravatarStyle, settings?.GravatarStyle },
                { AuditMetadataKeys.IconDefaultUserIconUrl, settings?.DefaultUserIconUrl },
                { AuditMetadataKeys.IconAllowUserUpload, settings?.AllowUserUpload.ToString() },
                { AuditMetadataKeys.IconAllowAdminUpload, settings?.AllowAdminUpload.ToString() }
            }
        };
        _auditLogger.Log(entry);
    }
}
