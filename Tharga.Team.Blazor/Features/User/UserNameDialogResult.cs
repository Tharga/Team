namespace Tharga.Team.Blazor.Features.User;

/// <summary>What <c>UserNameDialog</c> saved, for the page to update the user's row and say what happened.</summary>
/// <param name="Name">The name now held here.</param>
/// <param name="Verification">The directory as the dialog read it, with the name it now holds, or null when it was not read.</param>
/// <param name="TakenFromDirectory">The name here was taken from the directory.</param>
/// <param name="DirectoryUpdated">The name was written to the directory.</param>
/// <param name="DirectoryError">The rename stood here, and writing it to the directory failed for this reason.</param>
internal sealed record UserNameDialogResult(
    string Name,
    DirectoryVerificationResult Verification,
    bool TakenFromDirectory = false,
    bool DirectoryUpdated = false,
    string DirectoryError = null);
