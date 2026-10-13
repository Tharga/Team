using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Every <see cref="IUserManagementService"/> operation requires the <c>users:manage</c> system scope —
/// with it the call delegates, without it (wrong scope or anonymous) it throws before touching the inner
/// service, including the streaming operations.
/// </summary>
public class AuthorizationUserManagementServiceDecoratorTests
{
    private static (AuthorizationUserManagementServiceDecorator Sut, IUserManagementService Inner) Build(ClaimsPrincipal principal)
    {
        var inner = Substitute.For<IUserManagementService>();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));
        return (new AuthorizationUserManagementServiceDecorator(inner, new TeamAuthorizer(accessor)), inner);
    }

    private static ClaimsPrincipal WithScopes(params string[] scopes)
        => new(new ClaimsIdentity(scopes.Select(s => new Claim(TeamClaimTypes.SystemScope, s)), "Test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    [Fact]
    public async Task Verify_WithScope_Delegates()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        await sut.VerifyUserAsync("u-1");
        await inner.Received(1).VerifyUserAsync("u-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Verify_WithoutScope_Throws()
    {
        var (sut, inner) = Build(WithScopes(TeamScopes.Manage));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.VerifyUserAsync("u-1"));
        await inner.DidNotReceiveWithAnyArgs().VerifyUserAsync(default, default);
    }

    [Fact]
    public async Task VerifyAll_WithScope_Streams()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        inner.VerifyAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new UserVerificationResult("u-1", new DirectoryVerificationResult(DirectoryUserStatus.Found, "oid"))
        }.ToAsyncEnumerable());

        var results = await sut.VerifyAllAsync().ToListAsync();

        Assert.Single(results);
    }

    [Fact]
    public async Task VerifyAll_WithoutScope_Throws()
    {
        var (sut, inner) = Build(Anonymous());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await sut.VerifyAllAsync().ToListAsync());
        _ = inner.DidNotReceiveWithAnyArgs().VerifyAllAsync(default);
    }

    [Fact]
    public async Task Delete_WithScope_Delegates()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        await sut.DeleteUserAsync("u-1", deleteFromDirectory: true);
        await inner.Received(1).DeleteUserAsync("u-1", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_WithoutScope_Throws()
    {
        var (sut, inner) = Build(Anonymous());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DeleteUserAsync("u-1"));
        await inner.DidNotReceiveWithAnyArgs().DeleteUserAsync(default, default, default);
    }

    [Fact]
    public async Task SetUserEmail_WithScope_Delegates()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        var expected = new UserEmailChangeResult("old@example.com");
        inner.SetUserEmailAsync("u-1", "new@example.com", Arg.Any<CancellationToken>()).Returns(expected);

        var result = await sut.SetUserEmailAsync("u-1", "new@example.com");

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task SetUserEmail_WithoutScope_Throws()
    {
        var (sut, inner) = Build(WithScopes(TeamScopes.Manage));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetUserEmailAsync("u-1", "new@example.com"));
        await inner.DidNotReceiveWithAnyArgs().SetUserEmailAsync(default, default, default);
    }

    [Fact]
    public async Task DirectoryOnly_WithScope_Streams()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        inner.GetDirectoryOnlyUsersAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new DirectoryUser("oid-1", "N", "e@x.y", true) }.ToAsyncEnumerable());

        var results = await sut.GetDirectoryOnlyUsersAsync().ToListAsync();

        Assert.Single(results);
    }

    [Fact]
    public async Task DirectoryOnly_WithoutScope_Throws()
    {
        var (sut, inner) = Build(WithScopes(SystemTeamScopes.Read));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await sut.GetDirectoryOnlyUsersAsync().ToListAsync());
        _ = inner.DidNotReceiveWithAnyArgs().GetDirectoryOnlyUsersAsync(default);
    }

    [Fact]
    public async Task SignInIdentities_WithScope_Delegates()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        var expected = new[] { new UserSignInIdentities("u-1", []) };
        inner.GetSignInIdentitiesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await sut.GetSignInIdentitiesAsync(["u-1"]);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task SignInIdentities_WithoutScope_Throws()
    {
        var (sut, inner) = Build(WithScopes(SystemTeamScopes.Read));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetSignInIdentitiesAsync(["u-1"]));
        await inner.DidNotReceiveWithAnyArgs().GetSignInIdentitiesAsync(default, default);
    }

    [Fact]
    public async Task SignInIdentities_Anonymous_Throws()
    {
        var (sut, inner) = Build(Anonymous());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetSignInIdentitiesAsync(["u-1"]));
        await inner.DidNotReceiveWithAnyArgs().GetSignInIdentitiesAsync(default, default);
    }

    [Fact]
    public async Task UseDirectoryName_WithScope_Delegates()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        await sut.UseDirectoryNameAsync("u-1");
        await inner.Received(1).UseDirectoryNameAsync("u-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseDirectoryName_WithoutScope_Throws()
    {
        var (sut, inner) = Build(WithScopes(TeamScopes.Manage));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.UseDirectoryNameAsync("u-1"));
        await inner.DidNotReceiveWithAnyArgs().UseDirectoryNameAsync(default, default);
    }

    [Fact]
    public async Task WriteNameToDirectory_WithScope_Delegates()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        await sut.WriteNameToDirectoryAsync("u-1");
        await inner.Received(1).WriteNameToDirectoryAsync("u-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteNameToDirectory_WithoutScope_Throws()
    {
        var (sut, inner) = Build(Anonymous());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.WriteNameToDirectoryAsync("u-1"));
        await inner.DidNotReceiveWithAnyArgs().WriteNameToDirectoryAsync(default, default);
    }
}
