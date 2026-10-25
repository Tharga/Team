using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Renaming a user in one dialog: it shows the name the directory holds, can take that name or write the entered one
/// there, and asks before either. A rename that stays here is saved at once.
/// </summary>
public class UserNameDialogRenderTests : BunitContext
{
    private const string UserKey = "u-1";

    private readonly Mock<IUserManagementService> _users = new();

    private IRenderedComponent<UserNameDialog> Render(string currentName = "Ann", string directoryName = "unknown", bool readDirectory = true, bool allowWrite = true)
    {
        _users.Setup(x => x.VerifyUserAsync(UserKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryVerificationResult(DirectoryUserStatus.Found, "oid-1", directoryName));
        _users.Setup(x => x.SetUserNameAsync(UserKey, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, bool write, CancellationToken _) => new UserNameChangeResult(DirectoryUpdated: write));
        _users.Setup(x => x.UseDirectoryNameAsync(UserKey, It.IsAny<CancellationToken>())).ReturnsAsync(directoryName);

        Services.AddSingleton(_users.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<UserNameDialog>(p => p
            .Add(x => x.UserKey, UserKey)
            .Add(x => x.CurrentName, currentName)
            .Add(x => x.ReadDirectory, readDirectory)
            .Add(x => x.AllowWriteToDirectory, allowWrite));
    }

    private static async Task TickWriteToDirectoryAsync(IRenderedComponent<UserNameDialog> view)
    {
        var box = view.FindComponent<RadzenCheckBox<bool>>();
        await view.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(true));
    }

    [Fact]
    public void TheDirectorysName_IsShown_AndCanBeUsed()
    {
        var view = Render(directoryName: "Ann Larsson");

        Assert.Contains("Ann Larsson", view.Find("[data-directory-name]").TextContent);

        view.Find("button[data-use-directory-name]").Click();

        Assert.Equal("Ann Larsson", view.Find("input[data-name]").GetAttribute("value"));
    }

    /// <summary>"unknown" is the directory saying it has no name; there is nothing to take.</summary>
    [Fact]
    public void APlaceholderInTheDirectory_CannotBeUsed()
    {
        var view = Render(directoryName: "unknown");

        Assert.Empty(view.FindAll("button[data-use-directory-name]"));
    }

    [Fact]
    public void TheDirectoryPart_IsFramedAndTitledEntra()
    {
        var view = Render();

        Assert.Contains("Entra", view.Find("[data-directory] legend").TextContent);
        Assert.NotNull(view.Find("[data-directory] [data-directory-name]"));
    }

    /// <summary>The frame shows what Entra holds; the box is a choice about this rename, so it sits below it.</summary>
    [Fact]
    public void TheWriteBox_SitsOutsideTheEntraFrame()
    {
        var view = Render();

        Assert.NotEmpty(view.FindAll("[data-write-to-directory]"));
        Assert.Empty(view.FindAll("[data-directory] [data-write-to-directory]"));
    }

    /// <summary>A directory that holds no real name has nothing to give, so the likely intent is to give it ours.</summary>
    [Fact]
    public void NoNameInTheDirectory_WritesOursThereByDefault()
    {
        var view = Render(currentName: "Ann Larsson", directoryName: "unknown");

        Assert.True(view.FindComponent<RadzenCheckBox<bool>>().Instance.Value);
        Assert.Empty(view.FindAll("button[data-use-directory-name]"));
    }

    [Fact]
    public void ARealNameInTheDirectory_IsNotOverwrittenByDefault()
    {
        var view = Render(currentName: "Ann", directoryName: "Annie");

        Assert.False(view.FindComponent<RadzenCheckBox<bool>>().Instance.Value);
    }

    /// <summary>Nothing to write without a real name here: the box stays in view, disabled and unticked.</summary>
    [Fact]
    public void NoNameHereEither_DisablesTheWriteBox()
    {
        var view = Render(currentName: null, directoryName: "unknown");

        var box = view.FindComponent<RadzenCheckBox<bool>>().Instance;
        Assert.True(box.Disabled);
        Assert.False(box.Value);
    }

    /// <summary>After "Use this name" the two names agree, so writing to Entra would change nothing.</summary>
    [Fact]
    public async Task UsingEntrasName_DisablesAndUnticksTheWriteBox_UntilTheNameDiffersAgain()
    {
        var view = Render(currentName: "Ann", directoryName: "Ann Larsson");
        var box = view.FindComponent<RadzenCheckBox<bool>>();
        await view.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(true));
        Assert.True(box.Instance.Value);

        view.Find("button[data-use-directory-name]").Click();
        Assert.True(box.Instance.Disabled);
        Assert.False(box.Instance.Value);

        view.Find("input[data-name]").Input("Ann B. Larsson");
        Assert.False(box.Instance.Disabled);
        Assert.False(box.Instance.Value);
    }

    [Fact]
    public void ATypedRename_IsSavedAtOnce_AndStaysHere()
    {
        var view = Render(directoryName: "Annie");

        view.Find("input[data-name]").Input("Ann Larsson");
        view.Find("button[data-save]").Click();

        Assert.Empty(view.FindAll("[data-confirm]"));
        _users.Verify(x => x.SetUserNameAsync(UserKey, "Ann Larsson", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WritingToTheDirectory_AsksFirst_AndOkWrites()
    {
        var view = Render();

        view.Find("input[data-name]").Input("Ann Larsson");
        await TickWriteToDirectoryAsync(view);
        view.Find("button[data-save]").Click();

        Assert.Contains("unknown", view.Find("[data-change-directory]").TextContent);
        Assert.Contains("Ann Larsson", view.Find("[data-change-directory]").TextContent);
        _users.Verify(x => x.SetUserNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

        view.Find("button[data-ok]").Click();

        _users.Verify(x => x.SetUserNameAsync(UserKey, "Ann Larsson", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TakingTheDirectorysName_AsksFirst_AndOkTakesIt()
    {
        var view = Render(directoryName: "Ann Larsson");

        view.Find("button[data-use-directory-name]").Click();
        view.Find("button[data-save]").Click();

        Assert.Contains("Ann Larsson", view.Find("[data-change-here]").TextContent);
        _users.Verify(x => x.UseDirectoryNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        view.Find("button[data-ok]").Click();

        _users.Verify(x => x.UseDirectoryNameAsync(UserKey, It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(x => x.SetUserNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancellingTheConfirmation_GoesBack_AndChangesNothing()
    {
        var view = Render();

        view.Find("input[data-name]").Input("Ann Larsson");
        await TickWriteToDirectoryAsync(view);
        view.Find("button[data-save]").Click();
        view.FindAll("button").Last().Click();

        Assert.Empty(view.FindAll("[data-confirm]"));
        Assert.Equal("Ann Larsson", view.Find("input[data-name]").GetAttribute("value"));
        _users.Verify(x => x.SetUserNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The host's switch decides whether the directory may be written at all.</summary>
    [Fact]
    public void WithoutTheHostsPermission_ThereIsNoWriteOption()
    {
        var view = Render(allowWrite: false);

        Assert.NotEmpty(view.FindAll("[data-directory-name]"));
        Assert.Empty(view.FindComponents<RadzenCheckBox<bool>>());
    }

    [Fact]
    public void WithoutDirectoryFeatures_TheDirectoryIsNotRead()
    {
        var view = Render(readDirectory: false);

        Assert.Empty(view.FindAll("[data-directory-name]"));
        _users.Verify(x => x.VerifyUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
