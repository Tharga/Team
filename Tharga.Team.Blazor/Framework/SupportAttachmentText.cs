namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings for files on support messages, shared by the transcript and every answer box.</summary>
public static class SupportAttachmentText
{
    /// <summary>The button that chooses files to send with an answer.</summary>
    public static readonly TextKey Attach = new("team.support.attachments.attach", "Attach files");

    /// <summary>Names the button removing one chosen file; <c>{0}</c> is the file name.</summary>
    public static readonly TextKey Remove = new("team.support.attachments.remove", "Remove {0}");

    /// <summary>Names the files chosen to send with an answer.</summary>
    public static readonly TextKey Chosen = new("team.support.attachments.chosen", "Files to send");

    /// <summary>Names the files a message in the transcript carries.</summary>
    public static readonly TextKey Attached = new("team.support.attachments.attached", "Attached files");

    /// <summary>Names a file that opens in a new tab; <c>{0}</c> is the file name.</summary>
    public static readonly TextKey Open = new("team.support.attachments.open", "Open {0}");

    /// <summary>Names a file that is saved rather than opened; <c>{0}</c> is the file name.</summary>
    public static readonly TextKey Save = new("team.support.attachments.save", "Save {0}");

    /// <summary>Shown when a listed file could not be opened; <c>{0}</c> is the file name.</summary>
    public static readonly TextKey NotAvailable = new("team.support.attachments.notAvailable", "{0} could not be opened.");

    /// <summary>Shown when more files were chosen than may be sent; <c>{0}</c> is the most allowed.</summary>
    public static readonly TextKey TooMany = new("team.support.attachments.tooMany", "At most {0} files can be attached.");

    /// <summary>Shown when one chosen file is too large; <c>{0}</c> is its name, <c>{1}</c> its size, <c>{2}</c> the most one file may be.</summary>
    public static readonly TextKey TooLarge = new("team.support.attachments.tooLarge", "{0} is {1}, and one file may be at most {2}.");

    /// <summary>Shown when a chosen file would take the files past their total; <c>{0}</c> is its name, <c>{1}</c> the most all files may be.</summary>
    public static readonly TextKey TooLargeTogether = new("team.support.attachments.tooLargeTogether", "{0} was not added: together the files may be at most {1}.");

    /// <summary>Shown when a chosen file could not be read from the browser; <c>{0}</c> is its name.</summary>
    public static readonly TextKey Unreadable = new("team.support.attachments.unreadable", "{0} could not be read.");

    /// <summary>The button that chooses files only support's staff and the member may open.</summary>
    public static readonly TextKey AttachSecure = new("team.support.attachments.attachSecure", "Attach files for support only");

    /// <summary>Names the files chosen for support only.</summary>
    public static readonly TextKey SecureChosen = new("team.support.attachments.secureChosen", "Files for support only");

    /// <summary>Says who may open files for support only, when they are kept until support removes them.</summary>
    public static readonly TextKey SecureNote = new("team.support.attachments.secureNote", "Only support staff and you can open these files. They are never sent by mail.");

    /// <summary>Says who may open files for support only and when they go; <c>{0}</c> is how many days they are kept.</summary>
    public static readonly TextKey SecureNoteKeepFor = new("team.support.attachments.secureNoteKeepFor", "Only support staff and you can open these files. They are never sent by mail, and are removed after {0} days.");

    /// <summary>Marks a file in the transcript that only support's staff and its uploader may open.</summary>
    public static readonly TextKey SecureFile = new("team.support.attachments.secureFile", "For support only");

    /// <summary>Stands for a file this reader may not see; <c>{0}</c> is its size.</summary>
    public static readonly TextKey Withheld = new("team.support.attachments.withheld", "A file for support only ({0})");

    /// <summary>Stands for a file this reader may not see whose content is gone.</summary>
    public static readonly TextKey WithheldRemoved = new("team.support.attachments.withheldRemoved", "A file for support only, removed");

    /// <summary>Stands for a file whose content was removed; <c>{0}</c> is its name.</summary>
    public static readonly TextKey Removed = new("team.support.attachments.removed", "{0}, removed");

    /// <summary>The button with which support removes a secure file before its time; <c>{0}</c> is its name.</summary>
    public static readonly TextKey RemoveSecure = new("team.support.attachments.removeSecure", "Remove {0} now");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Attach, Remove, Chosen, Attached, Open, Save, NotAvailable, TooMany, TooLarge, TooLargeTogether, Unreadable,
        AttachSecure, SecureChosen, SecureNote, SecureNoteKeepFor, SecureFile, Withheld, WithheldRemoved, Removed, RemoveSecure
    ];
}
