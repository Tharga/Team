using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

public class AuditRetentionTests
{
    [Theory]
    [InlineData(null)]   // unset → keep forever
    [InlineData(0)]      // was the instant-expiry footgun → now keep forever
    [InlineData(-5)]     // negative → keep forever
    public void GetExpireAfter_DisabledRetention_ReturnsNull(int? days)
    {
        Assert.Null(AuditRetention.GetExpireAfter(days));
    }

    [Fact]
    public void GetExpireAfter_AboveMax_ReturnsNull_NoOverflow()
    {
        // int.MaxValue used to overflow TimeSpan.FromDays; now treated as keep-forever.
        Assert.Null(AuditRetention.GetExpireAfter(int.MaxValue));
        Assert.Null(AuditRetention.GetExpireAfter(AuditRetention.MaxRetentionDays + 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(90)]
    [InlineData(365)]
    public void GetExpireAfter_PositiveRetention_ReturnsMatchingTimeSpan(int days)
    {
        Assert.Equal(TimeSpan.FromDays(days), AuditRetention.GetExpireAfter(days));
    }

    [Fact]
    public void GetExpireAfter_AtMax_ReturnsTimeSpan()
    {
        Assert.Equal(TimeSpan.FromDays(AuditRetention.MaxRetentionDays),
            AuditRetention.GetExpireAfter(AuditRetention.MaxRetentionDays));
    }

    [Fact]
    public void AuditOptions_DefaultRetention_Is90()
    {
        Assert.Equal(90, new AuditOptions().RetentionDays);
    }

    [Fact]
    public void ResolveDays_NoOptionsRegistered_IsTheDefault()
    {
        Assert.Equal(90, AuditRetention.ResolveDays(null));
    }

    [Fact]
    public void ResolveDays_RetentionNotConfigured_IsTheDefault()
    {
        Assert.Equal(90, AuditRetention.ResolveDays(Options.Create(new AuditOptions())));
    }

    [Fact]
    public void ResolveDays_ExplicitlyNull_StaysNull_SoEntriesAreKeptForever()
    {
        Assert.Null(AuditRetention.ResolveDays(Options.Create(new AuditOptions { RetentionDays = null })));
    }

    [Fact]
    public void ResolveDays_AConfiguredValue_IsKept()
    {
        Assert.Equal(427, AuditRetention.ResolveDays(Options.Create(new AuditOptions { RetentionDays = 427 })));
    }

    [Fact]
    public void ExplicitNull_ConfiguredThroughRegistration_ReachesTheOptionsAsNull()
    {
        var services = new ServiceCollection();
        services.AddThargaAuditLogging(o => o.RetentionDays = null);

        using var provider = services.BuildServiceProvider();

        Assert.Null(AuditRetention.ResolveDays(provider.GetRequiredService<IOptions<AuditOptions>>()));
    }

    [Fact]
    public void RetentionNotConfigured_ThroughRegistration_ReachesTheOptionsAs90()
    {
        var services = new ServiceCollection();
        services.AddThargaAuditLogging();

        using var provider = services.BuildServiceProvider();

        Assert.Equal(90, AuditRetention.ResolveDays(provider.GetRequiredService<IOptions<AuditOptions>>()));
    }
}
