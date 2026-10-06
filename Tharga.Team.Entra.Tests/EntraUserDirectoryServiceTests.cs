using System.Net;
using System.Text;
using Tharga.Team;
using Tharga.Team.Entra;

namespace Tharga.Team.Entra.Tests;

/// <summary>
/// Graph-protocol behavior of <see cref="EntraUserDirectoryService"/> against a scripted HTTP handler:
/// verify by object id (404 = NotFound, no email fallback), verify by email (found id returned for
/// relink, OData quote escaping), delete (404 vs other failures), and paged enumeration via
/// <c>@odata.nextLink</c>.
/// </summary>
public class EntraUserDirectoryServiceTests
{
    private const string BaseAddress = "https://graph.test/v1.0/";

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string Authorization);

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string json = null)
        {
            var response = new HttpResponseMessage(status);
            if (json != null) response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            _responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private static (EntraUserDirectoryService Sut, ScriptedHandler Handler) Build()
    {
        var handler = new ScriptedHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) };
        var tokenProvider = Substitute.For<IEntraTokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<string>("token-123"));
        return (new EntraUserDirectoryService(httpClient, tokenProvider), handler);
    }

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
        public string DirectoryId { get; init; }
    }

    // ---- VerifyUserAsync by directory id ----

    [Fact]
    public async Task Verify_ById_FoundAndEnabled()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"oid-1","displayName":"A","accountEnabled":true}""");

        var result = await sut.VerifyUserAsync(new TestUser { Key = "u", DirectoryId = "oid-1" });

        Assert.Equal(DirectoryUserStatus.Found, result.Status);
        Assert.Equal("oid-1", result.DirectoryId);
        var request = Assert.Single(handler.Requests);
        Assert.StartsWith($"{BaseAddress}users/oid-1?$select=", request.Uri);
        Assert.Equal("Bearer token-123", request.Authorization);
    }

    [Fact]
    public async Task Verify_ById_Disabled()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"oid-1","accountEnabled":false}""");

        var result = await sut.VerifyUserAsync(new TestUser { Key = "u", DirectoryId = "oid-1" });

        Assert.Equal(DirectoryUserStatus.Disabled, result.Status);
    }

    [Fact]
    public async Task Verify_ById_Gone_ReportsNotFoundWithoutEmailFallback()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound);

        var result = await sut.VerifyUserAsync(new TestUser { Key = "u", DirectoryId = "oid-1", EMail = "a@b.c" });

        Assert.Equal(DirectoryUserStatus.NotFound, result.Status);
        Assert.Single(handler.Requests);
    }

    // ---- VerifyUserAsync by email fallback ----

    [Fact]
    public async Task Verify_ByEmail_Found_ReturnsIdForRelink()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"value":[{"id":"oid-9","displayName":"A","accountEnabled":true}]}""");

        var result = await sut.VerifyUserAsync(new TestUser { Key = "u", EMail = "a@b.c" });

        Assert.Equal(DirectoryUserStatus.Found, result.Status);
        Assert.Equal("oid-9", result.DirectoryId);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("$filter=", request.Uri);
        Assert.Contains("userPrincipalName", request.Uri);
    }

    [Fact]
    public async Task Verify_ByEmail_NoMatch_NotLinked()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"value":[]}""");

        var result = await sut.VerifyUserAsync(new TestUser { Key = "u", EMail = "a@b.c" });

        Assert.Equal(DirectoryUserStatus.NotLinked, result.Status);
    }

    [Fact]
    public async Task Verify_EmailWithQuote_EscapesODataFilter()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"value":[]}""");

        await sut.VerifyUserAsync(new TestUser { Key = "u", EMail = "o'brien@b.c" });

        var request = Assert.Single(handler.Requests);
        Assert.Contains("o%27%27brien", request.Uri);
    }

    [Fact]
    public async Task Verify_NoIdAndNoEmail_NotLinkedWithoutRequest()
    {
        var (sut, handler) = Build();

        var result = await sut.VerifyUserAsync(new TestUser { Key = "u" });

        Assert.Equal(DirectoryUserStatus.NotLinked, result.Status);
        Assert.Empty(handler.Requests);
    }

    // ---- DeleteUserAsync ----

    [Fact]
    public async Task Delete_Success()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.NoContent);

        await sut.DeleteUserAsync("oid-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{BaseAddress}users/oid-1", request.Uri);
    }

    [Fact]
    public async Task Delete_UnknownUser_Throws()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DeleteUserAsync("oid-gone"));
    }

    [Fact]
    public async Task Delete_Forbidden_ThrowsWithStatus()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.Forbidden, """{"error":{"message":"Insufficient privileges"}}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => sut.DeleteUserAsync("oid-1"));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Contains("Insufficient privileges", ex.Message);
    }

    // ---- GetUsersAsync ----

    [Fact]
    public async Task GetUsers_MapsFieldsAndFallsBackToUpn()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK,
            """{"value":[{"id":"o1","displayName":"A","mail":"a@b.c","accountEnabled":true},{"id":"o2","displayName":"B","userPrincipalName":"b@b.c","accountEnabled":false}]}""");

        var users = await sut.GetUsersAsync().ToListAsync();

        Assert.Equal(2, users.Count);
        Assert.Equal("a@b.c", users[0].EMail);
        Assert.Equal("b@b.c", users[1].EMail);
        Assert.False(users[1].Enabled);
    }

    [Fact]
    public async Task GetUsers_FollowsNextLinkAcrossPages()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK,
            $$"""{"value":[{"id":"o1"}],"@odata.nextLink":"{{BaseAddress}}users?$skiptoken=abc"}""");
        handler.Enqueue(HttpStatusCode.OK, """{"value":[{"id":"o2"}]}""");

        var users = await sut.GetUsersAsync().ToListAsync();

        Assert.Equal(["o1", "o2"], users.Select(x => x.DirectoryId));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("$skiptoken=abc", handler.Requests[1].Uri);
    }

    [Fact]
    public async Task GetUsers_ServerError_Throws()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.InternalServerError, "boom");

        await Assert.ThrowsAsync<HttpRequestException>(async () => await sut.GetUsersAsync().ToListAsync());
    }

    // ---- GetIdentitiesAsync ----

    [Fact]
    public async Task GetIdentities_ParsesEveryIdentityInOrder()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK,
            """{"id":"oid-1","identities":[{"signInType":"emailAddress","issuer":"contoso.onmicrosoft.com","issuerAssignedId":"a@b.c"},{"signInType":"federated","issuer":"https://legacy.example/","issuerAssignedId":"sub-42"},{"signInType":"userPrincipalName","issuer":"contoso.onmicrosoft.com","issuerAssignedId":"oid-1@contoso.onmicrosoft.com"}]}""");

        var identities = await sut.GetIdentitiesAsync("oid-1");

        Assert.Equal(["emailAddress", "federated", "userPrincipalName"], identities.Select(x => x.SignInType));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{BaseAddress}users/oid-1?$select=identities", request.Uri);
        Assert.Equal("Bearer token-123", request.Authorization);
    }

    [Fact]
    public async Task GetIdentities_MapsFederatedIdentityFields()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK,
            """{"identities":[{"signInType":"federated","issuer":"https://legacy.example/","issuerAssignedId":"sub-42"}]}""");

        var identities = await sut.GetIdentitiesAsync("oid-1");

        Assert.Equal(new DirectoryUserIdentity("federated", "https://legacy.example/", "sub-42"), Assert.Single(identities));
    }

    [Theory]
    [InlineData("""{"identities":[]}""")]
    [InlineData("""{"identities":null}""")]
    [InlineData("""{"id":"oid-1"}""")]
    public async Task GetIdentities_NoIdentities_ReturnsEmpty(string json)
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, json);

        var identities = await sut.GetIdentitiesAsync("oid-1");

        Assert.Empty(identities);
    }

    [Fact]
    public async Task GetIdentities_UnknownUser_Throws()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetIdentitiesAsync("oid-gone"));

        Assert.Contains("oid-gone", ex.Message);
    }

    [Fact]
    public async Task GetIdentities_Forbidden_ThrowsWithStatus()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.Forbidden, """{"error":{"message":"Insufficient privileges"}}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetIdentitiesAsync("oid-1"));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Contains("Insufficient privileges", ex.Message);
    }

    [Fact]
    public async Task GetIdentities_EscapesDirectoryId()
    {
        var (sut, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"identities":[]}""");

        await sut.GetIdentitiesAsync("a/b?c#d");

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith($"{BaseAddress}users/a%2Fb%3Fc%23d?$select=identities", request.Uri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetIdentities_MissingDirectoryId_ThrowsWithoutRequest(string directoryId)
    {
        var (sut, handler) = Build();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => sut.GetIdentitiesAsync(directoryId));

        Assert.Empty(handler.Requests);
    }
}
