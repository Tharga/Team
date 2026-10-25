using Tharga.Team.Blazor.Features.User;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// When a verified row on the Users page shows that its name differs from the directory's. What can be done about
/// it is the rename dialog's business, tested in <see cref="UserNameChangeTests"/> and <see cref="UserNameDialogRenderTests"/>.
/// </summary>
public class UserRowDirectoryNameTests
{
    private static UserViewModel Row(string storedName, string directoryName, DirectoryUserStatus? status = DirectoryUserStatus.Found)
        => new() { Key = "u-1", StoredName = storedName, DirectoryStatus = status, DirectoryName = directoryName, DirectoryId = "oid-1" };

    [Fact]
    public void Unverified_ShowsNothing()
    {
        Assert.False(Row("Daniel", null, status: null).NameDiffersFromDirectory);
    }

    [Fact]
    public void SameName_ShowsNothing()
    {
        Assert.False(Row("Daniel", "Daniel").NameDiffersFromDirectory);
    }

    [Theory]
    [InlineData(DirectoryUserStatus.NotFound)]
    [InlineData(DirectoryUserStatus.NotLinked)]
    public void NotInTheDirectory_ShowsNothing(DirectoryUserStatus status)
    {
        Assert.False(Row("Daniel", "Dan", status).NameDiffersFromDirectory);
    }

    [Fact]
    public void BothRealAndDifferent_Shows()
    {
        Assert.True(Row("Daniel Bohlin", "Daniel").NameDiffersFromDirectory);
    }

    /// <summary>The case this exists for: Entra holds "unknown", this application holds the real name.</summary>
    [Fact]
    public void DirectoryPlaceholder_Shows()
    {
        Assert.True(Row("Daniel Bohlin", "unknown").NameDiffersFromDirectory);
    }

    [Fact]
    public void NoLocalName_Shows()
    {
        Assert.True(Row(null, "Daniel Bohlin").NameDiffersFromDirectory);
    }

    /// <summary>A user disabled in the directory still has a name there worth reconciling.</summary>
    [Fact]
    public void DisabledInTheDirectory_StillCompares()
    {
        Assert.True(Row("Daniel", "Dan", DirectoryUserStatus.Disabled).NameDiffersFromDirectory);
    }
}
