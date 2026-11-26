using System.Text.RegularExpressions;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// A ratchet on the paths that reach team data with no user behind them (Tharga/Team#397): every background worker in the shipping packages either declares
/// <c>TeamAccess</c> before it reads, or is listed here as one that reads no team data, with the reason.
/// </summary>
/// <remarks>
/// <b>Why a list and not enforcement.</b> <c>TeamAccessInterceptor</c>, which refuses a call no decision covers, would reject nearly every page today: the central
/// services' reads pass through their authorization decorators and never open a database scope. Enforcing it is a large change with real breakage risk and no known
/// hole behind it, so it stays opt-in, and the log-only <c>TeamAccessReportInterceptor</c> is how a host finds what an enforcing one would refuse.
/// <para>
/// What this does instead is stop the set of workers from growing unnoticed. A background worker has no caller, so whatever it reads is unauthorized by definition,
/// and it has to say so: <c>TeamAccess.Unchecked(reason)</c>, greppable and reviewable. A new worker that does neither fails here, in CI, rather than being found
/// by a host that registers the enforcing interceptor. The workers that read nothing from the database are listed with the reason, and that list must stay true:
/// one that starts declaring access, or disappears, fails too.
/// </para>
/// </remarks>
public class BackgroundWorkerTeamAccessTests
{
    /// <summary>Workers that read no team or user data. Each says why; none of them may declare <c>TeamAccess</c>, since there is nothing for it to cover.</summary>
    private static readonly Dictionary<string, string> ReadsNoTeamData = new(StringComparer.Ordinal)
    {
        ["InvitationSenderCheck"] = "At start-up it only looks whether an invitation sender is registered and warns when none is.",
        ["TeamCacheWiringCheck"] = "At start-up it only resolves registered services to check the cache is wired, and logs.",
        ["TeamServiceCompletenessCheck"] = "At start-up it only checks which service facets are registered, and logs.",
        ["UserServiceCompletenessCheck"] = "At start-up it only checks which optional services are registered, and logs.",
        ["SessionWiringCheck"] = "At start-up it only checks that the session policy, store and cookie events are wired, from registrations and options.",
        ["SessionNotifierWiringCheck"] = "At start-up it only resolves the two notifiers and the caches to check they are the same instances.",
        ["ScopeOverrideCheck"] = "At start-up it checks configured scope overrides against the scope registry, in memory.",
        ["UnregisteredRoleScopeCheck"] = "At start-up it checks the role registry against the scope registry, in memory.",
        ["OutboundMailPolicyReport"] = "At start-up it logs what the outbound mail policy is configured to do, from configuration.",
        ["SlackNotificationSink"] = "It queues audit entries to Slack and posts them to its API; it reads nothing from the database."
    };

    [Fact]
    public void EveryBackgroundWorker_DeclaresItsAccess_OrIsListedAsReadingNoTeamData()
    {
        var undeclared = Workers().Where(x => !x.DeclaresAccess && !ReadsNoTeamData.ContainsKey(x.Class)).Select(x => $"{x.Class} ({x.File})").ToArray();

        Assert.True(undeclared.Length == 0,
            "A background worker has no caller, so what it reads is unauthorized by definition and must say so with TeamAccess.Unchecked(reason) " +
            "(or TeamAccess.System(reason)) before its first read. If it reads no team or user data, add it to ReadsNoTeamData with the reason. Offenders: " +
            string.Join("; ", undeclared));
    }

    [Fact]
    public void AWorkerListedAsReadingNothing_DeclaresNoAccess_AndStillExists()
    {
        var found = Workers().ToDictionary(x => x.Class, x => x, StringComparer.Ordinal);

        var gone = ReadsNoTeamData.Keys.Where(x => !found.ContainsKey(x)).ToArray();
        var declaring = ReadsNoTeamData.Keys.Where(x => found.TryGetValue(x, out var worker) && worker.DeclaresAccess).ToArray();

        Assert.True(gone.Length == 0, "These are listed as reading no team data but no longer exist; remove them from the list: " + string.Join(", ", gone));
        Assert.True(declaring.Length == 0, "These are listed as reading no team data but declare TeamAccess, so they read something; take them off the list: " + string.Join(", ", declaring));
    }

    [Fact]
    public void ARatchetThatFindsNothing_IsNotARatchet()
    {
        var workers = Workers().Select(x => x.Class).ToArray();

        // The workers known to read the database, so a scan that stops matching its own shipping code cannot pass by finding an empty set.
        foreach (var expected in new[] { "UserMailSender", "UserNoticeSender", "SupportMailPoller", "SupportCaseInactivityService", "MongoDbAuditLogger", "AuditRetentionIndexService", "IconSettingsRefresher" })
            Assert.Contains(expected, workers);
    }

    [Fact]
    public void EveryReasonGiven_SaysWhyInWords()
    {
        Assert.All(ReadsNoTeamData, x => Assert.True(x.Value.Length > 20, $"{x.Key} needs a reason that says why it reads no team data."));
    }

    private sealed record Worker(string Class, string File, bool DeclaresAccess);

    private static IEnumerable<Worker> Workers()
    {
        var root = SolutionRoot();

        foreach (var project in Directory.GetDirectories(root, "Tharga.Team*").Where(x => IsShipping(Path.GetFileName(x))))
        {
            foreach (var file in Directory.GetFiles(project, "*.cs", SearchOption.AllDirectories).Where(x => !IsGenerated(x)))
            {
                var text = File.ReadAllText(file);
                if (!text.Contains("BackgroundService", StringComparison.Ordinal) && !text.Contains("IHostedService", StringComparison.Ordinal)) continue;

                foreach (Match match in Regex.Matches(text, @"\bclass\s+(\w+)"))
                {
                    var open = text.IndexOf('{', match.Index);
                    if (open < 0) continue;

                    var header = text[match.Index..open];
                    if (!Regex.IsMatch(header, @"[:,]\s*(?:[\w.]+\.)?(?:BackgroundService|IHostedService)\b")) continue;

                    yield return new Worker(match.Groups[1].Value, Path.GetRelativePath(root, file).Replace('\\', '/'), Regex.IsMatch(text, @"\bTeamAccess\.(?:Unchecked|System|ForTeam)\("));
                }
            }
        }
    }

    private static bool IsShipping(string project)
        => !project.EndsWith(".Tests", StringComparison.Ordinal) && !project.EndsWith(".Sample", StringComparison.Ordinal);

    private static bool IsGenerated(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
           || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Service")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("The solution folder was not found above the test output.");
    }
}
