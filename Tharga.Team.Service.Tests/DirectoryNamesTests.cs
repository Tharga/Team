using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Entra External ID names an account "unknown" when its sign-up flow collects no display name. That
/// placeholder must never be mistaken for a real name in either direction.
/// </summary>
public class DirectoryNamesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("unknown", true)]
    [InlineData("Unknown", true)]
    [InlineData(" UNKNOWN ", true)]
    [InlineData("Daniel Bohlin", false)]
    [InlineData("unknown user", false)]
    public void IsPlaceholder(string name, bool expected)
    {
        Assert.Equal(expected, DirectoryNames.IsPlaceholder(name));
    }

    [Theory]
    [InlineData("Daniel", "Daniel", false)]
    [InlineData("Daniel", " Daniel ", false)]
    [InlineData("Daniel", "daniel", true)]
    [InlineData("Daniel", "Dan", true)]
    [InlineData("Daniel", "unknown", true)]
    [InlineData(null, "Daniel", true)]
    [InlineData("unknown", "Daniel", true)]
    [InlineData(null, "unknown", false)]
    [InlineData("Unknown", "unknown", false)]
    [InlineData(null, null, false)]
    public void Differ(string localName, string directoryName, bool expected)
    {
        Assert.Equal(expected, DirectoryNames.Differ(localName, directoryName));
    }
}
