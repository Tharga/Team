namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// What <see cref="SupportPanel"/> does on a screen 768 pixels wide or less.
/// </summary>
public enum SupportPanelNarrowScreen
{
    /// <summary>Not shown. The host's support page is where to ask.</summary>
    Hide,

    /// <summary>
    /// Over the page. Open, it is full height and at most 85% of the screen wide; collapsed, it is one floating button
    /// in the bottom-right corner.
    /// </summary>
    Drawer
}
