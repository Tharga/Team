using System.Security.Claims;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Messages to contacts, in memory: the contact service over contact mail's fixture, with a log of its own.</summary>
internal sealed class ContactMessageFixture
{
    public const string StaffKey = "staff-1";

    public ContactMessageFixture(Action<ContactMessageOptions> configure = null, Action<UserMailOptions> configureMail = null)
    {
        Contacts = new ContactMailFixture(configure, configureMail);
        Users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(_ =>
        {
            var user = Substitute.For<IUser>();
            user.Key.Returns(StaffKey);
            user.Name.Returns("Sam Staff");
            return Task.FromResult(user);
        });
    }

    public ContactMailFixture Contacts { get; }
    public UserMailFixture Mail => Contacts.Mail;
    public InMemoryContactMessageLog Log { get; } = new();
    public IUserService Users { get; } = Substitute.For<IUserService>();

    public ContactMessageService Service => new(
        Contacts.Categories,
        Contacts.Wrapped,
        Mail.Wrapped,
        Contacts.Suppressions,
        Mail.Outbox,
        Mail.Queue,
        Log,
        new TeamAuthorizer(new FixedPrincipal(UserMessageFixture.Staff())),
        Mail.Time,
        Users,
        new CompositeAuditLogger([Mail.Audit], Microsoft.Extensions.Options.Options.Create(new AuditOptions())),
        new AuditEntryFactory(null),
        null,
        Contacts.Footer(),
        Mail.Template);

    public static ContactAddressee Row(string address, string reference = null, string name = null, string detail = null, string excluded = null)
        => new() { Address = address, Reference = reference, Name = name, Detail = detail, ExcludedReason = excluded };

    public static ContactMessage Message(ContactAddressee[] addressees, string category = ContactMailFixture.Offers, UserMailAttachment[] attachments = null) => new()
    {
        Addressees = addressees,
        Category = category,
        Subject = "Something for you",
        BodyHtml = "<p>An <b>offer</b>.</p>",
        Attachments = attachments ?? []
    };

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}

internal sealed class InMemoryContactMessageLog : IContactMessageLog
{
    public List<ContactMessageLogEntry> Entries { get; } = [];

    public Exception FailWith { get; set; }

    public Task AddAsync(ContactMessageLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (FailWith != null) throw FailWith;

        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<ContactMessageLogEntry> GetAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(Entries.FirstOrDefault(x => x.Id == id));

    public Task<ContactMessageLogPage> ListAsync(string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var ordered = Entries.OrderByDescending(x => x.SentAt).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        var start = cursor == null ? 0 : ordered.FindIndex(x => x.Id == cursor) + 1;
        var page = ordered.Skip(start).Take(pageSize).Select(x => x with { Addressees = [] }).ToArray();

        return Task.FromResult(new ContactMessageLogPage { Items = page, NextCursor = start + pageSize < ordered.Count ? page[^1].Id : null });
    }
}
