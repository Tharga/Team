namespace Tharga.Team.Blazor.Features.Support;

/// <summary>How a message looks as a conversation bubble, shared by every view that shows one so they stay alike.</summary>
internal static class ConversationBubble
{
    /// <summary>The round badge beside a bubble that has no avatar, such as support's or the assistant's.</summary>
    public const string IconCircle = "display: inline-flex; align-items: center; justify-content: center; width: 32px; height: 32px; border-radius: 50%; background: var(--rz-secondary-lighter); color: var(--rz-secondary);";

    /// <summary>A bubble on the reader's own side.</summary>
    public const string Right = "background: var(--rz-primary-lighter); color: var(--rz-text-color); border-bottom-right-radius: 0.25rem;";

    /// <summary>A bubble on the other side: a tint of the theme's own text colour, because Radzen's dark themes keep the base scale light, so <c>--rz-base-200</c> under light text is unreadable.</summary>
    public const string Left = "background: var(--rz-secondary-lighter); background: color-mix(in srgb, var(--rz-text-color) 8%, transparent); color: var(--rz-text-color); border-bottom-left-radius: 0.25rem;";

    /// <summary>The bubble's own frame, before its side's colours.</summary>
    public const string Frame = "max-width: 75%; min-width: 0; padding: 0.5rem 0.75rem; border-radius: 0.75rem;";
}
