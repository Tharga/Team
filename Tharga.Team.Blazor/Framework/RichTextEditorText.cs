namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings for the formatted-text editor's toolbar and link dialog, shared by every place that writes formatted text.</summary>
public static class RichTextEditorText
{
    /// <summary>The toolbar button that makes text bold.</summary>
    public static readonly TextKey Bold = new("team.richText.bold", "Bold");

    /// <summary>The toolbar button that makes text italic.</summary>
    public static readonly TextKey Italic = new("team.richText.italic", "Italic");

    /// <summary>The toolbar button that underlines text.</summary>
    public static readonly TextKey Underline = new("team.richText.underline", "Underline");

    /// <summary>The toolbar button that starts a bulleted list.</summary>
    public static readonly TextKey BulletedList = new("team.richText.bulletedList", "Bulleted list");

    /// <summary>The toolbar button that starts a numbered list.</summary>
    public static readonly TextKey NumberedList = new("team.richText.numberedList", "Numbered list");

    /// <summary>The toolbar button that adds or changes a link.</summary>
    public static readonly TextKey Link = new("team.richText.link", "Insert link");

    /// <summary>The label of the link dialog's address field.</summary>
    public static readonly TextKey LinkAddress = new("team.richText.linkAddress", "Web address");

    /// <summary>The label of the link dialog's text field.</summary>
    public static readonly TextKey LinkText = new("team.richText.linkText", "Text");

    /// <summary>The link dialog's choice to open in a new window; every link does regardless.</summary>
    public static readonly TextKey LinkNewWindow = new("team.richText.linkNewWindow", "Open in new window");

    /// <summary>The link dialog's confirming button.</summary>
    public static readonly TextKey LinkOk = new("team.richText.linkOk", "OK");

    /// <summary>The link dialog's cancelling button.</summary>
    public static readonly TextKey LinkCancel = new("team.richText.linkCancel", "Cancel");

    /// <summary>The toolbar button that removes formatting from the selection.</summary>
    public static readonly TextKey RemoveFormatting = new("team.richText.removeFormatting", "Remove formatting");

    /// <summary>The toolbar button that undoes the last change.</summary>
    public static readonly TextKey Undo = new("team.richText.undo", "Undo");

    /// <summary>The toolbar button that redoes what was undone.</summary>
    public static readonly TextKey Redo = new("team.richText.redo", "Redo");

    /// <summary>The toolbar's text style picker, as its tooltip and as its label while the selection is in a style it does not offer.</summary>
    public static readonly TextKey TextStyle = new("team.richText.textStyle", "Text style");

    /// <summary>The text style picker's choice for ordinary body text.</summary>
    public static readonly TextKey Paragraph = new("team.richText.paragraph", "Paragraph");

    /// <summary>The text style picker's choice for a heading.</summary>
    public static readonly TextKey Heading = new("team.richText.heading", "Heading");

    /// <summary>The text style picker's choice for a heading under a heading.</summary>
    public static readonly TextKey Subheading = new("team.richText.subheading", "Subheading");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Bold, Italic, Underline, BulletedList, NumberedList, Link, LinkAddress, LinkText, LinkNewWindow, LinkOk, LinkCancel, RemoveFormatting, Undo, Redo, TextStyle, Paragraph, Heading, Subheading];
}
