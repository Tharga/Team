using System.Security.Claims;
using System.Text.RegularExpressions;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// After a temporary consent runs out, every surface that reads consent sees the consent it returned to.
/// </summary>
/// <remarks>
/// Expiry needs no job because each consent read goes through <see cref="TeamConsent.Resolve"/>. That is only true
/// while nothing reads the stored fields directly, which is why the last test here scans the source.
/// </remarks>
public class ConsentExpiryReadsTests
{
    private const string TeamKey = "team-1";
    private const string Developer = "Developer";

    private sealed record FakeTeam : ITeam
    {
        public string Key => TeamKey;
        public string Name => "Team";
        public string Icon => null;
        public string[] ConsentedRoles { get; init; }
        public AccessLevel? ConsentAccessLevel { get; init; }
        public TemporaryConsent TemporaryConsent { get; init; }
    }

    private static FakeTeam Temporary(DateTime expiresAt, string[] previousRoles = null, AccessLevel? previousLevel = null) => new()
    {
        ConsentedRoles = [Developer],
        ConsentAccessLevel = AccessLevel.Administrator,
        TemporaryConsent = new TemporaryConsent { ExpiresAt = expiresAt, PreviousConsentedRoles = previousRoles, PreviousConsentAccessLevel = previousLevel }
    };

    private static ClaimsPrincipal WithRole(string role)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "dev"), new Claim(ClaimTypes.Role, role)], "Test"));

    private static IScopeRegistry Registry()
    {
        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns(call => [$"level:{call.ArgAt<AccessLevel>(0)}"]);
        return registry;
    }

    private static Task<TeamGrant> ResolveGrantAsync(ITeam team)
    {
        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamMemberAsync(TeamKey, Arg.Any<string>()).Returns((ITeamMember)null);
        teamService.GetConsentedTeamsAsync(Arg.Any<string[]>()).Returns(new[] { team }.ToAsyncEnumerable());

        return new TeamGrantResolver(teamService, Registry(), null).ResolveAsync(WithRole(Developer), "dev", TeamKey, AccessLevel.Viewer);
    }

    // --- the claims path ---

    [Fact]
    public async Task Grant_UsesTheTemporaryLevel_BeforeExpiry()
        => Assert.Equal(AccessLevel.Administrator, (await ResolveGrantAsync(Temporary(DateTime.UtcNow.AddHours(1), [Developer], AccessLevel.Viewer))).AccessLevel);

    [Fact]
    public async Task Grant_ReturnsToThePreviousLevel_AfterExpiry()
        => Assert.Equal(AccessLevel.Viewer, (await ResolveGrantAsync(Temporary(DateTime.UtcNow.AddHours(-1), [Developer], AccessLevel.Viewer))).AccessLevel);

    /// <summary>
    /// A lookup that returns the team on its stored roles alone — as a host implementing <see cref="ITeamService"/>
    /// directly may — must not keep granting after expiry. The resolver checks what is in force itself.
    /// </summary>
    [Fact]
    public async Task Grant_IsNone_AfterExpiry_WhenThereWasNoPreviousConsent()
        => Assert.Null(await ResolveGrantAsync(Temporary(DateTime.UtcNow.AddHours(-1))));

    // --- an API key naming a team ---

    private static Task<TeamContext> ResolveKeyContextAsync(ITeam team)
    {
        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamByKeyAsync(TeamKey).Returns(team);

        var systemKey = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamClaimTypes.IsSystemKey, "true")], "Test"));
        return new TeamContextResolver(teamService, Registry()).ResolveAsync(systemKey, TeamKey);
    }

    [Fact]
    public async Task ApiKeyContext_UsesThePreviousLevel_AfterExpiry()
    {
        var context = await ResolveKeyContextAsync(Temporary(DateTime.UtcNow.AddHours(-1), [Developer], AccessLevel.User));

        Assert.False(context.IsRefused);
        Assert.Equal(["level:User"], context.Scopes);
    }

    [Fact]
    public async Task ApiKeyContext_IsRefused_AfterExpiry_WhenThereWasNoPreviousConsent()
    {
        var context = await ResolveKeyContextAsync(Temporary(DateTime.UtcNow.AddHours(-1)));

        Assert.Equal(TeamContextRefusal.NotConsented, context.Refusal);
    }

    // --- the guard ---

    /// <summary>
    /// Only the consent rule and the store may read the stored consent fields. Anything else reading them keeps
    /// honouring a temporary consent after it runs out.
    /// </summary>
    [Fact]
    public void NothingReadsStoredConsentDirectly()
    {
        var offenders = SourceFiles()
            .Where(f => !IsAllowed(f))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => DirectRead.IsMatch(l.Text) && !l.Text.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Select(l => $"{Path.GetRelativePath(RepoRoot(), l.File)}:{l.Line}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Read consent through TeamConsent.Resolve, not ConsentedRoles/ConsentAccessLevel directly — a direct read keeps " +
            $"honouring a temporary consent after it has expired. Found at: {string.Join(", ", offenders)}");
    }

    /// <summary>A scan matching nothing passes forever; this pins that it sees the files and the pattern.</summary>
    [Fact]
    public void TheGuard_SeesTheSourceAndTheShape()
    {
        Assert.Contains(SourceFiles(), f => f.EndsWith("TeamGrantResolver.cs", StringComparison.Ordinal));
        Assert.Matches(DirectRead, "var level = team.ConsentAccessLevel ?? x;");
        Assert.Matches(DirectRead, "roles = team.ConsentedRoles;");
        Assert.DoesNotMatch(DirectRead, "SetTeamConsentAsync(teamKey, consentedRoles, accessLevel)");
    }

    private static readonly Regex DirectRead = new(@"\.Consent(edRoles|AccessLevel)\b", RegexOptions.Compiled);

    private static bool IsAllowed(string file)
    {
        var name = Path.GetFileName(file);
        return name is "ITeam.cs" or "TeamConsent.cs"
               || file.Contains($"{Path.DirectorySeparatorChar}Tharga.Team.MongoDB{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles()
        => Directory.GetDirectories(RepoRoot(), "Tharga.Team*")
            .Where(d => !d.EndsWith(".Tests", StringComparison.Ordinal) && !d.EndsWith(".Sample", StringComparison.Ordinal))
            .SelectMany(d => Directory.GetFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tharga.Team.sln"))) dir = dir.Parent;

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
