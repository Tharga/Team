using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

/// <summary>
/// What a host sets on the audit options through <c>AddThargaAuditLogging</c> is what the toolkit then reads.
/// </summary>
/// <remarks>
/// <b>The options were copied by a list, and <c>DefaultAuditMode</c> was not on it.</b> A host under privacy rules that set it
/// to <see cref="AuditMode.None"/>, to make silence the default, had the setting accepted and never in effect, so every
/// unannotated method kept being recorded.
/// </remarks>
public class AddThargaAuditLoggingOptionsTests
{
    private static AuditOptions Registered(Action<AuditOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddThargaAuditLogging(configure);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AuditOptions>>().Value;
    }

    [Fact]
    public void TheDefaultAuditMode_SetByTheHost_IsTheOneRead()
    {
        Assert.Equal(AuditMode.None, Registered(o => o.DefaultAuditMode = AuditMode.None).DefaultAuditMode);
    }

    [Fact]
    public void WithNothingSet_TheDefaultAuditModeIsStillAccess_AsItAlwaysWas()
    {
        Assert.Equal(AuditMode.Access, Registered(_ => { }).DefaultAuditMode);
    }

    /// <summary>Every public setting is set to something other than its default and must arrive, so one added later cannot be missed.</summary>
    [Fact]
    public void EveryPublicSetting_IsCarriedOver()
    {
        var set = new AuditOptions
        {
            StorageMode = AuditStorageMode.Logger,
            CallerFilter = AuditCallerFilter.Api,
            EventFilter = AuditEventFilter.All,
            ExcludedActions = ["read"],
            DefaultAuditMode = AuditMode.None,
            ExcludedEndpoints = ["/health"],
            RetentionDays = 12,
            BatchSize = 7,
            FlushIntervalSeconds = 3
        };

        var got = Registered(o =>
        {
            foreach (var property in typeof(AuditOptions).GetProperties().Where(p => p.CanRead && p.CanWrite)) property.SetValue(o, property.GetValue(set));
        });

        foreach (var property in typeof(AuditOptions).GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            var expected = property.GetValue(set);
            var actual = property.GetValue(got);

            if (expected is Array expectedItems) Assert.Equal(expectedItems.Cast<object>(), ((Array)actual).Cast<object>());
            else Assert.True(Equals(expected, actual), $"The audit option {property.Name} was not carried over.");
        }
    }
}
