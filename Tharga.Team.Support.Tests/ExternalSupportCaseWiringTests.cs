using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The anonymous entry point is registered beside the user-only service, never instead of it, and what it does is audited.
/// </summary>
public class ExternalSupportCaseWiringTests
{
    [Fact]
    public void TheEntryPoint_IsRegistered_AsItsOwnService()
    {
        var services = new ServiceCollection();
        services.AddThargaSupportCases();

        Assert.Single(services, x => x.ServiceType == typeof(IExternalSupportCaseService));
        Assert.Single(services, x => x.ServiceType == typeof(ISupportCaseService));
    }

    [Fact]
    public void AReceipt_IsOff_UnlessTheHostTurnsItOn()
    {
        Assert.False(new SupportCaseOptions().External.SendReceipt);
        Assert.NotNull(new SupportCaseOptions().External);
    }

    [Fact]
    public void TheHostsReceiptSettings_ReachTheService()
    {
        var services = new ServiceCollection();
        services.AddThargaSupportCases(o =>
        {
            o.External.SendReceipt = true;
            o.External.ReceiptText = "Tack för ditt meddelande.";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SupportExternalCaseOptions>();

        Assert.True(options.SendReceipt);
        Assert.Equal("Tack för ditt meddelande.", options.ReceiptText);
    }

    [Fact]
    public async Task ACase_IsAudited_AsASupportRaise_FromAWebForm_WithoutTheMessage()
    {
        var (service, audit) = Audited(new ExternalCaseStub());

        await service.RaiseExternalCaseAsync(new ExternalSupportContact { Email = "visitor@example.com", Name = "Vera" }, "Question", "A secret message body");

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("support", entry.Feature);
        Assert.Equal("raise", entry.Action);
        Assert.True(entry.Success);
        Assert.Null(entry.TeamKey);
        Assert.Equal("web-form", entry.Metadata[SupportAuditMetadataKeys.Source]);
        Assert.Equal("false", entry.Metadata[SupportAuditMetadataKeys.WithAssistant]);
        Assert.Equal("case-1", entry.Metadata[SupportAuditMetadataKeys.CaseId]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains("secret") || x.Contains("visitor@example.com") || x.Contains("Vera"));
    }

    [Fact]
    public async Task ARefusedCase_IsAuditedAsFailed_AndTheRefusalReachesTheCaller()
    {
        var (service, audit) = Audited(new ExternalCaseStub { Throws = true });

        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(new ExternalSupportContact { Email = "nope" }, "Question", "Hello"));

        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Success);
        Assert.Equal("web-form", entry.Metadata[SupportAuditMetadataKeys.Source]);
    }

    private static (IExternalSupportCaseService Service, CollectingAuditLogger Audit) Audited(IExternalSupportCaseService inner)
    {
        var audit = new CollectingAuditLogger();

        return (new AuditingExternalSupportCaseServiceDecorator(inner, new CompositeAuditLogger([audit], Options.Create(new AuditOptions())), new AuditEntryFactory(null)), audit);
    }

    private sealed class ExternalCaseStub : IExternalSupportCaseService
    {
        public bool Throws { get; init; }

        public Task<SupportCase> RaiseExternalCaseAsync(ExternalSupportContact contact, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (Throws) throw new ArgumentException("refused");

            return Task.FromResult(new SupportCase
            {
                Id = "case-1",
                AuthorIdentity = null,
                AuthorName = "Vera",
                Subject = subject,
                Status = SupportCaseStatus.Open,
                CreatedAt = DateTime.UtcNow,
                MessageCount = 1
            });
        }
    }

    private sealed class CollectingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Log(AuditEntry entry) => Entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }
}
