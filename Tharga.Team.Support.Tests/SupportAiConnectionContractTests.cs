namespace Tharga.Team.Support.Tests;

/// <summary>
/// The shape of a team's AI connection as it crosses a boundary: what a team that never chose gets, and that the key
/// cannot leave in it.
/// </summary>
public class SupportAiConnectionContractTests
{
    private const string Key = "sk-test-0123456789";

    [Fact]
    public void ATeamThatNeverChose_GetsThePlatformsModel()
    {
        Assert.Equal(SupportAiConnectionKind.Platform, default(SupportAiConnectionKind));
        Assert.Equal(SupportAiConnectionKind.Platform, new SupportAiConnection().Kind);
    }

    /// <summary>A member that could hold the key would be one careless mapping away from returning it.</summary>
    [Fact]
    public void TheReadModel_HasNoMemberThatCouldCarryAKey()
    {
        var textMembers = typeof(SupportAiConnection).GetProperties()
            .Where(x => x.PropertyType == typeof(string))
            .Select(x => x.Name);

        Assert.DoesNotContain(textMembers, x => x.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A record prints every member, so a request logged by mistake would otherwise write the key into the log.</summary>
    [Fact]
    public void ARequestsText_NeverShowsTheKey()
    {
        var request = new SupportAiProviderRequest { Kind = SupportAiConnectionKind.Neurolito, Model = "claude-qwen3-coder:30b", ApiKey = Key };

        var text = request.ToString();

        Assert.DoesNotContain(Key, text);
        Assert.Contains("(hidden)", text);
        Assert.Contains("claude-qwen3-coder:30b", text);
    }

    [Fact]
    public void ARequestWithoutAKey_SaysSo()
    {
        Assert.Contains("ApiKey = null", new SupportAiProviderRequest { Kind = SupportAiConnectionKind.Neurolito }.ToString());
    }

    [Fact]
    public void TheResultHelpers_SayWhatHappened()
    {
        Assert.True(SupportAiConnectionResult.Success("Hello.").Succeeded);
        Assert.Equal("Hello.", SupportAiConnectionResult.Success("Hello.").Reply);
        Assert.False(SupportAiConnectionResult.Failure("The key was refused.").Succeeded);
        Assert.Equal("The key was refused.", SupportAiConnectionResult.Failure("The key was refused.").Problem);
    }
}
