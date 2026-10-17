using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>An unsubscribe link carries one user and one category, and nothing else can be read out of it or put into it.</summary>
public class UserMailUnsubscribeTokenTests
{
    private readonly IDataProtectionProvider _provider = new EphemeralDataProtectionProvider();

    private UserMailUnsubscribeToken Tokens(int version = 1)
        => new(_provider, Options.Create(new UserMailOptions { UnsubscribeTokenVersion = version }));

    [Fact]
    public void AToken_RoundTrips_ToTheUserAndCategoryItWasMadeFor()
    {
        var tokens = Tokens();

        Assert.True(tokens.TryRead(tokens.Create("user-1", "news"), out var user, out var category));
        Assert.Equal("user-1", user);
        Assert.Equal("news", category);
    }

    [Fact]
    public void AToken_IsSafeInAnAddress_AndNamesNobodyInTheClear()
    {
        var token = Tokens().Create("user-1", "news");

        Assert.Equal(token, Uri.EscapeDataString(token));
        Assert.DoesNotContain("user-1", token);
        Assert.DoesNotContain("news", token);
    }

    [Fact]
    public void ATamperedToken_IsRefused()
    {
        var tokens = Tokens();
        var token = tokens.Create("user-1", "news");
        var tampered = token[..10] + (token[10] == 'A' ? 'B' : 'A') + token[11..];

        Assert.False(tokens.TryRead(tampered, out _, out _));
        Assert.False(tokens.TryRead(token[..^4], out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public void SomethingThatIsNoToken_IsRefused(string token)
    {
        Assert.False(Tokens().TryRead(token, out var user, out var category));
        Assert.Null(user);
        Assert.Null(category);
    }

    [Fact]
    public void ATokenFromAnotherPurpose_IsRefused_EvenWithTheSameKeyRing()
    {
        var other = _provider.CreateProtector("Some.Other.Purpose").Protect("""{"v":1,"u":"user-1","c":"news"}""");

        Assert.False(Tokens().TryRead(other, out _, out _));
    }

    [Fact]
    public void ATokenFromAnotherKeyRing_IsRefused()
    {
        var foreign = new UserMailUnsubscribeToken(new EphemeralDataProtectionProvider(), Options.Create(new UserMailOptions())).Create("user-1", "news");

        Assert.False(Tokens().TryRead(foreign, out _, out _));
    }

    [Fact]
    public void ATokenForOneUserAndCategory_NeverReadsAsAnother()
    {
        var tokens = Tokens();

        tokens.TryRead(tokens.Create("user-2", "digest"), out var user, out var category);

        Assert.Equal("user-2", user);
        Assert.Equal("digest", category);
        Assert.NotEqual(tokens.Create("user-1", "news"), tokens.Create("user-2", "news"));
    }

    [Fact]
    public void RaisingTheVersion_InvalidatesEveryTokenAlreadySent()
    {
        var sent = Tokens(version: 1).Create("user-1", "news");

        Assert.False(Tokens(version: 2).TryRead(sent, out _, out _));
        Assert.True(Tokens(version: 1).TryRead(sent, out _, out _));
    }
}
