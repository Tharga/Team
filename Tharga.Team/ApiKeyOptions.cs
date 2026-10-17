namespace Tharga.Team;

/// <summary>
/// Configuration options for API key management.
/// </summary>
public class ApiKeyOptions
{
    /// <summary>
    /// When true, enables full CRUD with custom names, access levels, roles, and expiry.
    /// When false, keys are auto-created and only refresh/lock are available.
    /// Default: false.
    /// </summary>
    public bool AdvancedMode { get; set; }

    /// <summary>
    /// Number of keys to auto-create per team in simple mode. Default: 2.
    /// </summary>
    public int AutoKeyCount { get; set; } = 2;

    /// <summary>
    /// When true, newly created keys are automatically locked after creation
    /// so the raw key value is only visible once. Default: false.
    /// </summary>
    public bool AutoLockKeys { get; set; }

    /// <summary>
    /// Maximum allowed expiry in days for API keys (caps both team and system keys).
    /// Null means no maximum. Default: 365.
    /// </summary>
    public int? MaxExpiryDays { get; set; } = 365;

    /// <summary>
    /// Minimum time between "last used" timestamp writes for a given key. A key authenticating more
    /// often than this only gets its <see cref="IApiKey.LastUsedAt"/> persisted once per window, to
    /// avoid a database write on every authenticated request. Set to <see cref="TimeSpan.Zero"/> to
    /// stamp on every successful authentication. Default: 1 minute.
    /// </summary>
    public TimeSpan LastUsedThrottle { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// What happens to a member's private (owner-scoped) keys in a team when they stop being a member of it:
    /// removed, leaving, or deleted as a user. Default <see cref="PrivateKeyOwnerRemovedAction.Delete"/>.
    /// </summary>
    /// <remarks>
    /// Either way the keys stop authenticating the moment the owner is removed — that is checked on every
    /// request, not by this. This decides only whether they are also cleaned up, so they are not listed,
    /// and cannot come back if the same person is invited again.
    /// </remarks>
    public PrivateKeyOwnerRemovedAction OwnerRemovedAction { get; set; } = PrivateKeyOwnerRemovedAction.Delete;

    /// <summary>
    /// When true, every <b>new</b> team API key must be private (owner-scoped), so each key has exactly one
    /// responsible person. Default: false.
    /// </summary>
    /// <remarks>
    /// Enforced where keys are created, not only in the UI: <c>CreateKeyAsync</c> on both
    /// <see cref="IApiKeyManagementService"/> and the default <see cref="IApiKeyAdministrationService"/>
    /// refuses a team key with no owner, rather than quietly making it the caller's. <c>ApiKeyView</c> follows
    /// the option on its own: its create card always makes a private key, and private keys are always listed.
    /// <para>
    /// Existing team-wide keys keep working and stay listed and manageable; the option governs new keys only.
    /// System keys are not affected. Requires <see cref="AdvancedMode"/>: simple mode auto-creates keys that
    /// have no owner, so the combination fails at startup.
    /// </para>
    /// </remarks>
    public bool RequirePrivateKeys { get; set; }

    /// <summary>
    /// Absolute floor for both <see cref="MinKeyLength"/> and <see cref="MaxKeyLength"/>. 24 alphanumeric
    /// characters is ≈143 bits of entropy (well above 128-bit) and matches the historical minimum, so the
    /// secret can never be configured weaker than keys minted before these options existed.
    /// </summary>
    public const int KeyLengthFloor = 24;

    private int _minKeyLength = 32;
    private int? _maxKeyLength;

    /// <summary>
    /// Number of random alphanumeric characters in the secret portion of a generated API key (the part
    /// after the team/system prefix). When <see cref="MaxKeyLength"/> is null this is the exact, fixed
    /// length; when it is set, this is the lower bound of a random range. Applies to every
    /// <c>CreateKeyAsync</c> — team and system keys — on both create and recycle/regenerate. Default: 32
    /// (≈190-bit). Must be ≥ <see cref="KeyLengthFloor"/> so it cannot be accidentally weakened; a smaller
    /// value throws <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    /// <remarks>
    /// Secret entropy with the base62 alphabet (~5.95 bits per character):
    /// <list type="table">
    /// <item><term>22</term><description>≈128-bit</description></item>
    /// <item><term>32</term><description>≈190-bit (default)</description></item>
    /// <item><term>43</term><description>≈256-bit</description></item>
    /// <item><term>65</term><description>≈384-bit</description></item>
    /// <item><term>86</term><description>≈512-bit</description></item>
    /// </list>
    /// </remarks>
    public int MinKeyLength
    {
        get => _minKeyLength;
        set
        {
            if (value < KeyLengthFloor)
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(MinKeyLength)} must be at least {KeyLengthFloor}.");
            _minKeyLength = value;
        }
    }

    /// <summary>
    /// Optional upper bound for the random secret length. When null (the default), every key uses a fixed
    /// length of <see cref="MinKeyLength"/>; when set, each key's length is chosen at random in
    /// [<see cref="MinKeyLength"/>, <see cref="MaxKeyLength"/>] via a cryptographic RNG. Must be ≥
    /// <see cref="KeyLengthFloor"/>; the ≥ <see cref="MinKeyLength"/> relationship is enforced when a key
    /// is generated.
    /// </summary>
    public int? MaxKeyLength
    {
        get => _maxKeyLength;
        set
        {
            if (value.HasValue && value.Value < KeyLengthFloor)
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(MaxKeyLength)} must be at least {KeyLengthFloor}.");
            _maxKeyLength = value;
        }
    }
}
