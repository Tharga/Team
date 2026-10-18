using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Automatic notices, in memory: a catalogue of four notices in a "work" category, two teams and their members, on top of everything mail to users needs.</summary>
internal sealed class UserNoticeFixture
{
    public const string Work = "work";
    public const string Alpha = "team-alpha";
    public const string Beta = "team-beta";
    public const string CaseRead = "case:read";
    public const string Editor = "Editor";

    public static readonly DateTime DigestDue = new(2026, 10, 18, 7, 0, 0, DateTimeKind.Utc);

    public static UserNoticeType Assigned => new()
    {
        Key = "case.assigned",
        Name = "A case is assigned to me",
        Description = "When someone makes you responsible for a case.",
        Category = Work,
        Audience = UserNoticeAudience.NamedOnly,
        Deliveries = [UserNoticeDelivery.AtOnce, UserNoticeDelivery.Digest],
        Subject = "{subject} was assigned to you",
        Body = "<p>{actor} assigned <b>{subject}</b> to you.</p>",
        Placeholders = ["actor"]
    };

    public static UserNoticeType Opened => new()
    {
        Key = "case.opened",
        Name = "A case is opened in my team",
        Category = Work,
        Audience = UserNoticeAudience.WithScope(CaseRead),
        Deliveries = [UserNoticeDelivery.AtOnce, UserNoticeDelivery.Digest],
        DefaultDelivery = UserNoticeDelivery.Digest,
        Subject = "New case: {subject}"
    };

    public static UserNoticeType Joined => new()
    {
        Key = "member.joined",
        Name = "Someone joins my team",
        Category = Work,
        Audience = UserNoticeAudience.AtLeast(AccessLevel.Administrator),
        Subject = "{subject} joined the team"
    };

    public static UserNoticeType Published => new()
    {
        Key = "content.published",
        Name = "Content is published",
        Category = Work,
        Audience = UserNoticeAudience.WithRole(Editor),
        OnByDefault = false,
        Deliveries = [UserNoticeDelivery.Digest],
        DefaultDelivery = UserNoticeDelivery.Digest,
        Subject = "{subject} was published"
    };

    public UserNoticeFixture(Action<UserNoticeOptions> configure = null, Action<UserMailOptions> configureMail = null)
    {
        Mail = new UserMailFixture(o =>
        {
            o.AddCategory(new UserMailCategory { Key = Work, Name = "Notices about my work" });
            configureMail?.Invoke(o);
        });

        Options = new UserNoticeOptions { DigestTime = new TimeOnly(7, 0), TimeZoneId = "UTC", BurstWindow = TimeSpan.FromMinutes(2) };
        if (configure == null)
        {
            Options.Add(Assigned);
            Options.Add(Opened);
            Options.Add(Joined);
            Options.Add(Published);
        }
        else
        {
            configure(Options);
        }

        Notices = UserNoticeRegistry.Create(Options.Notices, Mail.Categories);
        Scopes.Register(CaseRead, AccessLevel.User, "Read cases.");

        Member(Alpha, "anna", AccessLevel.Owner);
        Member(Alpha, "bo", AccessLevel.Administrator, [Editor]);
        Member(Alpha, "cia", AccessLevel.User);
        Member(Alpha, "dan", AccessLevel.Viewer, scopeOverrides: [CaseRead]);
        Member(Alpha, "eva", AccessLevel.Viewer);
        Member(Beta, "anna", AccessLevel.Viewer);
        Member(Beta, "fia", AccessLevel.Administrator, [Editor]);
        Mail.Recipients.Add(new UserMailRecipient { Key = "outsider", EMail = "outsider@example.org", Name = "Outsider" });

        Services.AddSingleton(this);
    }

    public UserMailFixture Mail { get; }
    public UserNoticeOptions Options { get; }
    public UserNoticeRegistry Notices { get; }
    public ScopeRegistry Scopes { get; } = new();
    public InMemoryMessageMemberDirectory Directory { get; } = new();
    public InMemoryUserNoticeOutbox Outbox { get; } = new();
    public InMemoryUserNoticePreferenceStore Store { get; } = new();
    public ServiceCollection Services { get; } = new();
    public ClaimsPrincipal Principal { get; set; } = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Sam")], "Cookies"));

    public IOptions<UserNoticeOptions> Wrapped => Microsoft.Extensions.Options.Options.Create(Options);

    public UserNoticeSchedule Schedule => new(Wrapped);

    public UserNoticeEligibility Eligibility => new(null, Scopes);

    public UserNoticeRaiser Raiser => new(Notices, Outbox, Mail.Time);

    public UserNoticeMail NoticeMail(ITextResolver texts = null) => new(Notices, new UserMailText(texts), Mail.Wrapped);

    public UserNoticeDistributor Distributor(ITextResolver texts = null, UserNoticeRegistry notices = null)
    {
        var registry = notices ?? Notices;
        return new(Outbox, registry, Directory, Store, Eligibility, Mail.Queue, new UserNoticeMail(registry, new UserMailText(texts), Mail.Wrapped), Schedule, Services.BuildServiceProvider(), Wrapped, Mail.Wrapped, Mail.Time);
    }

    public CompositeAuditLogger AuditLogger => new([Mail.Audit], Microsoft.Extensions.Options.Options.Create(new AuditOptions()));

    public UserNoticePreferenceService Preferences(string currentUser = "anna", params string[] systemScopes)
    {
        var users = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns(currentUser);
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(currentUser == null ? null : user);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(systemScopes.Select(x => new Claim(TeamClaimTypes.SystemScope, x)), "Cookies"));
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));

        return new UserNoticePreferenceService(Notices, Mail.Categories, Mail.Preferences, Store, Directory, Eligibility, new TeamAuthorizer(accessor), users, AuditLogger, new AuditEntryFactory(null));
    }

    public UserNoticeTeamService TeamDefaults => new(Notices, Store, AuditLogger, new AuditEntryFactory(null));

    public void Member(string teamKey, string userKey, AccessLevel? level, string[] roles = null, string[] scopeOverrides = null)
    {
        Directory.Members.Add(new UserMessageMember { TeamKey = teamKey, UserKey = userKey, Name = userKey, AccessLevel = level, Roles = roles ?? [], ScopeOverrides = scopeOverrides ?? [] });
        Mail.Recipients.Add(new UserMailRecipient { Key = userKey, EMail = $"{userKey}@example.org", Name = userKey });
    }

    public Task<string> RaiseAsync(UserNoticeType notice, string subjectId, string teamKey = Alpha, string[] recipients = null, string discriminator = null, string linkPath = "/cases/1", Dictionary<string, string> values = null)
        => Raiser.RaiseAsync(new UserNoticeOccurrence
        {
            NoticeKey = notice.Key,
            TeamKey = teamKey,
            SubjectId = subjectId,
            SubjectText = $"Case {subjectId}",
            Discriminator = discriminator,
            Values = values ?? (notice.Placeholders.Contains("actor") ? new Dictionary<string, string> { ["actor"] = "Bo" } : []),
            LinkPath = linkPath,
            Recipients = recipients ?? []
        });

    /// <summary>Runs passes as instance <paramref name="claimant"/> until one handles nothing.</summary>
    public async Task<int> RunAsync(string claimant = "instance-a", ITextResolver texts = null)
    {
        var total = 0;
        for (var handled = -1; handled != 0;)
        {
            handled = await Distributor(texts).RunAsync(claimant, CancellationToken.None);
            total += handled;
        }

        return total;
    }

    /// <summary>The user mails queued by notices, by recipient.</summary>
    public IReadOnlyList<UserMailItem> Queued => Mail.Outbox.Items;

    public UserMailItem QueuedTo(string userKey) => Queued.Single(x => x.Mail.RecipientUserKey == userKey);
}

/// <summary>An in-memory <see cref="IUserNoticeOutbox"/> with the same claim and due-time rules as the MongoDB adapter.</summary>
internal sealed class InMemoryUserNoticeOutbox : IUserNoticeOutbox
{
    private readonly object _gate = new();
    private readonly List<Held> _notices = [];
    private readonly List<UserNoticeItem> _items = [];
    private readonly Dictionary<(string User, string Stream), Stream> _streams = [];

    public static readonly DateTime Idle = DateTime.MaxValue;

    public IReadOnlyList<UserNoticeRecord> Records
    {
        get { lock (_gate) return [.. _notices.Select(x => x.Record)]; }
    }

    public IReadOnlyList<UserNoticeItem> Items
    {
        get { lock (_gate) return [.. _items]; }
    }

    public (DateTime DueAt, string ClaimedBy, string MailKey)? StreamOf(string userKey, string stream)
    {
        lock (_gate) return _streams.TryGetValue((userKey, stream), out var s) ? (s.DueAt, s.ClaimedBy, s.MailKey) : null;
    }

    public Task<string> AddAsync(UserNoticeOccurrence occurrence, DateTime raisedAt, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var id = Guid.NewGuid().ToString("N");
            _notices.Add(new Held(new UserNoticeRecord { Id = id, Occurrence = occurrence, Status = UserNoticeStatus.Pending, RaisedAt = raisedAt, NextAttemptAt = raisedAt }));
            return Task.FromResult(id);
        }
    }

    public Task<UserNoticeRecord> ClaimNextAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var due = _notices
                .Where(x => x.Record.Status == UserNoticeStatus.Pending && x.Record.NextAttemptAt <= now && (x.ClaimedUntil == null || x.ClaimedUntil <= now))
                .OrderBy(x => x.Record.NextAttemptAt)
                .FirstOrDefault();
            if (due == null) return Task.FromResult<UserNoticeRecord>(null);

            due.ClaimedBy = claimant;
            due.ClaimedUntil = now + lease;
            due.Record = due.Record with { Attempts = due.Record.Attempts + 1 };
            return Task.FromResult(due.Record);
        }
    }

    public Task<bool> MarkDistributedAsync(string id, string claimant, int recipients, DateTime at, CancellationToken cancellationToken = default)
        => Complete(id, claimant, x => x with { Status = UserNoticeStatus.Distributed, Recipients = recipients, CompletedAt = at });

    public Task<bool> MarkFailedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default)
        => Complete(id, claimant, x => x with { Status = UserNoticeStatus.Failed, Reason = reason, CompletedAt = at });

    public Task<bool> PostponeAsync(string id, string claimant, string reason, DateTime nextAttemptAt, CancellationToken cancellationToken = default)
        => Complete(id, claimant, x => x with { NextAttemptAt = nextAttemptAt, Reason = reason });

    public Task<UserNoticeRecord> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_notices.FirstOrDefault(x => x.Record.Id == id)?.Record);
    }

    public Task<bool> AddItemAsync(UserNoticeItem item, DateTime dueBy, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var existing = _items.FirstOrDefault(x => x.OccurrenceKey == item.OccurrenceKey && x.UserKey == item.UserKey);
            if (existing != null)
            {
                if (existing.Status == UserNoticeItemStatus.Waiting) MakeDue(existing.UserKey, existing.Stream, dueBy);
                return Task.FromResult(false);
            }

            _items.Add(item with { Id = Guid.NewGuid().ToString("N"), Status = UserNoticeItemStatus.Waiting });
            MakeDue(item.UserKey, item.Stream, dueBy);
            return Task.FromResult(true);
        }
    }

    public Task<UserNoticeStreamClaim> ClaimDueStreamAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var due = _streams
                .Where(x => x.Value.DueAt <= now && (x.Value.ClaimedUntil == null || x.Value.ClaimedUntil <= now))
                .OrderBy(x => x.Value.DueAt)
                .Select(x => (KeyValuePair<(string User, string Stream), Stream>?)x)
                .FirstOrDefault();
            if (due == null) return Task.FromResult<UserNoticeStreamClaim>(null);

            var stream = due.Value.Value;
            stream.ClaimedBy = claimant;
            stream.ClaimedUntil = now + lease;
            return Task.FromResult(new UserNoticeStreamClaim { UserKey = due.Value.Key.User, Stream = due.Value.Key.Stream, MailKey = stream.MailKey, MailItemIds = stream.MailItemIds ?? [] });
        }
    }

    public Task<UserNoticeItem[]> GetWaitingAsync(string userKey, string stream, int max, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult<UserNoticeItem[]>([.. Waiting(userKey, stream).OrderBy(x => x.RaisedAt).Take(max)]);
    }

    public Task<UserNoticeItem[]> GetItemsAsync(string[] ids, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult<UserNoticeItem[]>([.. _items.Where(x => ids.Contains(x.Id))]);
    }

    public Task<bool> BeginMailAsync(string userKey, string stream, string claimant, string mailKey, string[] itemIds, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_streams.TryGetValue((userKey, stream), out var s) || s.ClaimedBy != claimant) return Task.FromResult(false);

            s.MailKey = mailKey;
            s.MailItemIds = itemIds;
            return Task.FromResult(true);
        }
    }

    public Task CompleteItemsAsync(string[] itemIds, UserNoticeItemStatus status, string mailId, string reason, DateTime at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (itemIds.Contains(_items[i].Id) && _items[i].Status == UserNoticeItemStatus.Waiting)
                    _items[i] = _items[i] with { Status = status, MailId = mailId ?? _items[i].MailId, Reason = reason ?? _items[i].Reason, CompletedAt = at };
            }

            return Task.CompletedTask;
        }
    }

    public Task<bool> ReleaseStreamAsync(string userKey, string stream, string claimant, DateTime nextDueWhenWaiting, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_streams.TryGetValue((userKey, stream), out var s) || s.ClaimedBy != claimant) return Task.FromResult(false);

            s.DueAt = Waiting(userKey, stream).Any() ? nextDueWhenWaiting : Idle;
            s.ClaimedBy = null;
            s.ClaimedUntil = null;
            s.MailKey = null;
            s.MailItemIds = null;
            return Task.FromResult(true);
        }
    }

    /// <summary>Lets the claim on every stream run out, as when the instance holding it stopped.</summary>
    public void ExpireStreamClaims()
    {
        lock (_gate)
        {
            foreach (var stream in _streams.Values) stream.ClaimedUntil = stream.ClaimedUntil == null ? null : DateTime.MinValue;
        }
    }

    private IEnumerable<UserNoticeItem> Waiting(string userKey, string stream)
        => _items.Where(x => x.UserKey == userKey && x.Stream == stream && x.Status == UserNoticeItemStatus.Waiting);

    private void MakeDue(string userKey, string stream, DateTime dueBy)
    {
        if (_streams.TryGetValue((userKey, stream), out var existing))
        {
            if (dueBy < existing.DueAt) existing.DueAt = dueBy;
            return;
        }

        _streams[(userKey, stream)] = new Stream { DueAt = dueBy };
    }

    private Task<bool> Complete(string id, string claimant, Func<UserNoticeRecord, UserNoticeRecord> change)
    {
        lock (_gate)
        {
            var held = _notices.FirstOrDefault(x => x.Record.Id == id && x.Record.Status == UserNoticeStatus.Pending && x.ClaimedBy == claimant);
            if (held == null) return Task.FromResult(false);

            held.Record = change(held.Record);
            held.ClaimedBy = null;
            held.ClaimedUntil = null;
            return Task.FromResult(true);
        }
    }

    private sealed class Held(UserNoticeRecord record)
    {
        public UserNoticeRecord Record { get; set; } = record;
        public string ClaimedBy { get; set; }
        public DateTime? ClaimedUntil { get; set; }
    }

    private sealed class Stream
    {
        public DateTime DueAt { get; set; }
        public string ClaimedBy { get; set; }
        public DateTime? ClaimedUntil { get; set; }
        public string MailKey { get; set; }
        public string[] MailItemIds { get; set; }
    }
}

/// <summary>An in-memory <see cref="IUserNoticePreferenceStore"/>.</summary>
internal sealed class InMemoryUserNoticePreferenceStore : IUserNoticePreferenceStore
{
    public Dictionary<(string User, string Notice), UserNoticeSetting> Choices { get; } = [];
    public Dictionary<(string Team, string Notice), UserNoticeSetting> TeamDefaults { get; } = [];

    public Task<IReadOnlyDictionary<string, UserNoticeSetting>> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, UserNoticeSetting>>(Choices.Where(x => x.Key.User == userKey).ToDictionary(x => x.Key.Notice, x => x.Value));

    public Task<IReadOnlyDictionary<string, UserNoticeSetting>> GetChoicesForNoticeAsync(string noticeKey, IReadOnlyCollection<string> userKeys, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, UserNoticeSetting>>(Choices.Where(x => x.Key.Notice == noticeKey && userKeys.Contains(x.Key.User)).ToDictionary(x => x.Key.User, x => x.Value));

    public Task SetChoiceAsync(string userKey, string noticeKey, UserNoticeSetting setting, CancellationToken cancellationToken = default)
    {
        if (setting == null || setting.IsEmpty) Choices.Remove((userKey, noticeKey));
        else Choices[(userKey, noticeKey)] = setting;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, UserNoticeSetting>> GetTeamDefaultsAsync(string teamKey, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, UserNoticeSetting>>(TeamDefaults.Where(x => x.Key.Team == teamKey).ToDictionary(x => x.Key.Notice, x => x.Value));

    public Task SetTeamDefaultAsync(string teamKey, string noticeKey, UserNoticeSetting setting, CancellationToken cancellationToken = default)
    {
        if (setting == null || setting.IsEmpty) TeamDefaults.Remove((teamKey, noticeKey));
        else TeamDefaults[(teamKey, noticeKey)] = setting;
        return Task.CompletedTask;
    }

    public Task<int> RemoveTeamDefaultsAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var keys = TeamDefaults.Keys.Where(x => x.Team == teamKey).ToArray();
        foreach (var key in keys) TeamDefaults.Remove(key);
        return Task.FromResult(keys.Length);
    }
}
