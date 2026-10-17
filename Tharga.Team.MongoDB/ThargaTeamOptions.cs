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

    /// <summary>
    /// How long a handled event is remembered. Default 24 hours.
    /// </summary>
    /// <remarks>
    /// Only has to outlast the channel's own retry window - Slack gives up long before a day - so this is
    /// generous. Entries expire by TTL index rather than accumulating.
    /// </remarks>
    public TimeSpan SupportEventLedgerRetention { get; set; } = TimeSpan.FromHours(24);

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