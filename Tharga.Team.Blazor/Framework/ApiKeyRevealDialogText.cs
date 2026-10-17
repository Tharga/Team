namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>ApiKeyRevealDialog</c>, shown right after a key is created or regenerated.</summary>
public static class ApiKeyRevealDialogText
{
    /// <summary>When the key is not locked on creation, so its value stays readable from the key list.</summary>
    public static readonly TextKey RemainsRetrievable = new(
        "team.apiKeyReveal.remainsRetrievable",
        "Copy this key now, or read it later from the key list — it stays stored until the key is locked. Lock it once you have copied it, to clear the stored value.");

    /// <summary>When the key is locked on creation (<c>ApiKeyOptions.AutoLockKeys</c>), so this is the only chance to read it.</summary>
    public static readonly TextKey ShownOnce = new(
        "team.apiKeyReveal.shownOnce",
        "Copy this key now. For security it is shown only once and is not stored — you will not be able to retrieve it again. If you lose it, regenerate the key to get a new one.");

    public static readonly TextKey Copy = new("team.apiKeyReveal.copy", "Copy");

    public static readonly TextKey Done = new("team.apiKeyReveal.done", "Done");

    public static readonly TextKey Copied = new("team.apiKeyReveal.copied", "API key copied to the clipboard.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [RemainsRetrievable, ShownOnce, Copy, Done, Copied];
}
