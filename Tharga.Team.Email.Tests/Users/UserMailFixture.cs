using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Tests.Mailbox;
using Tharga.Team.Email.Users;
using Tharga.Team.Service.Audit;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Everything mail to users needs, in memory: the queue, the choices, the user records, the transport and the clock.</summary>
internal sealed class UserMailFixture
{
    public const string UserKey = "user-1";
    public const string Address = "anna@example.org";
    public const string News = "news";
    public const string Digest = "digest";

    public static readonly DateTime Start = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    public UserMailFixture(Action<UserMailOptions> configure = null)
    {
        Options = new UserMailOptions { BaseUrl = "https://app.example.com", ReplyTo = "support@example.com" };
        Options.Mail.FromAddress = "hello@example.com";
        Options.Mail.FromName = "Example";
        Options.Mail.Smtp.Host = "smtp.example.com";
        Options.AddCategory(new UserMailCategory { Key = News, Name = "News", Description = "What is new in the product." });
        Options.AddCategory(new UserMailCategory { Key = Digest, Name = "Weekly digest", OnByDefault = false });
        configure?.Invoke(Options);

        Categories = UserMailCategoryRegistry.Create(Options.Categories);
        Recipients.Add(new UserMailRecipient { Key = UserKey, EMail = Address, Name = "Anna Andersson" });
        Policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(x => new OutboundMailDecision(OutboundMailAction.Deliver, x.ArgAt<string>(0), x.ArgAt<string>(1)));
        Audit = new RecordingAuditLogger();
    }

    public UserMailOptions Options { get; }
    public UserMailCategoryRegistry Categories { get; }
    public InMemoryUserMailOutbox Outbox { get; } = new();
    public InMemoryUserMailPreferenceStore Store { get; } = new();
    public InMemoryUserMailRecipients Recipients { get; } = new();
    public RecordingMailClient Mail { get; } = new();
    public IOutboundMailPolicy Policy { get; set; } = Substitute.For<IOutboundMailPolicy>();
    public SteppingTimeProvider Time { get; } = new(Start);
    public IDataProtectionProvider DataProtection { get; } = new EphemeralDataProtectionProvider();
    public RecordingAuditLogger Audit { get; }

    public IOptions<UserMailOptions> Wrapped => Microsoft.Extensions.Options.Options.Create(Options);

    public UserMailUnsubscribeToken Tokens => new(DataProtection, Wrapped);

    public UserMailPreferences Preferences => new(Categories, Store, new CompositeAuditLogger([Audit], Microsoft.Extensions.Options.Options.Create(new AuditOptions())), new AuditEntryFactory(null));

    public UserMailQueue Queue => new(Categories, Outbox, Wrapped, Time);

    public UserMailDispatcher Dispatcher(ITextResolver texts = null)
        => new(Outbox, Categories, Preferences, Recipients, Mail, Policy, new UserMailFooter(Tokens, Wrapped, new UserMailText(texts)), Wrapped, Time);

    public Task<UserMailQueued> QueueAsync(string category = News, string dedup = null, UserMailAttachment[] attachments = null)
        => Queue.QueueAsync(new UserMail
        {
            RecipientUserKey = UserKey,
            Category = category,
            Subject = "Hello",
            BodyHtml = "<p>Something <b>new</b>.</p>",
            DeduplicationKey = dedup,
            Attachments = attachments ?? []
        });
}

internal sealed class InMemoryUserMailOutbox : IUserMailOutbox
{
    private readonly object _gate = new();
    private readonly List<UserMailItem> _items = [];
    private readonly Dictionary<string, UserMailBatch> _batches = [];

    public IReadOnlyList<UserMailItem> Items
    {
        get { lock (_gate) return [.. _items]; }
    }

    public UserMailItem Single => Items.Single();

    public IReadOnlyDictionary<string, UserMailBatch> Batches
    {
        get { lock (_gate) return new Dictionary<string, UserMailBatch>(_batches); }
    }

    public int BatchContentReads { get; private set; }

    public Task<string> AddBatchAsync(UserMailBatch batch, DateTime queuedAt, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var id = Guid.NewGuid().ToString("N");
            _batches[id] = batch with { RecipientUserKeys = [] };

            foreach (var recipient in batch.RecipientUserKeys)
            {
                _items.Add(new UserMailItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Mail = new UserMail { RecipientUserKey = recipient, Category = batch.Category, Subject = batch.Subject },
                    BatchId = id,
                    Status = UserMailStatus.Queued,
                    QueuedAt = queuedAt,
                    NextAttemptAt = queuedAt
                });
            }

            return Task.FromResult(id);
        }
    }

    public Task<UserMailBatch> GetBatchContentAsync(string batchId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            BatchContentReads++;
            return Task.FromResult(_batches.GetValueOrDefault(batchId));
        }
    }

    public Task<UserMailBatchDelivery> GetBatchDeliveryAsync(string batchId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_batches.ContainsKey(batchId)) return Task.FromResult<UserMailBatchDelivery>(null);

            var items = _items.Where(x => x.BatchId == batchId).ToArray();
            var queued = items.Count(x => x.Status == UserMailStatus.Queued);

            return Task.FromResult(new UserMailBatchDelivery
            {
                Id = batchId,
                Recipients = items.Length,
                Outcomes = new UserMailOutcomes
                {
                    Queued = queued,
                    Sent = items.Count(x => x.Status == UserMailStatus.Sent),
                    Skipped = items.Count(x => x.Status == UserMailStatus.Skipped),
                    Failed = items.Count(x => x.Status == UserMailStatus.Failed)
                },
                Failures = [.. items.Where(x => x.Status == UserMailStatus.Failed).Select(x => new UserMailFailure { UserKey = x.Mail.RecipientUserKey, Reason = x.Reason })],
                CompletedAt = queued == 0 ? items.Max(x => x.CompletedAt) : null
            });
        }
    }

    public void ForgetBatch(string batchId)
    {
        lock (_gate) _batches.Remove(batchId);
    }

    public Task<UserMailQueued> AddAsync(UserMail mail, DateTime queuedAt, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var existing = mail.DeduplicationKey == null ? null : _items.FirstOrDefault(x => x.Mail.DeduplicationKey == mail.DeduplicationKey);
            if (existing != null) return Task.FromResult(new UserMailQueued(existing.Id, true));

            var item = new UserMailItem { Id = Guid.NewGuid().ToString("N"), Mail = mail, Status = UserMailStatus.Queued, QueuedAt = queuedAt, NextAttemptAt = queuedAt };
            _items.Add(item);
            return Task.FromResult(new UserMailQueued(item.Id, false));
        }
    }

    public Task<UserMailItem> ClaimNextAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var due = _items
                .Where(x => x.Status == UserMailStatus.Queued && x.NextAttemptAt <= now && (x.ClaimedUntil == null || x.ClaimedUntil <= now))
                .OrderBy(x => x.NextAttemptAt)
                .FirstOrDefault();
            if (due == null) return Task.FromResult<UserMailItem>(null);

            var claimed = due with { ClaimedBy = claimant, ClaimedUntil = now + lease, Attempts = due.Attempts + 1 };
            Replace(due, claimed);
            return Task.FromResult(claimed);
        }
    }

    public Task<bool> MarkSentAsync(string id, string claimant, string messageId, DateTime at, CancellationToken cancellationToken = default)
        => Write(id, claimant, x => Done(x, UserMailStatus.Sent, at) with { MessageId = messageId });

    public Task<bool> MarkSkippedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default)
        => Write(id, claimant, x => Done(x, UserMailStatus.Skipped, at) with { Reason = reason });

    public Task<bool> MarkFailedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default)
        => Write(id, claimant, x => Done(x, UserMailStatus.Failed, at) with { Reason = reason });

    public Task<bool> PostponeAsync(string id, string claimant, string reason, DateTime nextAttemptAt, CancellationToken cancellationToken = default)
        => Write(id, claimant, x => x with { NextAttemptAt = nextAttemptAt, Reason = reason, ClaimedBy = null, ClaimedUntil = null });

    public Task<UserMailItem> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_items.FirstOrDefault(x => x.Id == id));
    }

    private Task<bool> Write(string id, string claimant, Func<UserMailItem, UserMailItem> change)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(x => x.Id == id && x.Status == UserMailStatus.Queued && x.ClaimedBy == claimant);
            if (item == null) return Task.FromResult(false);

            Replace(item, change(item));
            return Task.FromResult(true);
        }
    }

    private static UserMailItem Done(UserMailItem item, UserMailStatus status, DateTime at) => item with
    {
        Status = status,
        CompletedAt = at,
        ClaimedBy = null,
        ClaimedUntil = null,
        Mail = item.Mail with { Attachments = [.. item.Mail.Attachments.Select(x => x with { Content = null })] }
    };

    private void Replace(UserMailItem item, UserMailItem with) => _items[_items.IndexOf(item)] = with;
}

internal sealed class InMemoryUserMailPreferenceStore : IUserMailPreferenceStore
{
    private readonly Dictionary<(string User, string Category), bool> _choices = [];

    public int Writes { get; private set; }

    public IReadOnlyDictionary<(string User, string Category), bool> Stored => _choices;

    public Task<IReadOnlyDictionary<string, bool>> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, bool>>(_choices.Where(x => x.Key.User == userKey).ToDictionary(x => x.Key.Category, x => x.Value));

    public Task SetChoiceAsync(string userKey, string categoryKey, bool receives, CancellationToken cancellationToken = default)
    {
        Writes++;
        _choices[(userKey, categoryKey)] = receives;
        return Task.CompletedTask;
    }

    public Task ClearChoiceAsync(string userKey, string categoryKey, CancellationToken cancellationToken = default)
    {
        Writes++;
        _choices.Remove((userKey, categoryKey));
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryUserMailRecipients : IUserMailRecipientDirectory
{
    private readonly Dictionary<string, UserMailRecipient> _recipients = [];

    public void Add(UserMailRecipient recipient) => _recipients[recipient.Key] = recipient;

    public void Remove(string userKey) => _recipients.Remove(userKey);

    public Task<UserMailRecipient> FindAsync(string userKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_recipients.GetValueOrDefault(userKey));
}

internal sealed class RecordingMailClient : IMailClient
{
    private readonly List<OutboundMail> _sent = [];
    private int _count;

    public IReadOnlyList<OutboundMail> Sent
    {
        get { lock (_sent) return [.. _sent]; }
    }

    public Func<OutboundMail, MailSendResult> Respond { get; set; }

    public bool CanSend => true;

    public bool CanRead => false;

    public Task<MailSendResult> SendAsync(OutboundMail mail, CancellationToken cancellationToken = default)
    {
        lock (_sent) _sent.Add(mail);
        return Task.FromResult(Respond?.Invoke(mail) ?? MailSendResult.Ok($"<m{Interlocked.Increment(ref _count)}@example.com>"));
    }

    public Task<MailFetchResult> FetchAsync(MailFetchPosition position, CancellationToken cancellationToken = default)
        => Task.FromResult(MailFetchResult.Empty(position));
}

internal sealed class RecordingAuditLogger : IAuditLogger
{
    public List<AuditEntry> Entries { get; } = [];

    public void Log(AuditEntry entry) => Entries.Add(entry);

    public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
}

internal sealed class DictionaryTextResolver(Dictionary<string, string> texts) : ITextResolver
{
    public Task<string> GetAsync(string key, string englishDefault) => Task.FromResult(texts.GetValueOrDefault(key) ?? englishDefault);
}
