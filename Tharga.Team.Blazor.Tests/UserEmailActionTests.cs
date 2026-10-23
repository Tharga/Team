using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The users grid offers "Change email" beside Rename and opens the dialog for the row's user, with its texts in the
/// catalogue a host translates from.
/// </summary>
public class UserEmailActionTests
{
    private static string UsersListViewSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "Tharga.Team.Blazor", "Features", "User", "UsersListView.razor"));
    }

    [Fact]
    public void TheRowMenu_OffersChangeEmail_RightAfterRename()
    {
        var source = UsersListViewSource();

        var rename = source.IndexOf("Value=\"@RenameAction\"", StringComparison.Ordinal);
        var change = source.IndexOf("Value=\"@ChangeEmailAction\"", StringComparison.Ordinal);

        Assert.True(rename > 0 && change > rename);
        Assert.Contains("@_text[UsersListViewText.ActionChangeEmail]", source);
    }

    [Fact]
    public void ChoosingChangeEmail_OpensTheDialog_ForThatUser()
    {
        var source = UsersListViewSource();

        Assert.Contains("case ChangeEmailAction:", source);
        Assert.Contains("OpenAsync<UserEmailDialog>", source);
        Assert.Contains("{ nameof(UserEmailDialog.UserKey), user.Key }", source);
    }

    [Fact]
    public void TheMenuTexts_AreResolvedWithTheViewsOtherText()
    {
        Assert.Contains(UsersListViewText.ActionChangeEmail, UsersListViewText.All);
        Assert.Contains(UsersListViewText.ChangeEmailTitle, UsersListViewText.All);
        Assert.Equal("Change email", UsersListViewText.ActionChangeEmail.Default);
    }

    [Fact]
    public void TheDialogsTexts_AreInTheSharedKeyList()
    {
        Assert.All(UserEmailDialogText.All, key => Assert.Contains(key, ThargaTextKeys.All));
        Assert.All(UserEmailDialogText.All, key => Assert.StartsWith("team.userEmail.", key.Key));
    }
}
