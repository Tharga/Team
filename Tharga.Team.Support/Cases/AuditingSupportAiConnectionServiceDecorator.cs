using System.Diagnostics;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Audits every change to a team's AI connection, and every test of one, under <c>assistant</c>.
/// </summary>
/// <remarks>
/// <b>The key is never recorded</b>, nor anything a provider said that could carry it: the metadata is the kind, the
/// model and the provider's host. A refused connection is recorded with the service's problem, which already has the
/// key taken out.
/// <para>
/// Reads and listing models are not audited; they change nothing and cost nothing. A test is, because it sends a
/// message on the team's account.
/// </para>
/// <para>
/// Wraps the authorizing decorator, so a refusal is recorded rather than lost, as for support cases.
/// </para>
/// </remarks>
internal sealed class AuditingSupportAiConnectionServiceDecorator(
    ISupportAiConnectionService inner,
    CompositeAuditLogger auditLogger,
    IAuditEntryFactory auditEntryFactory) : ISupportAiConnectionService
{
    private const string Feature = "assistant";

    public Task<SupportAiConnectionChoices> GetChoicesAsync(string teamKey, CancellationToken cancellationToken = default)
        => inner.GetChoicesAsync(teamKey, cancellationToken);

    public Task<SupportAiConnection> GetConnectionAsync(string teamKey, CancellationToken cancellationToken = default)
        => inner.GetConnectionAsync(teamKey, cancellationToken);

    public Task UsePlatformModelAsync(string teamKey, CancellationToken cancellationToken = default)
        => AuditAsync("use-platform", nameof(UsePlatformModelAsync), teamKey, Meta(SupportAiConnectionKind.Platform), () => inner.UsePlatformModelAsync(teamKey, cancellationToken));

    public Task TurnOffAsync(string teamKey, CancellationToken cancellationToken = default)
        => AuditAsync("turn-off", nameof(TurnOffAsync), teamKey, Meta(SupportAiConnectionKind.Off), () => inner.TurnOffAsync(teamKey, cancellationToken));

    public Task<SupportAiConnectionResult> ConnectAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default)
        => AuditResultAsync("connect", nameof(ConnectAsync), teamKey, request, () => inner.ConnectAsync(teamKey, request, cancellationToken));

    public Task<SupportAiModelList> ListModelsAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default)
        => inner.ListModelsAsync(teamKey, request, cancellationToken);

    public Task<SupportAiConnectionResult> TestAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default)
        => AuditResultAsync("test", nameof(TestAsync), teamKey, request, () => inner.TestAsync(teamKey, request, cancellationToken));

    private async Task AuditAsync(string action, string methodName, string teamKey, Dictionary<string, string> metadata, Func<Task> operation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await operation();
            Log(action, methodName, sw.ElapsedMilliseconds, true, teamKey, null, metadata);
        }
        catch (Exception ex)
        {
            Log(action, methodName, sw.ElapsedMilliseconds, false, teamKey, ex.Message, metadata);
            throw;
        }
    }

    private async Task<SupportAiConnectionResult> AuditResultAsync(string action, string methodName, string teamKey, SupportAiProviderRequest request, Func<Task<SupportAiConnectionResult>> operation)
    {
        var metadata = Meta(request?.Kind ?? SupportAiConnectionKind.Platform, request?.Model, request?.BaseAddress);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await operation();
            Log(action, methodName, sw.ElapsedMilliseconds, result.Succeeded, teamKey, result.Problem, metadata);
            return result;
        }
        catch (Exception ex)
        {
            Log(action, methodName, sw.ElapsedMilliseconds, false, teamKey, SupportAiConnectionService.Hide(ex.Message, request?.ApiKey), metadata);
            throw;
        }
    }

    private void Log(string action, string methodName, long durationMs, bool success, string teamKey, string errorMessage, IReadOnlyDictionary<string, string> metadata)
        => auditLogger.Log(auditEntryFactory.Create(Feature, action, methodName, durationMs, success, errorMessage, teamKey, metadata));

    private static Dictionary<string, string> Meta(SupportAiConnectionKind kind, string model = null, string baseAddress = null)
    {
        var metadata = new Dictionary<string, string> { [SupportAuditMetadataKeys.AssistantConnection] = kind.ToString() };

        if (!string.IsNullOrWhiteSpace(model)) metadata[SupportAuditMetadataKeys.AssistantModel] = model.Trim();
        if (Uri.TryCreate(baseAddress?.Trim(), UriKind.Absolute, out var uri)) metadata[SupportAuditMetadataKeys.AssistantHost] = uri.Host;

        return metadata;
    }
}
