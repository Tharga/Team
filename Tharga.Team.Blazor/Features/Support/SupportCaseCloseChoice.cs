namespace Tharga.Team.Blazor.Features.Support;

/// <summary>What the close dialog was confirmed with: the visibility to keep the case at, or null for no change to make.</summary>
public sealed record SupportCaseCloseChoice(SupportCaseVisibility? Visibility);
