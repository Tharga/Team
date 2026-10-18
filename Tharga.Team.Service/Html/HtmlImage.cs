namespace Tharga.Team.Service.Html;

/// <summary>What becomes of an image a body names by Content-ID: its reference kept, shown from data the caller read, or replaced by text.</summary>
public sealed record HtmlImage
{
    private HtmlImage()
    {
    }

    /// <summary>The <c>data:</c> URI the image is shown from, or null when it is not shown from data.</summary>
    public string Source { get; private init; }

    /// <summary>What stands in the image's place when it is not shown and carries no alt text of its own.</summary>
    public string Text { get; private init; }

    /// <summary>Whether the <c>cid:</c> reference stays as written, for a mail that carries the image beside its body.</summary>
    public bool KeepsReference { get; private init; }

    /// <summary>Keeps the <c>cid:</c> reference as written.</summary>
    public static HtmlImage Keep { get; } = new() { KeepsReference = true };

    /// <summary>Shows the image from its content, or returns null when its type is not one a browser shows without running anything.</summary>
    public static HtmlImage FromContent(string contentType, ReadOnlySpan<byte> content)
    {
        var mediaType = SafeHtml.ImageMediaType(contentType);

        return mediaType == null ? null : new HtmlImage { Source = $"data:{mediaType};base64,{Convert.ToBase64String(content)}" };
    }

    /// <summary>Replaces the image with its alt text, or with this text when it has none.</summary>
    public static HtmlImage Described(string text) => new() { Text = text };
}
