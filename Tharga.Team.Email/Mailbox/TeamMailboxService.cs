using Microsoft.Extensions.Options;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// A team's mailbox. Enforces nothing: it is registered behind the scope proxy, which checks every call
/// against the scope its interface method declares.
/// </summary>
internal sealed class TeamMailboxService : ITeamMailboxService
{
    private const int UniqueKeyAttempts = 5;

    private readonly ITeamMailKeyStore _keys;
    private readonly ITeamMailStore _mail;
    private readonly IInboundMailContentStore _content;
    private readonly ITeamMailNotifier _notifier;
    private readonly ITeamPrincipalAccessor _principal;
    private readonly TeamMailboxOptions _options;
    private readonly TimeProvider _time;
    private readonly Func<int, string> _generate;

    public TeamMailboxService(ITeamMailKeyStore keys, ITeamMailStore mail, ITeamMailNotifier notifier, ITeamPrincipalAccessor principal, IOptions<TeamMailboxOptions> options, TimeProvider time, IInboundMailContentStore content = null)
        : this(keys, mail, notifier, principal, options, time, content, null)
    {
    }

    internal TeamMailboxService(ITeamMailKeyStore keys, ITeamMailStore mail, ITeamMailNotifier notifier, ITeamPrincipalAccessor principal, IOptions<TeamMailboxOptions> options, TimeProvider time, IInboundMailContentStore content, Func<int, string> generate)
    {
        _keys = keys;
        _mail = mail;
        _notifier = notifier;
        _principal = principal;
        _content = content;
        _options = options.Value;
        _time = time;
        _generate = generate ?? MailKeyGenerator.New;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async Task<TeamMailAddresses> GetAddressesAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var now = Now;
        var held = (await _keys.GetForTeamAsync(teamKey, cancellationToken)).Where(x => !x.HasRetired(now)).ToArray();

        return new TeamMailAddresses
        {
            TeamKey = teamKey,
            Primary = ToContract(held.FirstOrDefault(x => x.Role == MailKeyRole.Primary)),
            Secondary = ToContract(held.Where(x => x.Role == MailKeyRole.Secondary).OrderByDescending(x => x.CreatedAt).FirstOrDefault())
        };
    }

    public async Task<string> GetItemAddressAsync(string teamKey, string itemKey, CancellationToken cancellationToken = default)
    {
        var primary = (await GetAddressesAsync(teamKey, cancellationToken)).Primary;
        return primary == null ? null : TeamMailAddress.ForItem(primary.Key, itemKey, _options);
    }

    public async Task<TeamMailAddresses> EnsureKeyAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var current = await GetAddressesAsync(teamKey, cancellationToken);
        if (current.Primary != null) return current;

        await _keys.IssueAsync(await NewKeyAsync(teamKey, MailKeyRole.Primary, cancellationToken), cancellationToken);

        return await GetAddressesAsync(teamKey, cancellationToken);
    }

    public async Task<TeamMailAddresses> RotateKeyAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var current = await GetAddressesAsync(teamKey, cancellationToken);
        if (current.Primary == null) return await EnsureKeyAsync(teamKey, cancellationToken);

        var now = Now;
        await _keys.RotateAsync(teamKey, await NewKeyAsync(teamKey, MailKeyRole.Primary, cancellationToken), now + _options.RetirementPeriod, now, cancellationToken);

        return await GetAddressesAsync(teamKey, cancellationToken);
    }

    public Task<InboundMailPage> ListMailAsync(string teamKey, InboundMailFilter filter = InboundMailFilter.Unhandled, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default) =>
        _mail.ListAsync(teamKey, filter, cursor, pageSize, cancellationToken);

    public Task<int> GetUnhandledCountAsync(string teamKey, CancellationToken cancellationToken = default) =>
        _mail.CountUnhandledAsync(teamKey, cancellationToken);

    public Task<InboundMailItem> GetMailAsync(string teamKey, string mailId, CancellationToken cancellationToken = default) =>
        _mail.GetAsync(teamKey, mailId, cancellationToken);

    public async Task<MailContent> GetContentAsync(string teamKey, string mailId, CancellationToken cancellationToken = default)
    {
        if (await _mail.GetAsync(teamKey, mailId, cancellationToken) == null) return null;

        return await ContentOfAsync(_content, mailId, cancellationToken);
    }

    public async Task<InboundMailItem> SetOutcomeAsync(string teamKey, string mailId, string outcome, MailThreadOwner owner = null, CancellationToken cancellationToken = default)
    {
        var handledBy = await new TeamAuthorizer(_principal).GetSubjectAsync();
        var item = await _mail.SetOutcomeAsync(teamKey, mailId, outcome, owner, handledBy, Now, cancellationToken);

        if (item != null) _notifier.Notify(new TeamMailUpdatedEventArgs { TeamKey = teamKey, MailId = mailId, Change = TeamMailChange.Handled });

        return item;
    }

    internal static async Task<MailContent> ContentOfAsync(IInboundMailContentStore content, string mailId, CancellationToken cancellationToken)
    {
        if (content == null) throw new InvalidOperationException($"No {nameof(IInboundMailContentStore)} is registered, so the content of received mail is not kept.");

        var raw = await content.GetAsync(mailId, cancellationToken);
        return raw == null ? null : new MailContent { MailId = mailId, Raw = raw };
    }

    private async Task<TeamMailKeyRecord> NewKeyAsync(string teamKey, MailKeyRole role, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < UniqueKeyAttempts; attempt++)
        {
            var key = _generate(_options.KeyLength);
            if (await _keys.FindAsync(key, cancellationToken) == null) return new TeamMailKeyRecord { TeamKey = teamKey, Key = key, Role = role, CreatedAt = Now };
        }

        throw new InvalidOperationException($"No unused mail key was found in {UniqueKeyAttempts} attempts; the key length is too short for the number of keys held.");
    }

    private TeamMailKey ToContract(TeamMailKeyRecord record) => record == null
        ? null
        : new TeamMailKey { Key = record.Key, Role = record.Role, Address = TeamMailAddress.For(record.Key, _options), CreatedAt = record.CreatedAt, RetiresAt = record.RetiresAt };
}
