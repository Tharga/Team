using Tharga.Team.Blazor.Features.User;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What saving the rename dialog does. A rename here is saved at once; taking the directory's name or writing to
/// the directory is confirmed first.
/// </summary>
public class UserNameChangeTests
{
    [Fact]
    public void TypedRename_IsSavedHereWithoutConfirmation()
    {
        var change = UserNameChange.From("Ann", "Ann Larsson", "unknown", writeToDirectory: false);

        Assert.True(change.RenamesHere);
        Assert.False(change.TakeFromDirectory);
        Assert.False(change.WriteToDirectory);
        Assert.False(change.NeedsConfirmation);
    }

    [Fact]
    public void TypedRename_WrittenToTheDirectory_IsConfirmed()
    {
        var change = UserNameChange.From("Ann", " Ann Larsson ", "unknown", writeToDirectory: true);

        Assert.Equal("Ann Larsson", change.NewName);
        Assert.True(change.RenamesHere);
        Assert.True(change.WriteToDirectory);
        Assert.True(change.NeedsConfirmation);
    }

    /// <summary>Writing the current name to the directory, without changing it here.</summary>
    [Fact]
    public void UnchangedName_WrittenToTheDirectory_IsConfirmed()
    {
        var change = UserNameChange.From("Ann Larsson", "Ann Larsson", "unknown", writeToDirectory: true);

        Assert.False(change.RenamesHere);
        Assert.True(change.WriteToDirectory);
        Assert.False(change.IsNothing);
        Assert.True(change.NeedsConfirmation);
    }

    [Fact]
    public void TheDirectorysName_IsTakenAndConfirmed()
    {
        var change = UserNameChange.From("Ann", "Ann Larsson", " Ann Larsson ", writeToDirectory: false);

        Assert.True(change.TakeFromDirectory);
        Assert.False(change.WriteToDirectory);
        Assert.True(change.NeedsConfirmation);
    }

    /// <summary>Taking the directory's name and writing it straight back would be a write that changes nothing there.</summary>
    [Fact]
    public void TheDirectorysName_IsNeverWrittenBack()
    {
        var change = UserNameChange.From("Ann", "Ann Larsson", "Ann Larsson", writeToDirectory: true);

        Assert.True(change.TakeFromDirectory);
        Assert.False(change.WriteToDirectory);
    }

    /// <summary>"unknown" is the directory saying it has no name, so it is never taken.</summary>
    [Fact]
    public void APlaceholderInTheDirectory_IsNotTaken()
    {
        var change = UserNameChange.From("Ann", "unknown", "unknown", writeToDirectory: false);

        Assert.False(change.TakeFromDirectory);
        Assert.False(change.NeedsConfirmation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void APlaceholderName_IsNotWrittenToTheDirectory(string entered)
    {
        var change = UserNameChange.From("Ann", entered, "Ann Larsson", writeToDirectory: true);

        Assert.False(change.WriteToDirectory);
    }

    [Fact]
    public void NothingChanged_IsNothing()
    {
        var change = UserNameChange.From("Ann", " Ann ", "Ann Larsson", writeToDirectory: false);

        Assert.True(change.IsNothing);
        Assert.False(change.NeedsConfirmation);
    }

    /// <summary>Ticking the box when the directory already holds the name has nothing to write.</summary>
    [Fact]
    public void SameNameAsTheDirectory_WritesNothing()
    {
        var change = UserNameChange.From("Ann Larsson", "Ann Larsson", "Ann Larsson", writeToDirectory: true);

        Assert.True(change.IsNothing);
    }

    /// <summary>When the directory could not be read, writing is still possible; there is just no name to compare with.</summary>
    [Fact]
    public void DirectoryNotRead_WritesWhenAsked()
    {
        var change = UserNameChange.From("Ann", "Ann Larsson", null, writeToDirectory: true);

        Assert.True(change.WriteToDirectory);
        Assert.False(change.TakeFromDirectory);
    }
}
