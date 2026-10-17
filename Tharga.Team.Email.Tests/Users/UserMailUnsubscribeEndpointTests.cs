using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>The unsubscribe link: GET asks and changes nothing, POST turns the one category off, and a mail provider's one-click POST works with no sign-in.</summary>
public sealed class UserMailUnsubscribeEndpointTests : IAsyncLifetime
{
    private const string Path = "/mail/unsubscribe";

    private readonly UserMailFixture _fixture = new();
    private ITextResolver _texts;
    private WebApplication _app;
    private HttpClient _client;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app != null) await _app.DisposeAsync();
    }

    private async Task<HttpClient> ClientAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_fixture.DataProtection);
        builder.Services.AddSingleton<IUserMailPreferenceStore>(_fixture.Store);
        builder.Services.AddSingleton<IUserMailOutbox>(_fixture.Outbox);
        builder.Services.AddSingleton<IUserMailRecipientDirectory>(_fixture.Recipients);
        builder.Services.AddSingleton(new CompositeAuditLogger([_fixture.Audit], Options.Create(new AuditOptions())));
        builder.Services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));
        if (_texts != null) builder.Services.AddSingleton(_texts);
        builder.Services.AddAntiforgery();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            foreach (var category in _fixture.Options.Categories) o.AddCategory(category);
        });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseAntiforgery();
        _app.MapThargaMailUnsubscribe();
        await _app.StartAsync();

        return _client = _app.GetTestClient();
    }

    private string Token(string userKey, string category)
        => new UserMailUnsubscribeToken(_fixture.DataProtection, Options.Create(new UserMailOptions())).Create(userKey, category);

    private string Link(string category = UserMailFixture.News) => $"{Path}?token={Uri.EscapeDataString(Token(UserMailFixture.UserKey, category))}";

    [Fact]
    public async Task Get_AsksFirst_AndChangesNothing()
    {
        var client = await ClientAsync();

        var response = await client.GetAsync(Link());
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Stop receiving News?", page);
        Assert.Contains("What is new in the product.", page);
        Assert.Contains("<form method=\"post\"><button type=\"submit\">Unsubscribe</button></form>", page);
        Assert.Equal(0, _fixture.Store.Writes);
        Assert.Empty(_fixture.Audit.Entries);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GettingTheLinkAgainAndAgain_AsAScannerWould_StillChangesNothing()
    {
        var client = await ClientAsync();

        for (var i = 0; i < 3; i++) await client.GetAsync(Link());

        Assert.Empty(_fixture.Store.Stored);
    }

    [Fact]
    public async Task Post_TurnsTheOneCategoryOff_AndSaysSo()
    {
        var client = await ClientAsync();

        var response = await client.PostAsync(Link(), new FormUrlEncodedContent([]));
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("You will no longer receive News.", page);
        Assert.DoesNotContain("<form", page);
        Assert.False(_fixture.Store.Stored[(UserMailFixture.UserKey, UserMailFixture.News)]);
        Assert.Single(_fixture.Store.Stored);
    }

    [Fact]
    public async Task Post_IsAuditedAsAnUnsubscribe()
    {
        var client = await ClientAsync();

        await client.PostAsync(Link(), new FormUrlEncodedContent([]));

        var entry = Assert.Single(_fixture.Audit.Entries);
        Assert.Equal(UserMailAudit.UnsubscribeAction, entry.Action);
        Assert.Equal(UserMailFixture.UserKey, entry.Metadata[UserMailAudit.UserKey]);
        Assert.Equal(UserMailFixture.News, entry.Metadata[UserMailAudit.Category]);
    }

    [Fact]
    public async Task AOneClickPost_AsRfc8058Describes_Unsubscribes_WithoutSignInOrAntiforgeryToken()
    {
        var client = await ClientAsync();

        var response = await client.PostAsync(Link(), new FormUrlEncodedContent([new KeyValuePair<string, string>("List-Unsubscribe", "One-Click")]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(_fixture.Store.Stored[(UserMailFixture.UserKey, UserMailFixture.News)]);
    }

    [Fact]
    public async Task UnsubscribingTwice_LeavesTheCategoryOff()
    {
        var client = await ClientAsync();

        await client.PostAsync(Link(), new FormUrlEncodedContent([]));
        var second = await client.PostAsync(Link(), new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(_fixture.Store.Stored[(UserMailFixture.UserKey, UserMailFixture.News)]);
        Assert.Single(_fixture.Audit.Entries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?token=")]
    [InlineData("?token=forged")]
    public async Task ALinkThatIsNotValid_IsRefused_AndChangesNothing(string query)
    {
        var client = await ClientAsync();

        var get = await client.GetAsync(Path + query);
        var post = await client.PostAsync(Path + query, new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, get.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Contains("This link is not valid.", await post.Content.ReadAsStringAsync());
        Assert.Equal(0, _fixture.Store.Writes);
    }

    [Theory]
    [InlineData(UserMailCategories.AccountKey)]
    [InlineData("not-registered")]
    public async Task ALinkToARequiredOrUnknownCategory_IsRefused(string category)
    {
        var client = await ClientAsync();

        var response = await client.PostAsync(Link(category), new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _fixture.Store.Writes);
    }

    [Fact]
    public async Task ThePage_IsTranslatedThroughTheHostsTexts_IncludingTheCategorysName()
    {
        _texts = new DictionaryTextResolver(new Dictionary<string, string>
        {
            [UserMailTexts.PageQuestionKey] = "Sluta ta emot {0}?",
            [UserMailTexts.PageConfirmKey] = "Avsluta",
            ["userMail.category.news.name"] = "Nyheter"
        });
        var client = await ClientAsync();

        var page = await (await client.GetAsync(Link())).Content.ReadAsStringAsync();

        Assert.Contains("Sluta ta emot Nyheter?", page);
        Assert.Contains(">Avsluta</button>", page);
    }

    [Fact]
    public void ThePage_EncodesWhatItShows()
    {
        var page = UserMailUnsubscribeEndpoint.Render("en", "<t>", "<script>alert(1)</script>", "a & b", "Go");

        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
        Assert.Contains("a &amp; b", page);
    }
}
