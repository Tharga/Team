namespace Tharga.Team.Blazor.Features.User;

/// <summary>How a <see cref="SignInFilter"/> selects users.</summary>
internal enum SignInFilterKind
{
    /// <summary>Users with no directory link.</summary>
    NotLinked,

    /// <summary>Users who sign in with a method.</summary>
    Has,

    /// <summary>Users whose sign-in methods were read and do not include a method.</summary>
    Lacks
}

/// <summary>
/// Which users the list shows by how they sign in. Separated from the component so the rules are testable
/// without rendering, as <see cref="UserAdminGate"/> is.
/// </summary>
/// <remarks>
/// <b>"Has" and "lacks" say something only about users whose methods were read.</b> A user with no directory
/// link has no methods the directory can report, and one whose read failed has none known, so neither matches
/// either: saying such a user "does not sign in with e-ID" would be a claim nothing supports. The unlinked are
/// found with <see cref="NotLinked"/>, and a failed read shows on the row.
/// </remarks>
internal sealed record SignInFilter(SignInFilterKind Kind, string Method = null)
{
    /// <summary>Users with no directory link.</summary>
    public static SignInFilter NotLinked { get; } = new(SignInFilterKind.NotLinked);

    /// <summary>Users who sign in with <paramref name="method"/>, named as the list shows it.</summary>
    public static SignInFilter Has(string method) => new(SignInFilterKind.Has, method);

    /// <summary>Users whose methods were read and do not include <paramref name="method"/>.</summary>
    public static SignInFilter Lacks(string method) => new(SignInFilterKind.Lacks, method);

    /// <summary>Whether <paramref name="user"/> is one the filter selects.</summary>
    public bool Matches(UserViewModel user)
    {
        if (user == null) return false;

        return Kind switch
        {
            SignInFilterKind.NotLinked => string.IsNullOrEmpty(user.DirectoryId),
            SignInFilterKind.Has => IsRead(user) && Signs(user, Method),
            SignInFilterKind.Lacks => IsRead(user) && !Signs(user, Method),
            _ => false
        };
    }

    /// <summary>The methods the users sign in with, each once, in the order a person would look for them.</summary>
    public static string[] MethodsOf(IEnumerable<UserViewModel> users)
        => [.. (users ?? [])
            .Where(IsRead)
            .SelectMany(x => x.SignInMethods)
            .Select(x => x.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)];

    private static bool IsRead(UserViewModel user)
        => user != null && !string.IsNullOrEmpty(user.DirectoryId) && user.SignInError == null && user.SignInMethods != null;

    private static bool Signs(UserViewModel user, string method)
        => user.SignInMethods.Any(x => string.Equals(x.Label, method, StringComparison.OrdinalIgnoreCase));
}
