using System.Security.Claims;

namespace Tharga.Team.Service.Tests;

public class UserIdentityResolverTests
{
    private const string Sub = "pairwise-sub";
    private const string Oid = "tenant-oid";

    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "test"));

    private static ClaimsPrincipal MappedEntraPrincipal()
        => Principal(new Claim(ClaimTypes.NameIdentifier, Sub), new Claim(DirectoryClaimTypes.ObjectIdentifier, Oid));

    private static ClaimsPrincipal UnmappedEntraPrincipal()
        => Principal(new Claim("sub", Sub), new Claim(DirectoryClaimTypes.ObjectId, Oid));

    [Fact]
    public void Default_IsNotConfigured()
    {
        Assert.False(UserIdentityResolver.Default.IsConfigured);
        Assert.Empty(UserIdentityResolver.Default.ClaimTypes);
    }

    [Fact]
    public void Default_UserIdentity_PrefersNameIdentifierOverOid()
    {
        Assert.Equal(Sub, UserIdentityResolver.Default.GetUserIdentity(MappedEntraPrincipal()));
    }

    [Fact]
    public void Default_UserIdentity_PrefersSubOverOid()
    {
        Assert.Equal(Sub, UserIdentityResolver.Default.GetUserIdentity(UnmappedEntraPrincipal()));
    }

    [Fact]
    public void Default_UserIdentity_FallsBackToOidWhenNothingEarlierInTheChain()
    {
        var principal = Principal(new Claim(DirectoryClaimTypes.ObjectId, Oid));

        Assert.Equal(Oid, UserIdentityResolver.Default.GetUserIdentity(principal));
    }

    [Fact]
    public void Default_Subject_IsNameIdentifierOnly()
    {
        Assert.Equal(Sub, UserIdentityResolver.Default.GetSubject(MappedEntraPrincipal()));
        Assert.Null(UserIdentityResolver.Default.GetSubject(UnmappedEntraPrincipal()));
    }

    [Fact]
    public void Default_NullPrincipal_ResolvesToNull()
    {
        Assert.Null(UserIdentityResolver.Default.GetUserIdentity(null));
        Assert.Null(UserIdentityResolver.Default.GetSubject(null));
    }

    [Fact]
    public void EmptyList_IsTheDefault()
    {
        var sut = new UserIdentityResolver([]);

        Assert.False(sut.IsConfigured);
        Assert.Equal(Sub, sut.GetUserIdentity(MappedEntraPrincipal()));
    }

    [Theory]
    [InlineData(DirectoryClaimTypes.ObjectId)]
    [InlineData(DirectoryClaimTypes.ObjectIdentifier)]
    public void ConfiguredOid_IsChosenOverSub_WithInboundMappingOn(string configured)
    {
        var sut = new UserIdentityResolver([configured]);

        Assert.Equal(Oid, sut.GetUserIdentity(MappedEntraPrincipal()));
        Assert.Equal(Oid, sut.GetSubject(MappedEntraPrincipal()));
    }

    [Theory]
    [InlineData(DirectoryClaimTypes.ObjectId)]
    [InlineData(DirectoryClaimTypes.ObjectIdentifier)]
    public void ConfiguredOid_IsChosenOverSub_WithInboundMappingOff(string configured)
    {
        var sut = new UserIdentityResolver([configured]);

        Assert.Equal(Oid, sut.GetUserIdentity(UnmappedEntraPrincipal()));
        Assert.Equal(Oid, sut.GetSubject(UnmappedEntraPrincipal()));
    }

    [Fact]
    public void Configured_HasNoFallbackToTheDefaultChain()
    {
        var sut = new UserIdentityResolver([DirectoryClaimTypes.ObjectId]);
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, Sub));

        Assert.Null(sut.GetUserIdentity(principal));
        Assert.Null(sut.GetSubject(principal));
    }

    [Fact]
    public void Configured_ListedTypesAreTriedInOrder()
    {
        var sut = new UserIdentityResolver([DirectoryClaimTypes.ObjectId, ClaimTypes.NameIdentifier]);

        Assert.Equal(Oid, sut.GetUserIdentity(MappedEntraPrincipal()));
        Assert.Equal(Sub, sut.GetUserIdentity(Principal(new Claim(ClaimTypes.NameIdentifier, Sub))));
    }

    [Fact]
    public void Configured_EmptyClaimValue_IsSkipped()
    {
        var sut = new UserIdentityResolver([DirectoryClaimTypes.ObjectId, ClaimTypes.NameIdentifier]);
        var principal = Principal(new Claim(DirectoryClaimTypes.ObjectId, " "), new Claim(ClaimTypes.NameIdentifier, Sub));

        Assert.Equal(Sub, sut.GetUserIdentity(principal));
    }

    [Fact]
    public void Configured_ClaimTypeMatchIgnoresCase()
    {
        var sut = new UserIdentityResolver(["OID"]);

        Assert.Equal(Oid, sut.GetUserIdentity(UnmappedEntraPrincipal()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Configured_BlankClaimType_Throws(string claimType)
    {
        Assert.Throws<ArgumentException>(() => new UserIdentityResolver([claimType]));
    }
}
