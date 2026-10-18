namespace Tharga.Team.Email.Users;

/// <summary>How staff write messages to users: the categories they may write in.</summary>
public class UserMessageOptions
{
    /// <summary>The keys of the registered categories staff may write a message in, default the built-in <c>service</c> category.</summary>
    public string[] StaffMessageCategories { get; set; } = [UserMailCategories.ServiceKey];
}
