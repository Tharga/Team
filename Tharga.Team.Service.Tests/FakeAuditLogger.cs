using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// In-memory IAuditLogger backend that captures logged entries.
/// Used as a backend for CompositeAuditLogger in tests.
/// </summary>
internal class FakeAuditBackend : IAuditLogger
{
    private readonly List<AuditEntry> _entries = new();

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public void Log(AuditEntry entry) => _entries.Add(entry);

    public Task<AuditQueryResult> QueryAsync(AuditQuery query)
        => Task.FromResult(new AuditQueryResult());
}

/// <summary>
/// Creates a CompositeAuditLogger with a FakeAuditBackend for testing.
/// </summary>
internal static class FakeAuditLoggerFactory
{
    public static (CompositeAuditLogger Logger, FakeAuditBackend Backend) Create(AuditOptions options = null)
    {
        var backend = new FakeAuditBackend();
        var logger = new CompositeAuditLogger(
            new IAuditLogger[] { backend },
            Options.Create(options ?? new AuditOptions()));
        return (logger, backend);
    }
}
