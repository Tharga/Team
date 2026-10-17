using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// <see cref="ApiKeyOptions.TeamKeys"/> decides whether a new team key may, or must, be personal
/// (Tharga/Team#314). Enforced in the service, so a caller skipping the create card gets the same answer.
/// </summary>
public class ApiKeyOwnershipTests
{
    private const string Team = "team-1";

    private static (ApiKeyManagementService Sut, IApiKeyAdministrationService Inner) Build(ApiKeyOwnership ownership, string memberKey = "m1")
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();

        var claims = new List<Claim>();
        if (memberKey != null) claims.Add(new Claim(TeamClaimTypes.MemberKey, memberKey));
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) });

        var options = Options.Create(new ApiKeyOptions { TeamKeys = ownership });
        return (new ApiKeyManagementService(inner, accessor, options), inner);
    }

    private static Task CreateAsync(ApiKeyManagementService sut, bool ownerScoped)
        => sut.CreateKeyAsync(Team, "k", AccessLevel.User, ownerScoped: ownerScoped);

    private static Task ReceivedCreate(IApiKeyAdministrationService inner, string ownerMemberKey)
        => inner.Received(1).CreateKeyAsync(Team, "k", AccessLevel.User, Arg.Any<string[]>(), Arg.Any<string[]>(), Arg.Any<DateTime?>(), Arg.Any<IReadOnlyList<Tag>>(), Arg.Any<string>(), ownerMemberKey);

    [Fact]
    public void BothKinds_AreAllowedByDefault()
    {
        var options = new ApiKeyOptions();

        Assert.Equal(ApiKeyOwnership.Both, options.TeamKeys);
        Assert.Equal(ApiKeyOwnership.Both, options.SystemKeys);
    }

    [Theory]
    [InlineData(true, "m1")]
    [InlineData(false, null)]
    public async Task Both_KeepsWhatTheCallerAskedFor(bool ownerScoped, string expectedOwner)
    {
        var (sut, inner) = Build(ApiKeyOwnership.Both);

        await CreateAsync(sut, ownerScoped);

        await ReceivedCreate(inner, expectedOwner);
    }

    [Fact]
    public async Task Shared_RefusesAPersonalKey()
    {
        var (sut, inner) = Build(ApiKeyOwnership.Shared);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(sut, ownerScoped: true));

        await inner.DidNotReceiveWithAnyArgs().CreateKeyAsync(default, default, default);
    }

    [Fact]
    public async Task Shared_CreatesASharedKey()
    {
        var (sut, inner) = Build(ApiKeyOwnership.Shared);

        await CreateAsync(sut, ownerScoped: false);

        await ReceivedCreate(inner, null);
    }

    [Fact]
    public async Task Personal_MakesEveryKeyPersonal()
    {
        var (sut, inner) = Build(ApiKeyOwnership.Personal);

        await CreateAsync(sut, ownerScoped: false);

        await ReceivedCreate(inner, "m1");
    }

    /// <summary>Forced personal still means owned by the caller, so a caller with no member context gets nothing.</summary>
    [Fact]
    public async Task Personal_WithNoMemberContext_IsRefused()
    {
        var (sut, inner) = Build(ApiKeyOwnership.Personal, memberKey: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(sut, ownerScoped: false));

        await inner.DidNotReceiveWithAnyArgs().CreateKeyAsync(default, default, default);
    }

    [Fact]
    public async Task NoOptions_BehavesAsBoth()
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamClaimTypes.MemberKey, "m1")], "test")) });
        var sut = new ApiKeyManagementService(inner, accessor);

        await CreateAsync(sut, ownerScoped: true);

        await ReceivedCreate(inner, "m1");
    }
}
