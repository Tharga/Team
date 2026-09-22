using Microsoft.AspNetCore.Http;
using NSubstitute.ExceptionExtensions;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Audit coverage for <see cref="AuditingIconSettingsServiceDecorator"/>: a save records the values it stored,
/// a failed save is recorded too, and reading is not audited.
/// </summary>
public class AuditingIconSettingsServiceDecoratorTests
{
    private readonly IIconSettingsService _inner = Substitute.For<IIconSettingsService>();
    private readonly FakeAuditBackend _backend;
    private readonly AuditingIconSettingsServiceDecorator _sut;

    public AuditingIconSettingsServiceDecoratorTests()
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create();
        _backend = backend;
        _sut = new AuditingIconSettingsServiceDecorator(_inner, logger, Substitute.For<IHttpContextAccessor>());
    }

    /// <remarks>
    /// The values are the point. "Somebody saved the icon settings" does not answer the question this entry
    /// exists for, which is always asked later and always phrased as "why do the avatars look like that now".
    /// </remarks>
    [Fact]
    public async Task Save_RecordsWhatWasStored()
    {
        await _sut.SaveAsync(new IconSettingsState
        {
            GravatarEnabled = false,
            GravatarStyle = GravatarStyles.RoboHash,
            DefaultUserIconUrl = "https://example.com/a.png",
            AllowUserUpload = false,
            AllowAdminUpload = true
        });

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("iconsettings", entry.Feature);
        Assert.Equal("save", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal("False", entry.Metadata[AuditMetadataKeys.IconGravatarEnabled]);
        Assert.Equal(GravatarStyles.RoboHash, entry.Metadata[AuditMetadataKeys.IconGravatarStyle]);
        Assert.Equal("https://example.com/a.png", entry.Metadata[AuditMetadataKeys.IconDefaultUserIconUrl]);
        Assert.Equal("False", entry.Metadata[AuditMetadataKeys.IconAllowUserUpload]);
        Assert.Equal("True", entry.Metadata[AuditMetadataKeys.IconAllowAdminUpload]);
    }

    [Fact]
    public async Task Save_WhenItFails_IsStillRecorded()
    {
        _inner.SaveAsync(Arg.Any<IconSettingsState>()).ThrowsAsync(new InvalidOperationException("no connection"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SaveAsync(new IconSettingsState()));

        var entry = Assert.Single(_backend.Entries);
        Assert.False(entry.Success);
        Assert.Contains("no connection", entry.ErrorMessage);
    }

    [Fact]
    public async Task Get_IsNotAudited()
    {
        _inner.GetAsync().Returns(new IconSettingsState());

        await _sut.GetAsync();

        Assert.Empty(_backend.Entries);
    }
}
