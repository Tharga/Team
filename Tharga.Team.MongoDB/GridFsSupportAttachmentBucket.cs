using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>
/// Keeps support attachments in a GridFS bucket of the database the support cases live in, named by
/// <see cref="ThargaTeamOptions.SupportAttachmentBucketName"/>.
/// </summary>
/// <remarks>
/// The bucket's own indexes are created by the driver on the first upload. Finding a case's files filters on
/// <c>metadata.CaseId</c> without an index, which only the rare purge of a team does.
/// </remarks>
internal sealed class GridFsSupportAttachmentBucket : ISupportAttachmentBucket
{
    internal const string CaseIdField = "CaseId";
    internal const string ContentTypeField = "ContentType";
    internal const string InlineField = "Inline";
    internal const string ContentIdField = "ContentId";

    private readonly Func<Task<IGridFSBucket>> _open;
    private IGridFSBucket _bucket;

    public GridFsSupportAttachmentBucket(IMongoDbServiceFactory factory, IOptions<ThargaTeamOptions> options)
        : this(() => OpenAsync(factory, options?.Value?.SupportAttachmentBucketName ?? ThargaTeamOptions.DefaultSupportAttachmentBucketName))
    {
    }

    internal GridFsSupportAttachmentBucket(Func<Task<IGridFSBucket>> open)
    {
        _open = open;
    }

    public async Task<string> UploadAsync(SupportAttachmentFile file, Stream content, CancellationToken cancellationToken)
    {
        var bucket = await BucketAsync();

        var id = await bucket.UploadFromStreamAsync(file.FileName, content, new GridFSUploadOptions { Metadata = Metadata(file) }, cancellationToken);

        return id.ToString();
    }

    public async Task<SupportAttachmentFile> FindAsync(string id, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return null;

        var found = await FindAsync(Builders<GridFSFileInfo>.Filter.Eq(x => x.Id, objectId), cancellationToken);

        return found.FirstOrDefault();
    }

    public async Task<SupportAttachmentFile[]> FindByCaseAsync(string caseId, CancellationToken cancellationToken)
        => await FindAsync(ByCase(caseId), cancellationToken);

    public async Task<Stream> OpenAsync(string id, CancellationToken cancellationToken)
    {
        var bucket = await BucketAsync();

        return await bucket.OpenDownloadStreamAsync(ObjectId.Parse(id), cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return;

        var bucket = await BucketAsync();

        try
        {
            await bucket.DeleteAsync(objectId, cancellationToken);
        }
        catch (GridFSFileNotFoundException)
        {
        }
    }

    internal static FilterDefinition<GridFSFileInfo> ByCase(string caseId)
        => Builders<GridFSFileInfo>.Filter.Eq($"metadata.{CaseIdField}", caseId);

    internal static BsonDocument Metadata(SupportAttachmentFile file)
    {
        var metadata = new BsonDocument
        {
            { CaseIdField, file.CaseId },
            { ContentTypeField, file.ContentType ?? string.Empty }
        };

        if (file.Inline) metadata.Add(InlineField, true);
        if (!string.IsNullOrWhiteSpace(file.ContentId)) metadata.Add(ContentIdField, file.ContentId);

        return metadata;
    }

    internal static SupportAttachmentFile ToFile(GridFSFileInfo info)
    {
        var metadata = info.Metadata ?? new BsonDocument();

        return new SupportAttachmentFile
        {
            Id = info.Id.ToString(),
            CaseId = Text(metadata, CaseIdField),
            FileName = info.Filename,
            ContentType = Text(metadata, ContentTypeField),
            Length = info.Length,
            Inline = metadata.TryGetValue(InlineField, out var inline) && inline.IsBoolean && inline.AsBoolean,
            ContentId = Text(metadata, ContentIdField)
        };
    }

    private async Task<SupportAttachmentFile[]> FindAsync(FilterDefinition<GridFSFileInfo> filter, CancellationToken cancellationToken)
    {
        var bucket = await BucketAsync();

        using var cursor = await bucket.FindAsync(filter, cancellationToken: cancellationToken);

        return [.. (await cursor.ToListAsync(cancellationToken)).Select(ToFile)];
    }

    private async Task<IGridFSBucket> BucketAsync() => _bucket ??= await _open();

    private static string Text(BsonDocument metadata, string field)
        => metadata.TryGetValue(field, out var value) && value.IsString ? value.AsString : null;

    private static async Task<IGridFSBucket> OpenAsync(IMongoDbServiceFactory factory, string bucketName)
    {
        var filesCollection = $"{bucketName}.files";
        var service = factory.GetMongoDbService(() => new DatabaseContext { CollectionName = filesCollection });
        var files = await service.GetCollectionAsync<BsonDocument>(filesCollection);

        return new GridFSBucket(files.Database, new GridFSBucketOptions { BucketName = bucketName });
    }
}
