using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// A team's mailbox. Enforces nothing: it is registered behind the scope proxy, which checks every call
/// against the scope its interface method declares.
/// </summary>
internal sealed class TeamMailboxService : ITeamMailboxService
{
    private const int UniqueKeyAttempts = 5;

    private readonly ITeamMailKeyStore _keys;
    private readonly TeamMailboxOptions _options;
    private readonly TimeProvider _time;
    private readonly Func<int, string> _generate;

    public TeamMailboxService(ITeamMailKeyStore keys, IOptions<TeamMailboxOptions> options, TimeProvider time)
        : this(keys, options, time, null)
    {
    }

    internal TeamMailboxService(ITeamMailKeyStore keys, IOptions<TeamMailboxOptions> options, TimeProvider time, Func<int, string> generate)
    {
        _keys = keys;
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

    public Task<InboundMailPage> ListMailAsync(string teamKey, InboundMailFilter filter = InboundMailFilter.Unhandled, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default) => throw MailNotYetKept();

    public Task<int> GetUnhandledCountAsync(string teamKey, CancellationToken cancellationToken = default) => throw MailNotYetKept();

    public Task<InboundMailItem> GetMailAsync(string teamKey, string mailId, CancellationToken cancellationToken = default) => throw MailNotYetKept();

    public Task<MailContent> GetContentAsync(string teamKey, string mailId, CancellationToken cancellationToken = default) => throw MailNotYetKept();

    public Task<InboundMailItem> SetOutcomeAsync(string teamKey, string mailId, string outcome, MailThreadOwner owner = null, CancellationToken cancellationToken = default) => throw MailNotYetKept();

    private static NotSupportedException MailNotYetKept() => new("Received mail is not kept yet; the mail store arrives with receiving.");

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
