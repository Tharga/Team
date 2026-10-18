using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// A contact's unsubscribe link on the same route as a user's: GET names the category and the product, says it stops only
/// that kind of mail and how often it comes, and offers two buttons, that category or all mail from the product; POST opts
/// the address out the way the button said, a one-click request the category; no categories are listed and no sign-in is
/// offered, whoever is signed in; and the page is served under the same headers with no script.
/// </summary>
public sealed class ContactMailUnsubscribeEndpointTests : IAsyncLifetime
{
    private const string Path = "/mail/unsubscribe";
    private const string UserHeader = "X-Test-User";

    private readonly ContactMailFixture _fixture = new();
    private readonly IUserService _users = Substitute.For<IUserService>();
    private ITextResolver _texts;
    private bool _registerContacts = true;
    private WebApplication _app;
    private HttpClient _client;

    public ContactMailUnsubscribeEndpointTests()
    {
        _users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(x =>
        {
            var key = x.Arg<ClaimsPrincipal>()?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (key == null) return Task.FromResult<IUser>(null);

            var user = Substitute.For<IUser>();
            user.Key.Returns(key);
            return Task.FromResult(user);
        });
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app != null) await _app.DisposeAsync();
    }

    private async Task<HttpClient> ClientAsync()
    {
        if (_client != null) return _client;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_fixture.Mail.DataProtection);
        builder.Services.AddSingleton<TimeProvider>(_fixture.Mail.Time);
        builder.Services.AddSingleton<IUserMailPreferenceStore>(_fixture.Mail.Store);
        builder.Services.AddSingleton<IUserMailOutbox>(_fixture.Mail.Outbox);
        builder.Services.AddSingleton<IUserMailRecipientDirectory>(_fixture.Mail.Recipients);
        builder.Services.AddSingleton<IContactMailSuppressionStore>(_fixture.Suppressions);
        builder.Services.AddSingleton(_users);
        builder.Services.AddSingleton(new CompositeAuditLogger([_fixture.Mail.Audit], Options.Create(new AuditOptions())));
        builder.Services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));
        if (_texts != null) builder.Services.AddSingleton(_texts);
        builder.Services.AddAntiforgery();
        builder.Services.AddAuthentication(SignedIn.SchemeName).AddScheme<AuthenticationSchemeOptions, SignedIn>(SignedIn.SchemeName, null);
        builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.Mail.FromName = "Example";
            o.PreferencesPath = "/profile/mail";
            o.SignInPath = "/login";
            o.PageCss = "body{background:#123456}";
            foreach (var category in _fixture.Mail.Options.Categories) o.AddCategory(category);
        });
        if (_registerContacts)
        {
            builder.Services.AddThargaContactMessages(o =>
            {
                o.SenderLine = ContactMailFixture.SenderLine;
                foreach (var category in _fixture.Options.Categories) o.AddCategory(category);
            });
        }

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseAntiforgery();
        _app.MapThargaMailUnsubscribe();
        await _app.StartAsync();

        return _client = _app.GetTestClient();
    }

    private string Link(string category = ContactMailFixture.Offers, string address = ContactMailFixture.Address)
        => $"{Path}?token={Uri.EscapeDataString(_fixture.Mail.Tokens.CreateForAddress(address, category))}";

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) => new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));

    private async Task<(HttpResponseMessage Response, string Page)> SendAsync(HttpMethod method, string uri, HttpContent content = null, string user = null)
    {
        var client = await ClientAsync();
        var request = new HttpRequestMessage(method, uri) { Content = content ?? (method == HttpMethod.Post ? Form() : null) };
        if (user != null) request.Headers.Add(UserHeader, user);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return (response, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_NamesTheCategoryAndTheProduct_SaysItStopsOnlyThat_AndChangesNothing()
    {
        var (response, page) = await SendAsync(HttpMethod.Get, Link());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Unsubscribe</title>", page);
        Assert.Contains("<h1>Stop receiving Offers?</h1>", page);
        Assert.Contains("<p class=\"scope\">This only turns off Offers. Other mail from Example is not affected.</p>", page);
        Assert.Contains("<p class=\"description\">What may be of interest to your organisation.</p>", page);
        Assert.Empty(_fixture.Suppressions.Items);
        Assert.Empty(_fixture.Mail.Audit.Entries);
    }

    [Fact]
    public async Task Get_OffersToStopTheCategory_OrAllMailFromTheProduct()
    {
        var (_, page) = await SendAsync(HttpMethod.Get, Link());

        Assert.Contains("<form method=\"post\"><button type=\"submit\" name=\"scope\" value=\"category\">Stop sending me Offers</button><button type=\"submit\" name=\"scope\" value=\"all\">Stop all mail from Example</button></form>", page);
    }

    [Fact]
    public async Task Get_SaysHowOftenTheCategoryIsSent_CountedOverContactMail()
    {
        await _fixture.QueueAsync(ContactMailFixture.Offers, "a@customer.example", "b@customer.example");
        await _fixture.QueueAsync(ContactMailFixture.Offers, "a@customer.example", "b@customer.example");
        await _fixture.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        var (_, page) = await SendAsync(HttpMethod.Get, Link());

        Assert.Contains("<p class=\"frequency\">About 2 a year</p>", page);
    }

    [Fact]
    public async Task SignedIn_NoCategoryIsListed_AndNoSignInIsOffered()
    {
        foreach (var user in new[] { null, UserMailFixture.UserKey })
        {
            var (_, get) = await SendAsync(HttpMethod.Get, Link(), user: user);

            Assert.DoesNotContain("<section class=\"mail\">", get);
            Assert.DoesNotContain("class=\"sign-in\"", get);
            Assert.DoesNotContain("class=\"settings\"", get);
            Assert.DoesNotContain("Weekly digest", get);
        }
    }

    [Fact]
    public async Task PostingTheFirstButton_OptsTheAddressOutOfTheCategory_AndSaysWhyTheAddressIsKept()
    {
        var (response, page) = await SendAsync(HttpMethod.Post, Link(), Form(("scope", "category")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>You will no longer receive Offers.</h1>", page);
        Assert.Contains("<p class=\"kept\">Your address is kept only to make sure this is respected.</p>", page);
        Assert.DoesNotContain("<form", page);
        var suppression = Assert.Single(_fixture.Suppressions.Items);
        Assert.Equal((ContactMailFixture.Address, ContactMailFixture.Offers, ContactMailSuppressionSource.Link), (suppression.Address, suppression.Category, suppression.Source));
        Assert.Equal(UserMailFixture.Start, suppression.SuppressedAt);
    }

    [Fact]
    public async Task PostingTheSecondButton_OptsTheAddressOutOfAllContactMail()
    {
        var (_, page) = await SendAsync(HttpMethod.Post, Link(), Form(("scope", "all")));

        Assert.Contains("<h1>You will no longer receive mail from Example.</h1>", page);
        var suppression = Assert.Single(_fixture.Suppressions.Items);
        Assert.Null(suppression.Category);
        Assert.Equal(ContactMailSuppressionSource.Link, suppression.Source);
    }

    [Fact]
    public async Task AOneClickPost_AsRfc8058Describes_OptsOutOfTheCategory_WithoutSignInOrAntiforgeryToken()
    {
        var (response, _) = await SendAsync(HttpMethod.Post, Link(), Form(("List-Unsubscribe", "One-Click")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var suppression = Assert.Single(_fixture.Suppressions.Items);
        Assert.Equal((ContactMailFixture.Offers, ContactMailSuppressionSource.OneClick), (suppression.Category, suppression.Source));
    }

    [Fact]
    public async Task APostWithNoForm_OptsOutOfTheCategory()
    {
        var (response, _) = await SendAsync(HttpMethod.Post, Link(), new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ContactMailFixture.Offers, Assert.Single(_fixture.Suppressions.Items).Category);
    }

    [Fact]
    public async Task AnOptOut_IsAuditedOnce_WithTheCategoryAndHow_AndNeverTheAddress()
    {
        await SendAsync(HttpMethod.Post, Link(), Form(("scope", "all")));
        await SendAsync(HttpMethod.Post, Link(), Form(("scope", "all")));

        var entry = Assert.Single(_fixture.Mail.Audit.Entries);
        Assert.Equal((ContactMessageAudit.Feature, ContactMessageAudit.UnsubscribeAction), (entry.Feature, entry.Action));
        Assert.Equal(ContactMessageAudit.EveryCategory, entry.Metadata[ContactMessageAudit.Category]);
        Assert.Equal("Link", entry.Metadata[ContactMessageAudit.Source]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains("customer.example"));
        Assert.Single(_fixture.Suppressions.Items);
    }

    [Fact]
    public async Task AnOptedOutAddress_IsSkippedWhenItsMailIsSent()
    {
        await _fixture.QueueAsync();
        await SendAsync(HttpMethod.Post, Link(), Form(("scope", "category")));

        await _fixture.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        Assert.Empty(_fixture.ContactMail.Sent);
        Assert.Equal(UserMailStatus.Skipped, _fixture.Mail.Outbox.Items.Single().Status);
    }

    [Fact]
    public async Task TheContactsPage_HasTheSameHeaders_TheHostsStyle_AndNoScript()
    {
        var (response, page) = await SendAsync(HttpMethod.Get, Link());

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("noindex", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Contains("body{background:#123456}</style>", page);
        Assert.DoesNotContain("<script", page);
    }

    [Fact]
    public async Task ThePage_IsTranslatedThroughTheHostsTexts_IncludingTheCategorysName()
    {
        _texts = new DictionaryTextResolver(new Dictionary<string, string>
        {
            [ContactMailTexts.PageStopAllKey] = "Stoppa all e-post från {0}",
            ["contactMail.category.offers.name"] = "Erbjudanden"
        });

        var (_, page) = await SendAsync(HttpMethod.Get, Link());

        Assert.Contains("Stop receiving Erbjudanden?", page);
        Assert.Contains(WebUtility.HtmlEncode("Stoppa all e-post från Example"), page);
    }

    [Theory]
    [InlineData("?token=forged")]
    [InlineData("?token=preview")]
    public async Task ALinkThatIsNotValid_IsRefused_AndOptsNothingOut(string query)
    {
        var (get, _) = await SendAsync(HttpMethod.Get, Path + query);
        var (post, _) = await SendAsync(HttpMethod.Post, Path + query, Form(("scope", "all")));

        Assert.Equal(HttpStatusCode.BadRequest, get.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Empty(_fixture.Suppressions.Items);
    }

    [Fact]
    public async Task ALinkToACategoryNoLongerRegistered_IsRefused()
    {
        var (response, _) = await SendAsync(HttpMethod.Post, Link("retired"), Form(("scope", "all")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_fixture.Suppressions.Items);
    }

    [Fact]
    public async Task WithoutContactMailRegistered_AContactsLinkIsNotValid()
    {
        _registerContacts = false;

        var (response, page) = await SendAsync(HttpMethod.Post, Link(), Form(("scope", "all")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("This link is not valid.", page);
        Assert.Empty(_fixture.Suppressions.Items);
        Assert.Equal(0, _fixture.Mail.Store.Writes);
    }

    [Fact]
    public async Task AUsersLink_StillWorksBesideContactMail_AndOptsNoAddressOut()
    {
        var token = _fixture.Mail.Tokens.Create(UserMailFixture.UserKey, UserMailFixture.News);

        var (response, page) = await SendAsync(HttpMethod.Post, $"{Path}?token={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>You will no longer receive News.</h1>", page);
        Assert.False(_fixture.Mail.Store.Stored[(UserMailFixture.UserKey, UserMailFixture.News)]);
        Assert.Empty(_fixture.Suppressions.Items);
    }

    private sealed class SignedIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user)) return Task.FromResult(AuthenticateResult.NoResult());

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
