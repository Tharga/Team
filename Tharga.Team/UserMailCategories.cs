namespace Tharga.Team;

/// <summary>The two built-in categories every host has, so mail the toolkit itself sends always has a category to go in.</summary>
/// <remarks>
/// Their keys are reserved: a host registers its own categories beside them and cannot redefine them. Account can never
/// be turned off, so no choice is ever stored for it and its mail carries no unsubscribe link. Service messages are
/// required too, unless the host sets <c>UserMailOptions.ServiceRequired</c> to false: then each user may turn them off,
/// on until they do, and their mail carries an unsubscribe link like any optional category's.
/// </remarks>
public static class UserMailCategories
{
    /// <summary>The key of <see cref="Account"/>.</summary>
    public const string AccountKey = "account";

    /// <summary>The key of <see cref="Service"/>.</summary>
    public const string ServiceKey = "service";

    /// <summary>Mail about the user's own account, access and its security.</summary>
    public static UserMailCategory Account { get; } = new()
    {
        Key = AccountKey,
        Name = "Account and security",
        NameKey = "team.userMail.category.account.name",
        Description = "Changes to your account and your access, and anything that affects their security.",
        DescriptionKey = "team.userMail.category.account.description",
        Required = true
    };

    /// <summary>Mail every user needs about the service, such as planned maintenance or changed terms.</summary>
    public static UserMailCategory Service { get; } = new()
    {
        Key = ServiceKey,
        Name = "Service messages",
        NameKey = "team.userMail.category.service.name",
        Description = "What you need to know about the service, such as planned maintenance and changes to the terms.",
        DescriptionKey = "team.userMail.category.service.description",
        Required = true
    };

    /// <summary>The built-in categories, in the order they are listed before a host's own.</summary>
    public static IReadOnlyList<UserMailCategory> BuiltIn { get; } = [Account, Service];
}
