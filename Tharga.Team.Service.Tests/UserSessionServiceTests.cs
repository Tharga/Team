using System.Buffers.Text;
using System.Security.Claims;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Starting and ending the current caller's server-side session (Tharga/Team#471): the claim is stamped by the library and never taken from the identity
/// provider, the id carries 256 random bits, the session starts with what the sign-in proved, and signing out ends it once and is audited once.
/// </summary>
/// <remarks>Time is a fixed clock; the people, teams and addresses are made up.</remarks>
public class UserSessionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private const string UserKey = "user-1";

    private readonly IUserSessionStore _store = Substitute.For<IUserSessionStore>();
    private readonly ITeamPrincipalAccessor _accessor = Substitute.For<ITeamPrincipalAccessor>();
    private readonly (CompositeAuditLogger Logger, FakeAuditBackend Backend) _audit = FakeAuditLoggerFactory.Create();

    /// <summary>Answers the one-argument call only; the interface's default for the two-argument one forwards to it, so either call finds the user.</summary>
    private sealed class FakeUserService(string key) : IUserService
    {
        private sealed record User(string Key) : IUser
        {
            public string Identity => "alice";
            public string EMail => "alice@example.com";
        }

        public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => Task.FromResult<IUser>(key == null ? null : new User(key));

        public IAsyncEnumerable<IUser> GetAsync() => throw new NotSupportedException();

        public Task SetUserNameAsync(string userKey, string name) => Task.CompletedTask;

        public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private UserSessionService Sut(UserIdentityResolver resolver = null, string userKey = UserKey, IUserSessionNotifier notifier = null)
        => new(_store, new FakeUserService(userKey), _accessor, new FixedTime(Now), resolver, _audit.Logger, notifier: notifier);

    /// <summary>A notifier that keeps what it was told, and whether the store had already ended the session and the audit was already written when it was.</summary>
    private sealed class RecordingNotifier : IUserSessionNotifier
    {
        public List<UserSessionEndedEventArgs> Heard { get; } = [];

        public Func<bool> Snapshot { get; set; }

        public List<bool> SnapshotAtNotify { get; } = [];

        public Exception Failure { get; set; }

        public event EventHandler<UserSessionEndedEventArgs> SessionEnded;

        public void Notify(UserSessionEndedEventArgs args)
        {
            if (Snapshot != null) SnapshotAtNotify.Add(Snapshot());
            Heard.Add(args);
            if (Failure != null) throw Failure;

            SessionEnded?.Invoke(this, args);
        }
    }

    private static ClaimsPrincipal Principal(params Claim[] extra)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "subject-1"), new Claim(ClaimTypes.Email, "alice@example.com"), .. extra], "Test"));

    private static string SessionClaim(ClaimsPrincipal principal) => principal.FindFirst(TeamClaimTypes.SessionId)?.Value;

    // ---- start: the claim

    [Fact]
    public async Task Starting_StampsTheSessionClaim_IssuedByTheLibrary()
    {
        var stamped = await Sut().StartAsync(Principal());

        var claim = Assert.Single(stamped.FindAll(TeamClaimTypes.SessionId));
        Assert.Equal("Tharga.Team", claim.Issuer);
        Assert.False(string.IsNullOrEmpty(claim.Value));
    }

    /// <summary>The claim is the session's only key: one the identity provider (or anyone upstream) put on the principal is never believed.</summary>
    [Fact]
    public async Task Starting_DiscardsASessionClaim_TheIdentityProviderSent()
    {
        var fromProvider = new Claim(TeamClaimTypes.SessionId, "chosen-by-the-provider", ClaimValueTypes.String, "https://idp.example");

        var stamped = await Sut().StartAsync(Principal(fromProvider));

        var claim = Assert.Single(stamped.FindAll(TeamClaimTypes.SessionId));
        Assert.NotEqual("chosen-by-the-provider", claim.Value);
        Assert.Equal("Tharga.Team", claim.Issuer);
    }

    [Fact]
    public async Task Starting_KeepsTheOtherClaims()
    {
        var stamped = await Sut().StartAsync(Principal(new Claim("idp", "entra")));

        Assert.Equal("entra", stamped.FindFirst("idp")?.Value);
        Assert.Equal("alice@example.com", stamped.FindFirst(ClaimTypes.Email)?.Value);
        Assert.True(stamped.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task TheSessionId_CarriesAtLeast256Bits_AndNeverRepeats()
    {
        var sut = Sut();

        var first = SessionClaim(await sut.StartAsync(Principal()));
        var second = SessionClaim(await sut.StartAsync(Principal()));

        Assert.NotEqual(first, second);
        Assert.True(Bytes(first) >= 32, $"'{first}' is fewer than 256 bits.");
        Assert.True(Bytes(second) >= 32);
    }

    /// <summary>The id may be base64url or hex; either must hold the 256 bits.</summary>
    private static int Bytes(string id)
    {
        if (id.Length % 2 == 0 && id.All(Uri.IsHexDigit)) return id.Length / 2;
        return Base64Url.DecodeFromChars(id).Length;
    }

    [Fact]
    public async Task StartingWithNoPrincipal_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Sut().StartAsync(null));
    }

    // ---- start: the stored session

    [Fact]
    public async Task Starting_StoresTheSession_AsOriginHost_WithWhatTheSignInProved()
    {
        UserSession stored = null;
        await _store.StartAsync(Arg.Do<UserSession>(x => stored = x), Arg.Any<CancellationToken>());
        var authTime = new DateTimeOffset(2026, 11, 16, 8, 55, 0, TimeSpan.Zero);

        var stamped = await Sut().StartAsync(Principal(
            new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString()),
            new Claim("idp", "entra"),
            new Claim("amr", "pwd"),
            new Claim("amr", "mfa")));

        Assert.NotNull(stored);
        Assert.Equal(SessionClaim(stamped), stored.SessionId);
        Assert.Equal(UserKey, stored.UserKey);
        Assert.Equal("subject-1", stored.Subject);
        Assert.Equal(authTime.UtcDateTime, stored.SignedInAt);
        Assert.Equal("entra", stored.IdentityProvider);
        Assert.Equal(["mfa", "pwd"], stored.AuthenticationMethods.Order());
        Assert.Equal(SessionOrigin.Host, stored.Origin);
        Assert.Equal(Now.UtcDateTime, stored.LastActivityAt);
        Assert.Null(stored.ActiveTeamKey);
        Assert.Null(stored.EndedAt);
        Assert.Null(stored.EndReason);
    }

    [Fact]
    public async Task WithNoAuthTime_TheSignInIsNow()
    {
        UserSession stored = null;
        await _store.StartAsync(Arg.Do<UserSession>(x => stored = x), Arg.Any<CancellationToken>());

        await Sut().StartAsync(Principal());

        Assert.Equal(Now.UtcDateTime, stored.SignedInAt);
    }

    [Fact]
    public async Task TheSubject_IsReadByTheHostsIdentityResolver()
    {
        UserSession stored = null;
        await _store.StartAsync(Arg.Do<UserSession>(x => stored = x), Arg.Any<CancellationToken>());

        await Sut(new UserIdentityResolver(["oid"])).StartAsync(Principal(new Claim("oid", "object-9")));

        Assert.Equal("object-9", stored.Subject);
    }

    // ---- ending the current session

    private void CallerIs(ClaimsPrincipal principal) => _accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));

    [Fact]
    public async Task SigningOut_EndsTheCallersSession_AsSignedOut_AndAuditsItOnce()
    {
        CallerIs(Principal(new Claim(TeamClaimTypes.SessionId, "s1", ClaimValueTypes.String, "Tharga.Team")));
        _store.EndAsync("s1", SessionEndReason.SignedOut, Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await Sut().EndCurrentAsync());

        var entry = Assert.Single(_audit.Backend.Entries);
        Assert.Equal(("auth", "signout"), (entry.Feature, entry.Action));
    }

    /// <summary>Another request ended it first (a logout in a second tab): this call neither claims it nor audits it.</summary>
    [Fact]
    public async Task SigningOut_OfASessionAlreadyEnded_IsFalse_AndAuditsNothing()
    {
        CallerIs(Principal(new Claim(TeamClaimTypes.SessionId, "s1")));
        _store.EndAsync("s1", SessionEndReason.SignedOut, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        Assert.False(await Sut().EndCurrentAsync());
        Assert.Empty(_audit.Backend.Entries);
    }

    [Fact]
    public async Task SigningOut_WithNoSessionClaim_TouchesNothing()
    {
        CallerIs(Principal());

        Assert.False(await Sut().EndCurrentAsync());

        await _store.DidNotReceiveWithAnyArgs().EndAsync(default, default, default);
        Assert.Empty(_audit.Backend.Entries);
    }

    [Fact]
    public async Task SigningOut_WithNoCaller_TouchesNothing()
    {
        CallerIs(null);

        Assert.False(await Sut().EndCurrentAsync());

        await _store.DidNotReceiveWithAnyArgs().EndAsync(default, default, default);
    }

    // ---- ending the others

    [Fact]
    public async Task EndingTheOthers_EndsTheUsersOtherSessions_ButNotTheCallers()
    {
        CallerIs(Principal(new Claim(TeamClaimTypes.SessionId, "mine")));
        _store.EndOthersAsync(UserKey, "mine", Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(2);

        Assert.Equal(2, await Sut().EndOtherSessionsAsync());
    }

    [Fact]
    public async Task EndingTheOthers_WithNoSessionClaim_EndsNothing()
    {
        CallerIs(Principal());

        Assert.Equal(0, await Sut().EndOtherSessionsAsync());

        await _store.DidNotReceiveWithAnyArgs().EndOthersAsync(default, default, default);
    }

    // ---- telling the host (Tharga/Team#471 part 2)

    private static ClaimsPrincipal WithSession(string id = "s1") => Principal(new Claim(TeamClaimTypes.SessionId, id, ClaimValueTypes.String, "Tharga.Team"));

    [Fact]
    public async Task SigningOut_TellsTheHost_WhoseSessionEnded_AndWhy()
    {
        var notifier = new RecordingNotifier();
        CallerIs(WithSession());
        _store.EndAsync("s1", SessionEndReason.SignedOut, Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(true);

        await Sut(notifier: notifier).EndCurrentAsync();

        var heard = Assert.Single(notifier.Heard);
        Assert.Equal((UserKey, SessionEndReason.SignedOut, false), (heard.UserKey, heard.Reason, heard.FromRemote));
    }

    /// <summary>Store first, audit second, notify last: whoever is told and looks finds the session ended, and the audit entry is there to be found.</summary>
    [Fact]
    public async Task TheNotification_FollowsTheStoreEnd_AndTheAudit()
    {
        var order = new List<string>();
        var notifier = new RecordingNotifier { Snapshot = () => _audit.Backend.Entries.Count == 1 };
        CallerIs(WithSession());
        _store.EndAsync("s1", SessionEndReason.SignedOut, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add("store");
            return true;
        });
        notifier.SessionEnded += (_, _) => order.Add("notify");

        await Sut(notifier: notifier).EndCurrentAsync();

        Assert.Equal(["store", "notify"], order);
        Assert.Equal([true], notifier.SnapshotAtNotify);
    }

    [Fact]
    public async Task SigningOut_OfASessionAlreadyEnded_TellsNobody()
    {
        var notifier = new RecordingNotifier();
        CallerIs(WithSession());
        _store.EndAsync("s1", SessionEndReason.SignedOut, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        await Sut(notifier: notifier).EndCurrentAsync();

        Assert.Empty(notifier.Heard);
    }

    [Fact]
    public async Task SigningOut_WithNoSessionClaim_TellsNobody()
    {
        var notifier = new RecordingNotifier();
        CallerIs(Principal());

        await Sut(notifier: notifier).EndCurrentAsync();

        Assert.Empty(notifier.Heard);
    }

    /// <summary>A handler that fails is the host's problem: the session is ended and audited, and the call says so.</summary>
    [Fact]
    public async Task AThrowingNotifier_DoesNotUndoOrFailTheEnd()
    {
        var notifier = new RecordingNotifier { Failure = new InvalidOperationException("host handler is broken") };
        CallerIs(WithSession());
        _store.EndAsync("s1", SessionEndReason.SignedOut, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await Sut(notifier: notifier).EndCurrentAsync());
        Assert.Single(_audit.Backend.Entries);
    }

    [Fact]
    public async Task WhenTheUserCannotBeResolved_TheEndStands_AndNobodyIsTold()
    {
        var notifier = new RecordingNotifier();
        CallerIs(WithSession());
        _store.EndAsync("s1", SessionEndReason.SignedOut, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await Sut(userKey: null, notifier: notifier).EndCurrentAsync());

        Assert.Empty(notifier.Heard);
    }

    [Fact]
    public async Task TheUserIsLookedUp_WithoutRecordingActivity()
    {
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<bool>()).Returns(Task.FromResult<IUser>(null));
        var sut = new UserSessionService(_store, users, _accessor, new FixedTime(Now), null, _audit.Logger, notifier: new RecordingNotifier());
        _store.EndAsync("s1", SessionEndReason.SignedOut, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        await sut.SignOutAsync(WithSession());

        await users.Received().GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>(), false);
        await users.DidNotReceive().GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>(), true);
    }

    // ---- signing out a given principal (the circuit's)

    [Fact]
    public async Task SignOutAsync_EndsThePrincipalsSession_AsSignedOut_AuditedOnce_AndAnnounced()
    {
        var notifier = new RecordingNotifier();
        _store.EndAsync("s1", SessionEndReason.SignedOut, Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await Sut(notifier: notifier).SignOutAsync(WithSession()));

        Assert.Equal(("auth", "signout"), (Assert.Single(_audit.Backend.Entries).Feature, _audit.Backend.Entries[0].Action));
        Assert.Equal(SessionEndReason.SignedOut, Assert.Single(notifier.Heard).Reason);
    }

    /// <summary>The circuit has no HttpContext, so the ambient caller is not asked: the principal it was given is the one whose session ends.</summary>
    [Fact]
    public async Task SignOutAsync_DoesNotAskTheAmbientCaller()
    {
        _store.EndAsync("s1", SessionEndReason.SignedOut, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        await Sut().SignOutAsync(WithSession());

        await _accessor.DidNotReceive().GetCurrentAsync();
    }

    [Fact]
    public async Task SignOutAsync_WhenAnotherEndedItFirst_IsFalse_AndSilent()
    {
        var notifier = new RecordingNotifier();
        _store.EndAsync("s1", SessionEndReason.SignedOut, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        Assert.False(await Sut(notifier: notifier).SignOutAsync(WithSession()));

        Assert.Empty(_audit.Backend.Entries);
        Assert.Empty(notifier.Heard);
    }

    [Fact]
    public async Task SignOutAsync_WithNoSessionClaim_TouchesNothing()
    {
        Assert.False(await Sut().SignOutAsync(Principal()));

        await _store.DidNotReceiveWithAnyArgs().EndAsync(default, default, default);
    }

    // ---- an end the library's own check makes

    [Theory]
    [InlineData(SessionEndReason.Idle)]
    [InlineData(SessionEndReason.MaxAge)]
    [InlineData(SessionEndReason.Ended)]
    public async Task EndingForAReason_TellsTheHostThatReason(SessionEndReason reason)
    {
        var notifier = new RecordingNotifier();
        _store.EndAsync("s1", reason, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await Sut(notifier: notifier).EndAsync(WithSession(), reason));

        var heard = Assert.Single(notifier.Heard);
        Assert.Equal((UserKey, reason, false), (heard.UserKey, heard.Reason, heard.FromRemote));
    }

    /// <summary>Two instances refusing one request at once: only the call the store says ended it is audited, and only it announces.</summary>
    [Fact]
    public async Task EndingForAReason_WhenAnotherCallEndedItFirst_TellsNobody()
    {
        var notifier = new RecordingNotifier();
        _store.EndAsync("s1", SessionEndReason.Idle, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        Assert.False(await Sut(notifier: notifier).EndAsync(WithSession(), SessionEndReason.Idle));

        Assert.Empty(notifier.Heard);
    }

    // ---- ending the others

    [Fact]
    public async Task EndingTheOthers_WhenSomeWereEnded_TellsTheHostOnce_AsEnded()
    {
        var notifier = new RecordingNotifier();
        CallerIs(WithSession("mine"));
        _store.EndOthersAsync(UserKey, "mine", Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(3);

        await Sut(notifier: notifier).EndOtherSessionsAsync();

        var heard = Assert.Single(notifier.Heard);
        Assert.Equal((UserKey, SessionEndReason.Ended), (heard.UserKey, heard.Reason));
    }

    [Fact]
    public async Task EndingTheOthers_WhenThereWereNone_TellsNobody()
    {
        var notifier = new RecordingNotifier();
        CallerIs(WithSession("mine"));
        _store.EndOthersAsync(UserKey, "mine", Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(0);

        await Sut(notifier: notifier).EndOtherSessionsAsync();

        Assert.Empty(notifier.Heard);
    }

    // ---- entering a team exclusively

    private static UserSession Other(string id) => new() { SessionId = id, UserKey = UserKey, Subject = "subject-1", SignedInAt = Now.UtcDateTime.AddHours(-1), Origin = SessionOrigin.SignIn, LastActivityAt = Now.UtcDateTime };

    [Fact]
    public async Task EnteringATeamExclusively_WhenOlderSessionsWereEnded_TellsTheHostOnce_AsANewerSignIn()
    {
        var notifier = new RecordingNotifier();
        _store.EnterTeamExclusiveAsync("s1", "strict", Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(new SessionTeamEntry([Other("old-1"), Other("old-2")], false));

        await Sut(notifier: notifier).EnterTeamExclusiveAsync(WithSession(), "strict");

        var heard = Assert.Single(notifier.Heard);
        Assert.Equal((UserKey, SessionEndReason.NewerSignIn), (heard.UserKey, heard.Reason));
    }

    [Fact]
    public async Task EnteringATeamExclusively_WhenTheCallersOwnSessionWasEnded_TellsTheHost_AsANewerSignIn()
    {
        var notifier = new RecordingNotifier();
        _store.EnterTeamExclusiveAsync("s1", "strict", Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(new SessionTeamEntry([], true));

        await Sut(notifier: notifier).EnterTeamExclusiveAsync(WithSession(), "strict");

        Assert.Equal(SessionEndReason.NewerSignIn, Assert.Single(notifier.Heard).Reason);
    }

    [Fact]
    public async Task EnteringATeamExclusively_WhenNothingWasEnded_TellsNobody()
    {
        var notifier = new RecordingNotifier();
        _store.EnterTeamExclusiveAsync("s1", "strict", Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(new SessionTeamEntry([], false));

        await Sut(notifier: notifier).EnterTeamExclusiveAsync(WithSession(), "strict");

        Assert.Empty(notifier.Heard);
    }

    [Fact]
    public async Task EnteringATeamExclusively_WhenBothHappened_TellsTheHostOnce()
    {
        var notifier = new RecordingNotifier();
        _store.EnterTeamExclusiveAsync("s1", "strict", Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(new SessionTeamEntry([Other("old")], true));

        await Sut(notifier: notifier).EnterTeamExclusiveAsync(WithSession(), "strict");

        Assert.Single(notifier.Heard);
    }
}
