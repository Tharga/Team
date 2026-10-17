using Microsoft.Extensions.Logging;
using Tharga.Team;

namespace Tharga.Team.Blazor.Tests;

public class ResilientMemberLookupTests
{
    [Fact]
    public void PickOneOrDefault_NoMatch_ReturnsNull()
    {
        var logger = new RecordingLogger();
        var members = new[]
        {
            new FakeMember("a"),
            new FakeMember("b")
        };

        var result = members.PickOneOrDefault(m => m.Key == "x", logger, "team-1", "x");

        Assert.Null(result);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void PickOneOrDefault_SingleMatch_ReturnsIt_NoWarning()
    {
        var logger = new RecordingLogger();
        var target = new FakeMember("b");
        var members = new[]
        {
            new FakeMember("a"),
            target,
            new FakeMember("c")
        };

        var result = members.PickOneOrDefault(m => m.Key == "b", logger, "team-1", "b");

        Assert.Same(target, result);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void PickOneOrDefault_TwoMatches_ReturnsFirst_LogsWarning()
    {
        var logger = new RecordingLogger();
        var first = new FakeMember("dup");
        var second = new FakeMember("dup");
        var members = new[]
        {
            new FakeMember("a"),
            first,
            second
        };

        var result = members.PickOneOrDefault(m => m.Key == "dup", logger, "team-42", "dup");

        Assert.Same(first, result);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("team-42", warning);
        Assert.Contains("dup", warning);
        Assert.Contains("2", warning);
    }

    [Fact]
    public void PickOneOrDefault_ThreeMatches_ReturnsFirst_LogsWarningWithCount()
    {
        var logger = new RecordingLogger();
        var first = new FakeMember("dup");
        var members = new[]
        {
            first,
            new FakeMember("dup"),
            new FakeMember("dup")
        };

        var result = members.PickOneOrDefault(m => m.Key == "dup", logger, "team-7", "dup");

        Assert.Same(first, result);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("3", warning);
    }

    [Fact]
    public void PickOneOrDefault_NullLogger_DoesNotThrow_OnDuplicates()
    {
        var first = new FakeMember("dup");
        var members = new[] { first, new FakeMember("dup") };

        var result = members.PickOneOrDefault(m => m.Key == "dup", logger: null, "team-1", "dup");

        Assert.Same(first, result);
    }

    [Fact]
    public void PickOneOrDefault_NullSource_ReturnsDefault()
    {
        var logger = new RecordingLogger();

        FakeMember[] members = null;
        var result = members.PickOneOrDefault(m => m.Key == "x", logger, "team-1", "x");

        Assert.Null(result);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void PickOneOrDefault_WorksForNonMemberTypes()
    {
        var logger = new RecordingLogger();
        var items = new[]
        {
            (Id: 1, Hash: "x"),
            (Id: 2, Hash: "dup"),
            (Id: 3, Hash: "dup")
        };

        var result = items.PickOneOrDefault(x => x.Hash == "dup", logger, "ctx", "dup");

        Assert.Equal(2, result.Id);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public void ReplaceByReference_ReplacesOnlyTheTargetInstance_PreservesDuplicateKeyedSiblings()
    {
        // Reproduces the Tharga/Team#64 strip-siblings scenario: a list contains two members
        // with the same Key but as distinct instances. The picked one must be replaced; the other
        // must remain untouched.
        var sibling = new FakeMember("dup");
        var target = new FakeMember("dup");
        var members = new[]
        {
            new FakeMember("a"),
            sibling,
            target,
            new FakeMember("c")
        };

        var replacement = new FakeMember("dup");
        var result = members.ReplaceByReference(target, replacement);

        Assert.Equal(4, result.Length);
        Assert.Same(members[0], result[0]);
        Assert.Same(sibling, result[1]);
        Assert.Same(replacement, result[2]);
        Assert.Same(members[3], result[3]);
    }

    [Fact]
    public void ReplaceByReference_NoReferenceMatch_ReturnsCopyOfOriginal()
    {
        var members = new[] { new FakeMember("a"), new FakeMember("b") };
        var notInList = new FakeMember("a");
        var replacement = new FakeMember("a");

        var result = members.ReplaceByReference(notInList, replacement);

        Assert.Equal(2, result.Length);
        Assert.Same(members[0], result[0]);
        Assert.Same(members[1], result[1]);
    }

    [Fact]
    public void ReplaceByReference_NullSource_ReturnsEmptyArray()
    {
        FakeMember[] members = null;
        var result = members.ReplaceByReference(new FakeMember("x"), new FakeMember("y"));

        Assert.Empty(result);
    }

    private sealed class FakeMember : ITeamMember
    {
        public FakeMember(string key) { Key = key; }
        public string Key { get; }
        public string Name => null;
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public MembershipState? State => null;
        public AccessLevel? AccessLevel => Tharga.Team.AccessLevel.User;
        public string[] TenantRoles => null;
        public string[] ScopeOverrides => null;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public IDisposable BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
