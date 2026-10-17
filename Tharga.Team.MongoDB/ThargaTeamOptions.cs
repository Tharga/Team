using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

public record ThargaTeamOptions
{
    internal Type _userEntity;
    internal Type _userCollectionType;
    internal Type _teamEntity;
    internal Type _teamMemberModel;

    /// <summary>
    /// MongoDB collection name for team documents. Default is "Team".
    /// </summary>
    public string TeamCollectionName { get; set; } = "Team";

    /// <summary>
    /// MongoDB collection name for user documents. Default is "User".
    /// </summary>
    public string UserCollectionName { get; set; } = "User";

    /// <summary>
    /// MongoDB collection name for icon documents (the built-in <see cref="MongoIconStore"/>). Default is "Icon".
    /// </summary>
    public string IconCollectionName { get; set; } = "Icon";

    /// <summary>
    /// Collection holding the site's icon settings (the built-in <see cref="MongoIconSettingsStore"/>). One
    /// document. Default is "IconSettings".
    /// </summary>
    public string IconSettingsCollectionName { get; set; } = "IconSettings";

    /// <summary>
    /// Collection holding support cases and their transcripts. Default <c>SupportCase</c>.
    /// </summary>
    public string SupportCaseCollectionName { get; set; } = "SupportCase";

    /// <summary>
    /// Collection holding each team's mail keys, one document per team. Default <c>TeamMailKey</c>.
    /// </summary>
    public string TeamMailKeyCollectionName { get; set; } = "TeamMailKey";

    /// <summary>
    /// Collection holding received mail for teams' mailboxes and the postmaster — the envelope only; the raw
    /// message is kept by the host's content store. Default <c>InboundMail</c>.
    /// </summary>
    public string InboundMailCollectionName { get; set; } = "InboundMail";

    /// <summary>
    /// Collection remembering which inbound channel events have been handled. Default
    /// <c>SupportEventLedger</c>.
    /// </summary>
    public string SupportEventLedgerCollectionName { get; set; } = "SupportEventLedger";

    /// <summary>
    /// Collection remembering how far the support mailbox has been read. Default
    /// <c>SupportMailPosition</c>.
    /// </summary>
    public string SupportMailPositionCollectionName { get; set; } = "SupportMailPosition";

    /// <summary>The default for <see cref="SupportAttachmentBucketName"/>.</summary>
    public const string DefaultSupportAttachmentBucketName = "SupportAttachment";

    /// <summary>
    /// GridFS bucket holding the files support messages carry, beside the support cases. Default
    /// <c>SupportAttachment</c>.
    /// </summary>
    public string SupportAttachmentBucketName { get; set; } = DefaultSupportAttachmentBucketName;

    /// <summary>
    /// How long a handled event is remembered. Default 24 hours.
    /// </summary>
    /// <remarks>
    /// Only has to outlast the channel's own retry window - Slack gives up long before a day - so this is
    /// generous. Entries expire by TTL index rather than accumulating.
    /// </remarks>
    public TimeSpan SupportEventLedgerRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>The default for <see cref="UserMailPreferenceCollectionName"/>.</summary>
    public const string DefaultUserMailPreferenceCollectionName = "UserMailPreference";

    /// <summary>Collection holding users' mail choices that differ from a category's default, default <c>UserMailPreference</c>.</summary>
    public string UserMailPreferenceCollectionName { get; set; } = DefaultUserMailPreferenceCollectionName;

    /// <summary>The default for <see cref="UserMailCollectionName"/>.</summary>
    public const string DefaultUserMailCollectionName = "UserMail";

    /// <summary>Collection holding queued mail to users and what became of it, default <c>UserMail</c>.</summary>
    public string UserMailCollectionName { get; set; } = DefaultUserMailCollectionName;

    /// <summary>The default for <see cref="UserMailRetention"/>.</summary>
    public static readonly TimeSpan DefaultUserMailRetention = TimeSpan.FromDays(90);

    /// <summary>How long a mail to a user is kept once sent, skipped or given up, a batch once its last mail is, and a sent message from when it was sent; default 90 days.</summary>
    public TimeSpan UserMailRetention { get; set; } = DefaultUserMailRetention;

    /// <summary>The default for <see cref="UserMailBatchCollectionName"/>.</summary>
    public const string DefaultUserMailBatchCollectionName = "UserMailBatch";

    /// <summary>Collection holding the content one mail to many users shares, stored once, default <c>UserMailBatch</c>.</summary>
    public string UserMailBatchCollectionName { get; set; } = DefaultUserMailBatchCollectionName;

    /// <summary>The default for <see cref="UserMessageCollectionName"/>.</summary>
    public const string DefaultUserMessageCollectionName = "UserMessage";

    /// <summary>Collection holding the log of messages staff sent to users, default <c>UserMessage</c>.</summary>
    public string UserMessageCollectionName { get; set; } = DefaultUserMessageCollectionName;

    /// <summary>
    /// Whether to report, at startup, stored team members that carry no access level and are therefore
    /// being treated as <see cref="AccessLevel.Owner"/>. Default <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <b>On by default because the condition it finds is a silent privilege grant.</b> A member with no
    /// stored level is an Owner, with nothing in the logs and nothing visible in the UI to say so — and
    /// since <c>Owner</c> is the enum's zero value, no check above the store can distinguish it from a
    /// member deliberately made one. Silence about that should be something a host chooses, not something
    /// it gets by default.
    /// <para>
    /// The cost of leaving it on is one count query per start, which returns immediately on a deployment
    /// with nothing to report. Set <c>false</c> once the data is corrected, or if the startup query is
    /// unwelcome.
    /// </para>
    /// </remarks>
    public bool CheckMemberAccessLevels { get; set; } = true;

    /// <summary>
    /// Registers the User repository using the built-in <see cref="UserRepositoryCollection{TUserEntity}"/>.
    /// Use the <c>RegisterUserRepository&lt;TUserEntity, TCollection&gt;</c> overload to register a consumer
    /// subclass that declares additional per-deployment indices.
    /// </summary>
    public void RegisterUserRepository<TUserEntity>()
        where TUserEntity : EntityBase, IUser
    {
        _userEntity = typeof(TUserEntity);
        _userCollectionType = null;
    }

    /// <summary>
    /// Registers the User repository with a consumer-provided collection subclass.
    /// Use this when you need to add per-deployment indices on top of the built-in
    /// unique <c>Identity</c> index (e.g. a unique index on a custom email field).
    /// </summary>
    public void RegisterUserRepository<TUserEntity, TCollection>()
        where TUserEntity : EntityBase, IUser
        where TCollection : UserRepositoryCollection<TUserEntity>
    {
        _userEntity = typeof(TUserEntity);
        _userCollectionType = typeof(TCollection);
    }

    public void RegisterTeamRepository<TTeamEntity, TTeamMemberModel>()
        where TTeamEntity : TeamEntityBase<TTeamMemberModel>
        where TTeamMemberModel : TeamMemberBase
    {
        _teamEntity = typeof(TTeamEntity);
        _teamMemberModel = typeof(TTeamMemberModel);
    }
}