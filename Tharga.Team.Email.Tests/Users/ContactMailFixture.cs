using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Contact mail, in memory: two contact categories, the opt-outs and a transport of its own, on top of everything mail to users needs.</summary>
internal sealed class ContactMailFixture
{
    public const string Offers = "offers";
    public const string Events = "events";
    public const string Address = "kim@customer.example";
    public const string SenderLine = "Example Ltd, 1 Example Street, Example Town";

    public ContactMailFixture(Action<ContactMessageOptions> configure = null, Action<UserMailOptions> configureMail = null)
    {
        Mail = new UserMailFixture(configureMail);
        Options = new ContactMessageOptions { SenderLine = SenderLine };
        Options.AddCategory(new ContactMailCategory { Key = Offers, Name = "Offers", Description = "What may be of interest to your organisation." });
        Options.AddCategory(new ContactMailCategory { Key = Events, Name = "Events" });
        configure?.Invoke(Options);

        Categories = ContactMailCategoryRegistry.Create(Options.Categories, Mail.Categories);
    }

    public UserMailFixture Mail { get; }
    public ContactMessageOptions Options { get; }
    public ContactMailCategoryRegistry Categories { get; }
    public InMemoryContactMailSuppressionStore Suppressions { get; } = new();
    public RecordingMailClient ContactMail { get; } = new();

    public IOptions<ContactMessageOptions> Wrapped => Microsoft.Extensions.Options.Options.Create(Options);

    public ContactMailFooter Footer(ITextResolver texts = null) => new(Mail.Tokens, Mail.Wrapped, Wrapped, new UserMailText(texts));

    public ContactMailSending Sending(ITextResolver texts = null) => new(Categories, Suppressions, Footer(texts), ContactMail, Mail.Wrapped, Wrapped);

    public UserMailDispatcher Dispatcher(ITextResolver texts = null)
        => new(Mail.Outbox, Mail.Categories, Mail.Preferences, Mail.Recipients, Mail.Mail, Mail.Policy, Mail.Footer(texts), Mail.Wrapped, Mail.Time, null, Mail.Template, Sending(texts));

    public ContactMailUnsubscribe Unsubscribe
        => new(Mail.Tokens, Categories, Suppressions, Mail.Time, new CompositeAuditLogger([Mail.Audit], Microsoft.Extensions.Options.Options.Create(new AuditOptions())), new AuditEntryFactory(null));

    public ContactMailFrequencyCache FrequencyCache { get; } = new();

    public ContactMailFrequencies Frequencies => new(Mail.Outbox, Categories, FrequencyCache, Mail.Time);

    /// <summary>Queues one contact mail per address in a batch, as the contact message service does.</summary>
    public Task<string> QueueAsync(string category = Offers, params string[] addresses)
        => Mail.Outbox.AddBatchAsync(new UserMailBatch
        {
            RecipientUserKeys = [],
            RecipientAddresses = [.. (addresses.Length == 0 ? [Address] : addresses).Select(x => new UserMailBatchAddress(x, "Kim Customer"))],
            Category = category,
            Subject = "Something for you",
            BodyHtml = "<p>An <b>offer</b>.</p>",
            Body = "An offer.",
            Kind = UserMailKind.Contact
        }, Mail.Time.GetUtcNow().UtcDateTime);

    public Task<bool> OptOutAsync(string category, string address = Address, ContactMailSuppressionSource source = ContactMailSuppressionSource.Staff)
        => Suppressions.AddAsync(new ContactMailSuppression { Address = address, Category = category, SuppressedAt = Mail.Time.GetUtcNow().UtcDateTime, Source = source });
}

internal sealed class InMemoryContactMailSuppressionStore : IContactMailSuppressionStore
{
    private readonly List<ContactMailSuppression> _items = [];

    public IReadOnlyList<ContactMailSuppression> Items
    {
        get { lock (_items) return [.. _items]; }
    }

    public Task<bool> AddAsync(ContactMailSuppression suppression, CancellationToken cancellationToken = default)
    {
        var stored = suppression with { Address = suppression.Address.Trim().ToLowerInvariant() };

        lock (_items)
        {
            if (_items.Any(x => x.Address == stored.Address && x.Category == stored.Category)) return Task.FromResult(false);

            _items.Add(stored);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<ContactMailSuppression>> GetAsync(IReadOnlyCollection<string> addresses, CancellationToken cancellationToken = default)
    {
        var wanted = (addresses ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).ToHashSet();

        lock (_items) return Task.FromResult<IReadOnlyList<ContactMailSuppression>>([.. _items.Where(x => wanted.Contains(x.Address))]);
    }
}
