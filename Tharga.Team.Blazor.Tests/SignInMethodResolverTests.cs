using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// How directory identities become the sign-in chips in the users list: host mappings first, in order;
/// then the fallbacks (federated → issuer host, email → "Email", UPN hidden by default); one chip per label.
/// </summary>
public class SignInMethodResolverTests
{
    private static readonly DirectoryUserIdentity Email = new("emailAddress", "contoso.onmicrosoft.com", "a@b.c");
    private static readonly DirectoryUserIdentity Upn = new("userPrincipalName", "contoso.onmicrosoft.com", "oid@contoso.onmicrosoft.com");
    private static readonly DirectoryUserIdentity Broker = new("federated", "https://broker.example/oidc", "sub-1");
    private static readonly DirectoryUserIdentity Saml = new("federated", "https://idp.partner.test/saml", "sub-2");

    private static string[] Labels(params DirectoryUserIdentity[] identities) => Labels(new SignInMethodOptions(), identities);

    private static string[] Labels(SignInMethodOptions options, params DirectoryUserIdentity[] identities)
        => SignInMethodResolver.Resolve(identities, options).Select(x => x.Label).ToArray();

    [Fact]
    public void NoMapping_FallsBack_InDirectoryOrder_HidingUpn()
    {
        Assert.Equal(["Email", "broker.example", "idp.partner.test"], Labels(Email, Upn, Broker, Saml));
    }

    [Fact]
    public void ShowUserPrincipalName_ShowsIt()
    {
        var options = new SignInMethodOptions { ShowUserPrincipalName = true };
        Assert.Equal(["Account"], Labels(options, Upn));
    }

    [Fact]
    public void HostMapping_NamesTheProvider_WithIcon()
    {
        var options = new SignInMethodOptions()
            .Add(x => x.Issuer.StartsWith("https://broker.example/") ? new SignInMethod("e-ID", "/images/eid.svg") : null);

        var methods = SignInMethodResolver.Resolve([Email, Broker], options);

        Assert.Equal(new SignInMethod("Email"), methods[0]);
        Assert.Equal(new SignInMethod("e-ID", "/images/eid.svg"), methods[1]);
    }

    [Fact]
    public void FirstMatchingMapping_Wins()
    {
        var options = new SignInMethodOptions()
            .Add(x => x.SignInType == "federated" ? new SignInMethod("First") : null)
            .Add(_ => new SignInMethod("Second"));

        Assert.Equal(["First"], Labels(options, Broker));
    }

    [Fact]
    public void HostMapping_CanClaimUpn()
    {
        var options = new SignInMethodOptions().Add(x => x.SignInType == "userPrincipalName" ? new SignInMethod("Staff") : null);
        Assert.Equal(["Staff"], Labels(options, Upn));
    }

    [Fact]
    public void DuplicateLabels_Collapse()
    {
        var options = new SignInMethodOptions().Add(x => x.SignInType == "federated" ? new SignInMethod("SSO") : null);
        Assert.Equal(["SSO"], Labels(options, Broker, Saml));
    }

    [Fact]
    public void ThrowingMapping_IsTreatedAsNoMatch()
    {
        var options = new SignInMethodOptions().Add(_ => throw new InvalidOperationException("host bug"));
        Assert.Equal(["Email"], Labels(options, Email));
    }

    [Fact]
    public void IssuerThatIsNotAUrl_IsShownAsIs()
    {
        Assert.Equal(["legacy-idp"], Labels(new DirectoryUserIdentity("federated", "legacy-idp", "x")));
    }

    [Fact]
    public void UnknownSignInType_ShowsTheType()
    {
        Assert.Equal(["phoneNumber"], Labels(new DirectoryUserIdentity("phoneNumber", "contoso", "+46")));
    }

    [Fact]
    public void FallbackLabels_AreReplaceable()
    {
        var methods = SignInMethodResolver.Resolve([Email], new SignInMethodOptions(), new SignInFallbackLabels(Email: "E-post"));
        Assert.Equal("E-post", Assert.Single(methods).Label);
    }

    [Fact]
    public void NoIdentities_IsEmpty()
    {
        Assert.Empty(SignInMethodResolver.Resolve(null, null));
    }
}
