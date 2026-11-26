using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Which secure files the sweep removes, that racing sweeps record each removal once, and that a removal interrupted half-way is finished
/// by the next pass rather than left looking done.
/// </summary>
public class SupportSecureFileSweepTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string CaseId = "case-1";

    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ASecureFilePastItsTime_HasItsContentRemoved_AndTheEntrySaysWhen()
    {
        var world = await World.CreateAsync();
        var id = await world.FileAsync(Now.AddDays(-31));

        Assert.Equal(1, await world.Sweep().RunAsync());

        Assert.Empty(world.Files.Files);
        Assert.Equal(Now, (await world.AttachmentAsync(id)).RemovedAt);
    }

    [Fact]
    public async Task ASecureFileWithinItsTime_IsKept()
    {
        var world = await World.CreateAsync();
        var id = await world.FileAsync(Now.AddDays(-29));

        Assert.Equal(0, await world.Sweep().RunAsync());

        Assert.Single(world.Files.Files);
        Assert.Null((await world.AttachmentAsync(id)).RemovedAt);
    }

    [Fact]
    public async Task AnOrdinaryFile_IsNeverRemoved_HoweverOld()
    {
        var world = await World.CreateAsync();
        await world.FileAsync(Now.AddYears(-1), secure: false);

        Assert.Equal(0, await world.Sweep().RunAsync());

        Assert.Single(world.Files.Files);
    }

    [Fact]
    public async Task TwoSweepsRacing_RecordEachRemovalOnce()
    {
        var world = await World.CreateAsync();
        for (var i = 0; i < 20; i++) await world.FileAsync(Now.AddDays(-40));
        var first = world.Sweep();
        var second = world.Sweep();

        var removed = await Task.WhenAll(Task.Run(() => first.RunAsync()), Task.Run(() => second.RunAsync()));

        Assert.Equal(20, removed.Sum());
        Assert.Equal(20, world.Cases.MarkedRemoved);
        Assert.Empty(world.Files.Files);
    }

    [Fact]
    public async Task ASecondSweep_FindsNothingLeft()
    {
        var world = await World.CreateAsync();
        await world.FileAsync(Now.AddDays(-31));

        await world.Sweep().RunAsync();

        Assert.Equal(0, await world.Sweep().RunAsync());
        Assert.Equal(1, world.Cases.MarkedRemoved);
    }

    /// <summary>Content gone and the removal not yet recorded, as after a crash between the two: the next pass records it.</summary>
    [Fact]
    public async Task ARemovalInterruptedHalfWay_IsFinishedByTheNextPass()
    {
        var world = await World.CreateAsync();
        var id = await world.FileAsync(Now.AddDays(-31));
        await world.Files.DeleteAsync(CaseId, id);

        Assert.Equal(1, await world.Sweep().RunAsync());

        Assert.NotNull((await world.AttachmentAsync(id)).RemovedAt);
    }

    [Fact]
    public async Task AFileThatCannotBeRemoved_DoesNotHoldBackTheRest_AndStaysDue()
    {
        var world = await World.CreateAsync();
        var stuck = await world.FileAsync(Now.AddDays(-32));
        var other = await world.FileAsync(Now.AddDays(-31));
        world.Files.DeleteFailsFor = x => x == stuck;

        Assert.Equal(1, await world.Sweep().RunAsync());

        Assert.Null((await world.AttachmentAsync(stuck)).RemovedAt);
        Assert.NotNull((await world.AttachmentAsync(other)).RemovedAt);

        world.Files.DeleteFailsFor = _ => false;
        Assert.Equal(1, await world.Sweep().RunAsync());
    }

    [Fact]
    public async Task WithKeepForZero_NothingIsRemoved()
    {
        var world = await World.CreateAsync();
        await world.FileAsync(Now.AddYears(-1));

        Assert.Equal(0, await world.Sweep(keepFor: TimeSpan.Zero).RunAsync());

        Assert.Single(world.Files.Files);
    }

    [Fact]
    public async Task TheBatchSize_BoundsOneSweep()
    {
        var world = await World.CreateAsync();
        for (var i = 0; i < 5; i++) await world.FileAsync(Now.AddDays(-31));

        Assert.Equal(2, await world.Sweep(batchSize: 2).RunAsync());
    }

    [Fact]
    public async Task ARemoval_IsAnnounced_AsNotFromAChannel()
    {
        var world = await World.CreateAsync();
        await world.FileAsync(Now.AddDays(-31));
        var notifier = Substitute.For<ISupportCaseNotifier>();

        await world.Sweep(notifier: notifier).RunAsync();

        notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x =>
            x.TeamKey == TeamA && x.CaseId == CaseId && x.Change == SupportCaseChange.AttachmentRemoved && !x.FromChannel));
    }

    [Fact]
    public void TheSweep_RunsByDefault_AndKeepForReachesTheOptions()
    {
        var services = new ServiceCollection();
        services.AddThargaSupportCases(o => o.SecureFiles.KeepFor = TimeSpan.FromDays(10));

        Assert.Single(services, x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(SupportSecureFileSweepService));
        Assert.Contains(services, x => x.ServiceType == typeof(SupportSecureFileSweep));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(TimeSpan.FromDays(10), provider.GetRequiredService<IOptions<SupportCaseOptions>>().Value.SecureFiles.KeepFor);
    }

    [Fact]
    public void WithKeepForZero_NoSweepIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddThargaSupportCases(o => o.SecureFiles.KeepFor = TimeSpan.Zero);

        Assert.DoesNotContain(services, x => x.ImplementationType == typeof(SupportSecureFileSweepService));
    }

    [Fact]
    public void ByDefault_SecureFilesAreKeptThirtyDays()
    {
        Assert.Equal(TimeSpan.FromDays(30), new SupportCaseOptions().SecureFiles.KeepFor);
    }

    private sealed class World
    {
        public InMemorySupportCaseStore Cases { get; } = new();

        public InMemorySupportAttachmentStore Files { get; } = new();

        public static async Task<World> CreateAsync()
        {
            var world = new World();
            await world.Cases.AddCaseAsync(new SupportCase
            {
                Id = CaseId,
                TeamKey = TeamA,
                AuthorIdentity = Alice,
                AuthorName = "Alice",
                Subject = "Export",
                Status = SupportCaseStatus.Open,
                CreatedAt = Now.AddYears(-2),
                MessageCount = 1
            }, new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorIdentity = Alice, AuthorName = "Alice", Body = "It is empty.", SentAt = Now.AddYears(-2) });

            return world;
        }

        /// <summary>An entry written at <paramref name="sentAt"/> carrying one file, its content in the attachment store.</summary>
        public async Task<string> FileAsync(DateTime sentAt, bool secure = true)
        {
            var saved = await Files.SaveAsync(CaseId, new SupportAttachmentUpload { FileName = "salaries.xlsx", ContentType = "application/octet-stream", Content = new MemoryStream([1, 2, 3]) });
            await Cases.AppendMessageAsync(TeamA, CaseId, new SupportMessage
            {
                Sequence = 0,
                Kind = SupportMessageKind.User,
                AuthorIdentity = Alice,
                AuthorName = "Alice",
                Body = "For support only.",
                SentAt = sentAt,
                Attachments = [secure ? saved with { Secure = true, UploadedBy = Alice } : saved]
            });

            return saved.Id;
        }

        public async Task<SupportAttachment> AttachmentAsync(string id)
            => (await Cases.GetMessagesAsync(TeamA, CaseId, null, 100)).Items.SelectMany(x => x.Attachments).Single(x => x.Id == id);

        public SupportSecureFileSweep Sweep(TimeSpan? keepFor = null, int batchSize = 100, ISupportCaseNotifier notifier = null)
        {
            var options = new SupportCaseOptions();
            options.SecureFiles.KeepFor = keepFor ?? TimeSpan.FromDays(30);
            options.SecureFiles.SweepBatchSize = batchSize;

            return new SupportSecureFileSweep(Cases, Options.Create(options), new FixedTimeProvider(Now), Files, notifier);
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
