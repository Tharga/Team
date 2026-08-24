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