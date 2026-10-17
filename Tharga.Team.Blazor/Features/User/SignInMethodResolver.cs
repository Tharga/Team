using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.User;

/// <summary>The labels used for identities no host mapping claims, so they can be translated.</summary>
public sealed record SignInFallbackLabels(string Email = "Email", string UserName = "Username", string PrincipalName = "Account");

/// <summary>
/// Turns a user's directory identities into the sign-in methods shown in the users list. Separated from the
/// component so the mapping is testable without rendering, matching <see cref="UserAdminGate"/>.
/// </summary>
public static class SignInMethodResolver
{
    /// <summary>
    /// One method per distinct label, in directory order. A mapping that throws is treated as not matching,
    /// so a fault in host code costs one chip rather than the whole list.
    /// </summary>
    public static SignInMethod[] Resolve(IEnumerable<DirectoryUserIdentity> identities, SignInMethodOptions options, SignInFallbackLabels labels = null)
    {
        options ??= new SignInMethodOptions();
        labels ??= new SignInFallbackLabels();

        var result = new List<SignInMethod>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var identity in identities ?? [])
        {
            if (identity == null) continue;

            var method = Map(identity, options) ?? Fallback(identity, options, labels);
            if (method == null || string.IsNullOrWhiteSpace(method.Label)) continue;
            if (seen.Add(method.Label)) result.Add(method);
        }

        return result.ToArray();
    }

    private static SignInMethod Map(DirectoryUserIdentity identity, SignInMethodOptions options)
    {
        foreach (var mapping in options.Mappings)
        {
            try
            {
                var method = mapping(identity);
                if (method != null) return method;
            }
            catch
            {
                // Host code. A mapping that cannot handle an identity has not claimed it.
            }
        }

        return null;
    }

    private static SignInMethod Fallback(DirectoryUserIdentity identity, SignInMethodOptions options, SignInFallbackLabels labels)
        => identity.SignInType switch
        {
            "federated" => new SignInMethod(IssuerName(identity.Issuer)),
            "emailAddress" => new SignInMethod(labels.Email),
            "userName" => new SignInMethod(labels.UserName),
            "userPrincipalName" => options.ShowUserPrincipalName ? new SignInMethod(labels.PrincipalName) : null,
            _ => string.IsNullOrEmpty(identity.SignInType) ? null : new SignInMethod(identity.SignInType)
        };

    /// <summary>The issuer's host name when it is a URL (the usual case for a federated issuer), else the issuer itself.</summary>
    internal static string IssuerName(string issuer)
        => Uri.TryCreate(issuer, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : issuer;
}
