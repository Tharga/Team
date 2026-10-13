using Tharga.Team.Blazor.Features.User;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What a verified row on the Users page offers when its name differs from the directory's: a badge, taking
/// the directory name (never a placeholder), and writing this application's name (only with a real name and
/// a directory link).
/// </summary>
public class UserRowDirectoryNameTests
{
    private static UserViewModel Row(string storedName, string directoryName, DirectoryUserStatus? status = DirectoryUserStatus.Found, string directoryId = "oid-1")
        => new() { Key = "u-1", StoredName = storedName, DirectoryStatus = status, DirectoryName = directoryName, DirectoryId = directoryId };

    [Fact]
    public void Unverified_ShowsNothing()
    {
        var row = Row("Daniel", null, status: null);

        Assert.False(row.NameDiffersFromDirectory);
        Assert.False(row.CanUseDirectoryName);
        Assert.False(row.CanWriteNameToDirectory);
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
    public void BothRealAndDifferent_OffersBothDirections()
    {
        var row = Row("Daniel Bohlin", "Daniel");

        Assert.True(row.NameDiffersFromDirectory);
        Assert.True(row.CanUseDirectoryName);
        Assert.True(row.CanWriteNameToDirectory);
    }

    /// <summary>The case this exists for: Entra holds "unknown", this application holds the real name.</summary>
    [Fact]
    public void DirectoryPlaceholder_OffersOnlyWriting()
    {
        var row = Row("Daniel Bohlin", "unknown");

        Assert.True(row.NameDiffersFromDirectory);
        Assert.False(row.CanUseDirectoryName);
        Assert.True(row.CanWriteNameToDirectory);
    }

    [Fact]
    public void NoLocalName_OffersOnlyTaking()
    {
        var row = Row(null, "Daniel Bohlin");

        Assert.True(row.CanUseDirectoryName);
        Assert.False(row.CanWriteNameToDirectory);
    }

    [Fact]
    public void Unlinked_CannotWrite()
    {
        Assert.False(Row("Daniel Bohlin", "unknown", directoryId: null).CanWriteNameToDirectory);
    }

    /// <summary>A user disabled in the directory still has a name there worth reconciling.</summary>
    [Fact]
    public void DisabledInTheDirectory_StillCompares()
    {
        Assert.True(Row("Daniel", "Dan", DirectoryUserStatus.Disabled).NameDiffersFromDirectory);
    }
}
