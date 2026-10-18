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
/// The unsubscribe link: GET asks and changes nothing, POST turns the one category off, and a mail provider's one-click
/// POST works with no sign-in. The page names the category and the product, says it turns off only that, and how often it
/// comes; below, the user the link is for sees every category and its state when signed in, and anyone else a way to sign in.
/// </summary>
public sealed class UserMailUnsubscribeEndpointTests : IAsyncLifetime
{
    private const string Path = "/mail/unsubscribe";
    private const string UserHeader = "X-Test-User";

    private readonly UserMailFixture _fixture = new();
    private readonly IUserService _users = Substitute.For<IUserService>();
    private ITextResolver _texts;
    private Action<UserMailOptions> _configure;
    private WebApplication _app;
    private HttpClient _client;

    public UserMailUnsubscribeEndpointTests()
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
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_fixture.DataProtection);
        builder.Services.AddSingleton<TimeProvider>(_fixture.Time);
        builder.Services.AddSingleton<IUserMailPreferenceStore>(_fixture.Store);
        builder.Services.AddSingleton<IUserMailOutbox>(_fixture.Outbox);
        builder.Services.AddSingleton<IUserMailRecipientDirectory>(_fixture.Recipients);
        builder.Services.AddSingleton(_users);
        builder.Services.AddSingleton(new CompositeAuditLogger([_fixture.Audit], Options.Create(new AuditOptions())));
        builder.Services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));
        if (_texts != null) builder.Services.AddSingleton(_texts);
        builder.Services.AddAntiforgery();
        builder.Services.AddAuthentication(SignedIn.SchemeName).AddScheme<AuthenticationSchemeOptions, SignedIn>(SignedIn.SchemeName, null);
        builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.Mail.FromName = "Example";
            foreach (var category in _fixture.Options.Categories) o.AddCategory(category);
            _configure?.Invoke(o);
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

    private static HttpRequestMessage As(HttpMethod method, string uri, string user)
    {
        var request = new HttpRequestMessage(method, uri) { Content = method == HttpMethod.Post ? new FormUrlEncodedContent([]) : null };
        if (user != null) request.Headers.Add(UserHeader, user);
        return request;
    }

    private async Task<string> PageAsync(HttpMethod method, string user = null)
    {
        var response = await (_client ?? await ClientAsync()).SendAsync(As(method, Link(), user));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Get_AsksFirst_AndChangesNothing()
    {
        var client = await ClientAsync();

        var response = await client.GetAsync(Link());
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>Stop receiving News?</h1>", page);
        Assert.Contains("What is new in the product.", page);
        Assert.Contains("<form method=\"post\"><button type=\"submit\">Stop sending me News</button></form>", page);
        Assert.Equal(0, _fixture.Store.Writes);
        Assert.Empty(_fixture.Audit.Entries);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_SaysItTurnsOffOnlyThisCategory_NamingTheProduct()
    {
        var page = await PageAsync(HttpMethod.Get);

        Assert.Contains("<p class=\"scope\">This only turns off News. Other mail from Example is not affected.</p>", page);
    }

    [Fact]
    public async Task TheProduct_IsTheHostsProductNameWhenSet()
    {
        _configure = o => o.ProductName = "Example Cloud";

        var page = await PageAsync(HttpMethod.Get);

        Assert.Contains("Other mail from Example Cloud is not affected.", page);
    }

    [Fact]
    public async Task Get_SaysHowOftenTheCategoryIsSent()
    {
        _fixture.Outbox.Add(_fixture.Done(UserMailFixture.News, "anna", UserMailStatus.Sent, TimeSpan.FromDays(10)));
        _fixture.Outbox.Add(_fixture.Done(UserMailFixture.News, "anna", UserMailStatus.Sent, TimeSpan.FromDays(100)));

        var page = await PageAsync(HttpMethod.Get);

        Assert.Contains("<p class=\"frequency\">About 2 a year</p>", page);
    }

    [Fact]
    public async Task GettingTheLinkAgainAndAgain_AsAScannerWould_StillChangesNothing()
    {
        var client = await ClientAsync();

        for (var i = 0; i < 3; i++) await client.SendAsync(As(HttpMethod.Get, Link(), i == 0 ? UserMailFixture.UserKey : null));

        Assert.Empty(_fixture.Store.Stored);
        Assert.Equal(0, _fixture.Store.Writes);
    }

    [Fact]
    public async Task Post_TurnsTheOneCategoryOff_AndSaysSo_AndHowToTurnItBackOn()
    {
        var client = await ClientAsync();

        var response = await client.PostAsync(Link(), new FormUrlEncodedContent([]));
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>You will no longer receive News.</h1>", page);
        Assert.Contains("You can turn it back on at any time in your mail settings.", page);
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
    public async Task SignedInAsTheLinksUser_EveryCategoryIsListed_WithItsState_HowOften_AndTheSettingsLink()
    {
        _configure = o => o.PreferencesPath = "/profile/mail";
        _fixture.Outbox.Add(_fixture.Done(UserMailCategories.AccountKey, "anna", UserMailStatus.Sent, TimeSpan.FromDays(10)));

        var page = await PageAsync(HttpMethod.Get, UserMailFixture.UserKey);

        Assert.Contains("<h2>The mail you get</h2>", page);
        Assert.Contains("<li data-category=\"account\" data-state=\"required\"><span class=\"name\">Account and security</span><span class=\"state\">Always sent: part of the terms of use</span><span class=\"frequency\">About 1 a year</span></li>", page);
        Assert.Contains("<li data-category=\"service\" data-state=\"required\">", page);
        Assert.Contains("<li data-category=\"news\" data-state=\"on\"><span class=\"name\">News</span><span class=\"state\">On</span><span class=\"frequency\">None in the past year</span></li>", page);
        Assert.Contains("<li data-category=\"digest\" data-state=\"off\"><span class=\"name\">Weekly digest</span><span class=\"state\">Off</span>", page);
        Assert.Contains("<p class=\"settings\"><a href=\"/profile/mail\">Change your mail settings</a></p>", page);
        Assert.DoesNotContain("class=\"sign-in\"", page);
        Assert.Equal(0, _fixture.Store.Writes);
    }

    [Fact]
    public async Task SignedInAsTheLinksUser_AfterThePost_TheCategoryIsListedOff()
    {
        var page = await PageAsync(HttpMethod.Post, UserMailFixture.UserKey);

        Assert.Contains("<li data-category=\"news\" data-state=\"off\">", page);
    }

    [Fact]
    public async Task SignedIn_ARequiredCategory_LinksToTheTermsOfUse_WhenTheHostHasThem()
    {
        _configure = o => o.TermsUrl = "/terms";

        var page = await PageAsync(HttpMethod.Get, UserMailFixture.UserKey);

        Assert.Contains("<span class=\"state\"><a href=\"/terms\">Always sent: part of the terms of use</a></span>", page);
    }

    [Fact]
    public async Task SignedIn_WithoutMailSettings_ThereIsNoSettingsLink()
    {
        var page = await PageAsync(HttpMethod.Get, UserMailFixture.UserKey);

        Assert.Contains("<section class=\"mail\">", page);
        Assert.DoesNotContain("class=\"settings\"", page);
    }

    [Fact]
    public async Task NotSignedIn_NoOtherCategoryIsShown_AndSignInLeadsBackToTheMailSettings()
    {
        _configure = o =>
        {
            o.PreferencesPath = "/profile/mail";
            o.SignInPath = "/login";
        };

        var page = await PageAsync(HttpMethod.Get);

        Assert.DoesNotContain("<section class=\"mail\">", page);
        Assert.DoesNotContain("Weekly digest", page);
        Assert.DoesNotContain("Account and security", page);
        Assert.Contains("<p class=\"sign-in\"><a href=\"/login?returnUrl=%2Fprofile%2Fmail\">Sign in to see all the mail you get and choose what you want</a></p>", page);
    }

    [Fact]
    public async Task SignedInAsSomebodyElse_NoChoiceOfTheLinksUserIsShown_OnlyTheWayToSignIn()
    {
        _configure = o =>
        {
            o.PreferencesPath = "/profile/mail";
            o.SignInPath = "/login";
        };
        await _fixture.Store.SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.Digest, true, CancellationToken.None);

        var get = await PageAsync(HttpMethod.Get, "user-9");
        var post = await PageAsync(HttpMethod.Post, "user-9");

        foreach (var page in new[] { get, post })
        {
            Assert.DoesNotContain("<section class=\"mail\">", page);
            Assert.DoesNotContain("data-state", page);
            Assert.DoesNotContain("Weekly digest", page);
            Assert.Contains("class=\"sign-in\"", page);
        }
    }

    [Fact]
    public async Task NotSignedIn_WithoutASignInPath_ThereIsNoSignInLink()
    {
        var page = await PageAsync(HttpMethod.Get);

        Assert.DoesNotContain("class=\"sign-in\"", page);
        Assert.DoesNotContain("<section class=\"mail\">", page);
    }

    [Fact]
    public async Task TheHostsStyle_IsAddedToThePagesOwn_UnderTheSamePolicy()
    {
        _configure = o => o.PageCss = "body{background:#123456}";
        var client = await ClientAsync();

        var response = await client.GetAsync(Link());
        var page = await response.Content.ReadAsStringAsync();

        Assert.Contains("body{background:#123456}</style>", page);
        Assert.Equal("default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.DoesNotContain("<script", page);
    }

    [Fact]
    public async Task ThePage_IsTranslatedThroughTheHostsTexts_IncludingTheCategorysName()
    {
        _texts = new DictionaryTextResolver(new Dictionary<string, string>
        {
            [UserMailTexts.PageQuestionKey] = "Sluta ta emot {0}?",
            [UserMailTexts.PageStopKey] = "Sluta skicka {0} till mig",
            [UserMailTexts.PageOnlyThisKey] = "Det här stänger bara av {0}. Annan e-post från {1} påverkas inte.",
            ["userMail.category.news.name"] = "Nyheter"
        });
        var client = await ClientAsync();

        var page = await (await client.GetAsync(Link())).Content.ReadAsStringAsync();

        Assert.Contains("Sluta ta emot Nyheter?", page);
        Assert.Contains(">Sluta skicka Nyheter till mig</button>", page);
        Assert.Contains(WebUtility.HtmlEncode("Det här stänger bara av Nyheter. Annan e-post från Example påverkas inte."), page);
    }

    [Fact]
    public void ThePage_EncodesWhatItShows()
    {
        var page = UserMailUnsubscribeEndpoint.Render(new UserMailUnsubscribePage
        {
            Language = "en",
            Title = "<t>",
            Heading = "<script>alert(1)</script>",
            Lines = [new UserMailUnsubscribeLine("scope", "a & b")],
            Button = "Go",
            Overview = new UserMailUnsubscribeOverview("<h>", [new UserMailUnsubscribeItem("news", "<b>News</b>", "required", "Always", "/terms?a=1&b=\"2\"", null)], null),
            SignIn = new UserMailUnsubscribeAnchor("<i>in</i>", "/login")
        });

        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
        Assert.Contains("a &amp; b", page);
        Assert.Contains("&lt;b&gt;News&lt;/b&gt;", page);
        Assert.Contains("href=\"/terms?a=1&amp;b=&quot;2&quot;\"", page);
        Assert.Contains("&lt;i&gt;in&lt;/i&gt;", page);
    }

    [Theory]
    [InlineData("/login", "/settings", "/login?returnUrl=%2Fsettings")]
    [InlineData("/login?prompt=1", "/settings", "/login?prompt=1&returnUrl=%2Fsettings")]
    [InlineData("https://id.example.com/in", "https://app.example.com/settings", "https://id.example.com/in?returnUrl=https%3A%2F%2Fapp.example.com%2Fsettings")]
    [InlineData("/login", null, "/login")]
    public void SignIn_ReturnsToTheMailSettings(string signIn, string preferences, string expected)
    {
        Assert.Equal(expected, UserMailUnsubscribeEndpoint.SignInUrl(new UserMailOptions { SignInPath = signIn, PreferencesPath = preferences }));
    }

    [Theory]
    [InlineData(nameof(UserMailOptions.PreferencesPath), "profile")]
    [InlineData(nameof(UserMailOptions.SignInPath), "//elsewhere.example/login")]
    [InlineData(nameof(UserMailOptions.PageCss), "body{}</style><script>")]
    public void APageOptionThatCannotBeUsed_StopsRegistration(string option, string value)
    {
        var e = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaUserMail(o => typeof(UserMailOptions).GetProperty(option)!.SetValue(o, value)));

        Assert.Contains(option, e.Message);
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
