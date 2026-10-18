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

    /// <summary>The default for <see cref="ContactMailSuppressionCollectionName"/>.</summary>
    public const string DefaultContactMailSuppressionCollectionName = "ContactMailSuppression";

    /// <summary>Collection holding the addresses that opted out of contact mail, kept for good, default <c>ContactMailSuppression</c>.</summary>
    public string ContactMailSuppressionCollectionName { get; set; } = DefaultContactMailSuppressionCollectionName;

    /// <summary>The default for <see cref="UserNoticeCollectionName"/>.</summary>
    public const string DefaultUserNoticeCollectionName = "UserNotice";

    /// <summary>Collection holding raised notice occurrences until they are distributed, default <c>UserNotice</c>.</summary>
    public string UserNoticeCollectionName { get; set; } = DefaultUserNoticeCollectionName;

    /// <summary>The default for <see cref="UserNoticeItemCollectionName"/>.</summary>
    public const string DefaultUserNoticeItemCollectionName = "UserNoticeItem";

    /// <summary>Collection holding each user's notices until and after they are mailed, default <c>UserNoticeItem</c>.</summary>
    public string UserNoticeItemCollectionName { get; set; } = DefaultUserNoticeItemCollectionName;

    /// <summary>The default for <see cref="UserNoticeStreamCollectionName"/>.</summary>
    public const string DefaultUserNoticeStreamCollectionName = "UserNoticeStream";

    /// <summary>Collection holding when each user's next notice mail is due, default <c>UserNoticeStream</c>.</summary>
    public string UserNoticeStreamCollectionName { get; set; } = DefaultUserNoticeStreamCollectionName;

    /// <summary>The default for <see cref="UserNoticePreferenceCollectionName"/>.</summary>
    public const string DefaultUserNoticePreferenceCollectionName = "UserNoticePreference";

    /// <summary>Collection holding users' notice choices that differ from a default, default <c>UserNoticePreference</c>.</summary>
    public string UserNoticePreferenceCollectionName { get; set; } = DefaultUserNoticePreferenceCollectionName;

    /// <summary>The default for <see cref="UserNoticeTeamDefaultCollectionName"/>.</summary>
    public const string DefaultUserNoticeTeamDefaultCollectionName = "UserNoticeTeamDefault";

    /// <summary>Collection holding teams' notice defaults that differ from the catalogue's, default <c>UserNoticeTeamDefault</c>.</summary>
    public string UserNoticeTeamDefaultCollectionName { get; set; } = DefaultUserNoticeTeamDefaultCollectionName;

    /// <summary>The default for <see cref="UserNoticeRetention"/>.</summary>
    public static readonly TimeSpan DefaultUserNoticeRetention = TimeSpan.FromDays(90);

    /// <summary>How long a distributed occurrence and a mailed notice are kept, which is also how long an occurrence raised again is recognised and not sent twice; default 90 days.</summary>
    public TimeSpan UserNoticeRetention { get; set; } = DefaultUserNoticeRetention;

    /// <summary>
    /// Whether to report, at startup, stored team members that carry no access level and therefore, since
    /// 4.0, have no access to their team. Default <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <b>On by default because the condition it finds changed meaning in 4.0.</b> Before it, a member with no
    /// stored level was silently an Owner; since then they are silently refused. That is the safe direction,
    /// but it can lock out a team's original owner on upgrade, and nothing else in the logs says why.
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