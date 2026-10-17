using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserMailRecipientDirectory"/>, over the user repository.</summary>
/// <remarks>Reads the repository rather than the user service, because the service needs a signed-in caller and the mail sender has none.</remarks>
internal sealed class MongoUserMailRecipientDirectory<TUserEntity>(IUserRepository<TUserEntity> users) : IUserMailRecipientDirectory
    where TUserEntity : EntityBase, IUser
{
    public async Task<UserMailRecipient> FindAsync(string userKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userKey)) return null;

        var user = await users.GetByKeyAsync(userKey);

        return user == null ? null : new UserMailRecipient
        {
            Key = user.Key,
            EMail = string.IsNullOrWhiteSpace(user.EMail) ? null : user.EMail.Trim(),
            Name = string.IsNullOrWhiteSpace(user.Name) ? null : user.Name
        };
    }
}
