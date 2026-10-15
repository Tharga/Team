using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

public interface IInboundMailCollection : IDiskRepositoryCollection<InboundMailEntity>;

public class InboundMailCollection : DiskRepositoryCollectionBase<InboundMailEntity>, IInboundMailCollection
{
    private readonly string _collectionName;

    public InboundMailCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<InboundMailCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.InboundMailCollectionName ?? "InboundMail";
    }

    public override string CollectionName => _collectionName;

    /// <remarks>
    /// The mail id is unique and never expires: it is what makes reading a mailbox twice harmless, so unlike the
    /// support ledger it is kept for as long as the mail is.
    /// </remarks>
    public override IEnumerable<CreateIndexModel<InboundMailEntity>> Indices =>
    [
        new(Builders<InboundMailEntity>.IndexKeys.Ascending(x => x.MailId), new CreateIndexOptions { Unique = true, Name = "MailId" }),
        new(Builders<InboundMailEntity>.IndexKeys.Ascending(x => x.TeamKey).Descending(x => x.ReceivedAt).Ascending(x => x.MailId), new CreateIndexOptions { Name = "TeamKey_ReceivedAt" }),
        new(Builders<InboundMailEntity>.IndexKeys.Ascending(x => x.ReceivedAt), new CreateIndexOptions { Name = "ReceivedAt" })
    ];
}
