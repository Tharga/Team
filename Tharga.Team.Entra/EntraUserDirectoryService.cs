using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;

namespace Tharga.Team.Entra;

/// <summary>
/// <see cref="IUserDirectoryService"/> over Microsoft Graph. Verification resolves by the stored
/// directory object id when present (a 404 then means the user is gone — no email fallback, since a
/// broken link is a finding, not a lookup miss); unlinked users are matched by mail or UPN, and the
/// found object id is returned so the caller can relink. Enumeration streams pages via
/// <c>@odata.nextLink</c>. Deletion is Graph's org-wide soft delete (30-day restore window). Sign-in
/// identities are read through <see cref="IUserIdentityDirectory"/>.
/// </summary>
public class EntraUserDirectoryService : IUserDirectoryService, IUserIdentityDirectory
{
    private const string SelectFields = "id,displayName,mail,userPrincipalName,accountEnabled";
    private const int PageSize = 999;

    private readonly HttpClient _httpClient;
    private readonly IEntraTokenProvider _tokenProvider;

    public EntraUserDirectoryService(HttpClient httpClient, IEntraTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the token provider, which owns the credentials. Nothing else about this service can
    /// be half-configured — the Graph address and scope both have working defaults.
    /// </remarks>
    public bool IsConfigured => _tokenProvider.IsConfigured;

    public async Task<DirectoryVerificationResult> VerifyUserAsync(IUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!string.IsNullOrEmpty(user.DirectoryId))
        {
            var byId = await GetUserByIdAsync(user.DirectoryId, cancellationToken);
            if (byId == null) return new DirectoryVerificationResult(DirectoryUserStatus.NotFound);
            return new DirectoryVerificationResult(ToStatus(byId), byId.Id);
        }

        if (!string.IsNullOrEmpty(user.EMail))
        {
            var byEmail = await FindUserByEmailAsync(user.EMail, cancellationToken);
            if (byEmail != null) return new DirectoryVerificationResult(ToStatus(byEmail), byEmail.Id);
        }

        return new DirectoryVerificationResult(DirectoryUserStatus.NotLinked);
    }

    public async Task DeleteUserAsync(string directoryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryId);

        using var response = await SendAsync(HttpMethod.Delete, $"users/{Uri.EscapeDataString(directoryId)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Directory user '{directoryId}' was not found.");
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <remarks>
    /// Graph <c>PATCH /users/{id}</c>. Needs no permission beyond the <c>User.ReadWrite.All</c> that
    /// deletion already requires, so a tenant able to delete users can already do this.
    /// </remarks>
    public async Task SetUserNameAsync(string directoryId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryId);
        ArgumentException.ThrowIfNullOrEmpty(name);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"users/{Uri.EscapeDataString(directoryId)}")
        {
            Content = JsonContent.Create(new { displayName = name })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokenProvider.GetTokenAsync(cancellationToken));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Directory user '{directoryId}' was not found.");

        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Graph <c>GET /users/{id}?$select=identities</c>, needing only <c>User.Read.All</c>. An unknown
    /// <paramref name="directoryId"/> throws <see cref="InvalidOperationException"/>, as deletion and renaming do.
    /// </remarks>
    public async Task<IReadOnlyList<DirectoryUserIdentity>> GetIdentitiesAsync(string directoryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryId);

        using var response = await SendAsync(HttpMethod.Get, $"users/{Uri.EscapeDataString(directoryId)}?$select=identities", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Directory user '{directoryId}' was not found.");

        await EnsureSuccessAsync(response, cancellationToken);

        var graphUser = await response.Content.ReadFromJsonAsync<GraphUserIdentities>(cancellationToken);
        return (graphUser?.Identities ?? []).Select(x => x.ToDirectoryUserIdentity()).ToArray();
    }

    public async IAsyncEnumerable<DirectoryUser> GetUsersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = $"users?$select={SelectFields}&$top={PageSize}";
        while (url != null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<GraphUserPage>(cancellationToken);
            foreach (var graphUser in page?.Value ?? [])
            {
                yield return graphUser.ToDirectoryUser();
            }

            url = page?.NextLink;
        }
    }

    private async Task<GraphUser> GetUserByIdAsync(string directoryId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"users/{Uri.EscapeDataString(directoryId)}?$select={SelectFields}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GraphUser>(cancellationToken);
    }

    private async Task<GraphUser> FindUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var escaped = email.Replace("'", "''");
        var filter = Uri.EscapeDataString($"mail eq '{escaped}' or userPrincipalName eq '{escaped}'");

        using var response = await SendAsync(HttpMethod.Get, $"users?$filter={filter}&$select={SelectFields}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var page = await response.Content.ReadFromJsonAsync<GraphUserPage>(cancellationToken);
        return page?.Value?.FirstOrDefault();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokenProvider.GetTokenAsync(cancellationToken));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Microsoft Graph request failed with {(int)response.StatusCode} {response.StatusCode}: {Truncate(body)}",
            null, response.StatusCode);
    }

    private static string Truncate(string value)
        => string.IsNullOrEmpty(value) ? "(no body)" : value.Length <= 500 ? value : value[..500];

    private static DirectoryUserStatus ToStatus(GraphUser graphUser)
        => graphUser.AccountEnabled == false ? DirectoryUserStatus.Disabled : DirectoryUserStatus.Found;
}
