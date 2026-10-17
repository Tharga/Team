using Bunit;
using Microsoft.AspNetCore.Components;
using Radzen.Blazor;

namespace Tharga.Team.Blazor.Tests;

/// <summary>Types into a formatted-text editor as its script does in a browser: by reporting the editor's HTML as it changes.</summary>
internal static class RichTextTyping
{
    public static void TypeRichText<TComponent>(this IRenderedComponent<TComponent> view, string html, int editor = 0)
        where TComponent : class, IComponent
    {
        var target = view.FindComponents<RadzenHtmlEditor>()[editor];

        view.InvokeAsync(() => target.Instance.OnChange(html)).GetAwaiter().GetResult();
    }
}
