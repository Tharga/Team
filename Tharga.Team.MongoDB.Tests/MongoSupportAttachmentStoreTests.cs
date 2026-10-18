using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The GridFS adapter for support attachments: what goes in comes back, only for the case it was saved for,
/// and a case's files can all be removed.
/// </summary>
/// <remarks>
/// <b>The bucket is the seam, as a collection is for the other stores here.</b> The adapter's own work — the
/// description it writes, the case it checks, the size it reports — runs against a bucket in memory, and the
/// GridFS bucket is checked for the metadata and filters it hands the driver.
/// </remarks>
public class MongoSupportAttachmentStoreTests
{
    private readonly InMemoryBucket _bucket = new();

    private MongoSupportAttachmentStore Store() => new(_bucket);

    private static SupportAttachmentUpload Upload(string name = "report.pdf", byte[] content = null, bool inline = false, string contentId = null) => new()
    {
        FileName = name,
        ContentType = "application/pdf",
        Content = new MemoryStream(content ?? [1, 2, 3]),
        Inline = inline,
        ContentId = contentId
    };

    [Fact]
    public async Task AFile_RoundTrips_WithItsDescriptionAndContent()
    {
        var saved = await Store().SaveAsync("case-1", Upload("logo.png", [5, 6, 7, 8], inline: true, contentId: "logo@example.com"));

        await using var opened = await Store().OpenAsync("case-1", saved.Id);

        Assert.Equal(saved, opened.Attachment);
        Assert.Equal("logo.png", saved.FileName);
        Assert.Equal("application/pdf", saved.ContentType);
        Assert.Equal(4, saved.Size);
        Assert.True(saved.Inline);
        Assert.Equal("logo@example.com", saved.ContentId);

        using var read = new MemoryStream();
        await opened.Content.CopyToAsync(read);
        Assert.Equal([5, 6, 7, 8], read.ToArray());
    }

    [Fact]
    public async Task AFile_IsNotOpenedForAnotherCase()
    {
        var saved = await Store().SaveAsync("case-1", Upload());

        Assert.Null(await Store().OpenAsync("case-2", saved.Id));
    }

    [Fact]
    public async Task AnUnknownId_FindsNothing()
    {
        Assert.Null(await Store().OpenAsync("case-1", ObjectId.GenerateNewId().ToString()));
    }

    [Fact]
    public async Task AFile_IsNotDeletedForAnotherCase()
    {
        var saved = await Store().SaveAsync("case-1", Upload());

        await Store().DeleteAsync("case-2", saved.Id);

        Assert.NotNull(await Store().OpenAsync("case-1", saved.Id));
    }

    [Fact]
    public async Task DeletingACase_RemovesItsFiles_AndNoOthers()
    {
        await Store().SaveAsync("case-1", Upload("a.pdf"));
        await Store().SaveAsync("case-1", Upload("b.pdf"));
        var kept = await Store().SaveAsync("case-2", Upload("c.pdf"));

        var removed = await Store().DeleteForCaseAsync("case-1");

        Assert.Equal(2, removed);
        Assert.Equal([kept.Id], _bucket.Ids);
    }

    [Fact]
    public void TheMetadata_NamesTheCase_AndLeavesOutWhatIsNotSet()
    {
        var metadata = GridFsSupportAttachmentBucket.Metadata(new SupportAttachmentFile { CaseId = "case-1", FileName = "a.pdf", ContentType = "application/pdf" });

        Assert.Equal("case-1", metadata[GridFsSupportAttachmentBucket.CaseIdField].AsString);
        Assert.Equal("application/pdf", metadata[GridFsSupportAttachmentBucket.ContentTypeField].AsString);
        Assert.False(metadata.Contains(GridFsSupportAttachmentBucket.InlineField));
        Assert.False(metadata.Contains(GridFsSupportAttachmentBucket.ContentIdField));
    }

    [Fact]
    public void AStoredFile_ReadsBackFromItsGridFsDescription()
    {
        var id = ObjectId.GenerateNewId();
        var info = new GridFSFileInfo(new BsonDocument
        {
            { "_id", id },
            { "length", 42L },
            { "chunkSize", 261120 },
            { "uploadDate", DateTime.UtcNow },
            { "filename", "logo.png" },
            { "metadata", GridFsSupportAttachmentBucket.Metadata(new SupportAttachmentFile { CaseId = "case-1", FileName = "logo.png", ContentType = "image/png", Inline = true, ContentId = "logo@example.com" }) }
        });

        var file = GridFsSupportAttachmentBucket.ToFile(info);

        Assert.Equal(id.ToString(), file.Id);
        Assert.Equal("case-1", file.CaseId);
        Assert.Equal("logo.png", file.FileName);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(42, file.Length);
        Assert.True(file.Inline);
        Assert.Equal("logo@example.com", file.ContentId);
    }

    [Fact]
    public void ACasesFiles_AreFoundByTheCaseInTheirMetadata()
    {
        var rendered = GridFsSupportAttachmentBucket.ByCase("case-1")
            .Render(new RenderArgs<GridFSFileInfo>(BsonSerializer.LookupSerializer<GridFSFileInfo>(), BsonSerializer.SerializerRegistry))
            .ToString();

        Assert.Contains("\"metadata.CaseId\" : \"case-1\"", rendered);
    }

    [Fact]
    public async Task AnUpload_HandsGridFsTheNameTheContentAndTheDescription()
    {
        var gridFs = Substitute.For<IGridFSBucket>();
        GridFSUploadOptions sent = null;
        byte[] content = null;
        gridFs.UploadFromStreamAsync("report.pdf", Arg.Do<Stream>(x => content = ReadAll(x)), Arg.Do<GridFSUploadOptions>(x => sent = x), Arg.Any<CancellationToken>())
            .Returns(ObjectId.GenerateNewId());

        var bucket = new GridFsSupportAttachmentBucket(() => Task.FromResult(gridFs));

        await bucket.UploadAsync(new SupportAttachmentFile { CaseId = "case-1", FileName = "report.pdf", ContentType = "application/pdf" }, new MemoryStream([1, 2]), CancellationToken.None);

        Assert.Equal([1, 2], content);
        Assert.Equal("case-1", sent.Metadata[GridFsSupportAttachmentBucket.CaseIdField].AsString);
    }

    [Fact]
    public async Task AnIdThatIsNoObjectId_FindsNothing_WithoutAskingGridFs()
    {
        var gridFs = Substitute.For<IGridFSBucket>();
        var bucket = new GridFsSupportAttachmentBucket(() => Task.FromResult(gridFs));

        Assert.Null(await bucket.FindAsync("not-an-id", CancellationToken.None));
        await gridFs.DidNotReceiveWithAnyArgs().FindAsync(default(FilterDefinition<GridFSFileInfo>));
    }

    [Fact]
    public void WithATeamRepository_TheGridFsStoreIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o => o.RegisterTeamRepository<MongoSupportSenderDirectoryTests.TeamEntity, MongoSupportSenderDirectoryTests.Member>());

        var descriptor = services.Single(x => x.ServiceType == typeof(ISupportAttachmentStore));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AHostsOwnAttachmentStore_Wins()
    {
        var own = Substitute.For<ISupportAttachmentStore>();
        var services = new ServiceCollection();
        services.AddScoped(_ => own);
        services.AddThargaTeamRepository(o => o.RegisterTeamRepository<MongoSupportSenderDirectoryTests.TeamEntity, MongoSupportSenderDirectoryTests.Member>());

        Assert.Single(services, x => x.ServiceType == typeof(ISupportAttachmentStore));
    }

    [Fact]
    public void TheBucketName_DefaultsToSupportAttachment()
    {
        Assert.Equal("SupportAttachment", new ThargaTeamOptions().SupportAttachmentBucketName);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class InMemoryBucket : ISupportAttachmentBucket
    {
        private readonly Dictionary<string, (SupportAttachmentFile File, byte[] Content)> _files = [];

        public string[] Ids => [.. _files.Keys];

        public async Task<string> UploadAsync(SupportAttachmentFile file, Stream content, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);

            var id = ObjectId.GenerateNewId().ToString();
            _files[id] = (file with { Id = id, Length = buffer.Length }, buffer.ToArray());

            return id;
        }

        public Task<SupportAttachmentFile> FindAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(_files.TryGetValue(id, out var found) ? found.File : null);

        public Task<SupportAttachmentFile[]> FindByCaseAsync(string caseId, CancellationToken cancellationToken)
            => Task.FromResult(_files.Values.Where(x => x.File.CaseId == caseId).Select(x => x.File).ToArray());

        public Task<Stream> OpenAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream(_files[id].Content, false));

        public Task DeleteAsync(string id, CancellationToken cancellationToken)
        {
            _files.Remove(id);
            return Task.CompletedTask;
        }
    }
}
