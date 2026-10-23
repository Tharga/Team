using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

/// <summary>
/// A host whose start failed is disposed without <c>StopAsync</c>. The audit writer must end quietly then, also while it
/// waits after a batch it could not write, or the host logs the cancellation as a fault at Error and Critical.
/// </summary>
public class MongoDbAuditLoggerShutdownTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static (MongoDbAuditLogger Logger, Task Written) Failing()
    {
        var written = new TaskCompletionSource();
        var collection = Substitute.For<IAuditRepositoryCollection>();
        collection.AddAsync(Arg.Any<AuditEntryEntity>()).Returns(_ =>
        {
            written.TrySetResult();
            return Task.FromException(new TimeoutException("The database is down."));
        });

        var provider = new ServiceCollection().AddSingleton(collection).BuildServiceProvider();
        var logger = new MongoDbAuditLogger(provider, NullLogger<MongoDbAuditLogger>.Instance, Options.Create(new AuditOptions { BatchSize = 1, FlushIntervalSeconds = 1 }));

        return (logger, written.Task);
    }

    private static async Task<TaskStatus> EndOfAsync(MongoDbAuditLogger logger)
    {
        var finished = await Task.WhenAny(logger.ExecuteTask, Task.Delay(Patience));

        return ReferenceEquals(finished, logger.ExecuteTask) ? logger.ExecuteTask.Status : TaskStatus.Running;
    }

    [Fact]
    public async Task AHostDisposedWhileTheWriterWaitsAfterAFailedBatch_EndsItWithoutAFault()
    {
        var (logger, written) = Failing();
        await logger.StartAsync(CancellationToken.None);
        logger.Log(new AuditEntry { Timestamp = DateTime.UtcNow, EventType = AuditEventType.ServiceCall });
        await written.WaitAsync(Patience);

        logger.Dispose();

        Assert.Equal(TaskStatus.RanToCompletion, await EndOfAsync(logger));
    }

    [Fact]
    public async Task AHostStoppedInTheOrdinaryWayWhileTheWriterWaits_EndsItWithoutAFault()
    {
        var (logger, written) = Failing();
        await logger.StartAsync(CancellationToken.None);
        logger.Log(new AuditEntry { Timestamp = DateTime.UtcNow, EventType = AuditEventType.ServiceCall });
        await written.WaitAsync(Patience);

        await logger.StopAsync(CancellationToken.None);

        Assert.Equal(TaskStatus.RanToCompletion, await EndOfAsync(logger));
    }
}
