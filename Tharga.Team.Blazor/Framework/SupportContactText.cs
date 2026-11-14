namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportContactTable</c> — who wrote through the host's contact form, as a table of
/// labels and values, with the copy buttons beside the address and the phone number.
/// </summary>
public static class SupportContactText
{
    /// <summary>Heading of the table.</summary>
    public static readonly TextKey Title = new("team.support.contact.title", "Contact");

    /// <summary>Label of the sender's name.</summary>
    public static readonly TextKey Name = new("team.support.contact.name", "Name");

    /// <summary>Label of the sender's email address.</summary>
    public static readonly TextKey Email = new("team.support.contact.email", "E-mail");

    /// <summary>Label of the sender's phone number.</summary>
    public static readonly TextKey Phone = new("team.support.contact.phone", "Phone");

    /// <summary>Label of the sender's organisation.</summary>
    public static readonly TextKey Organisation = new("team.support.contact.organisation", "Organisation");

    /// <summary>Label of the time the sender consented to their data being processed.</summary>
    public static readonly TextKey Consent = new("team.support.contact.consent", "Consent");

    /// <summary>Shown as the consent when none was recorded: staff should see the absence rather than a missing row.</summary>
    public static readonly TextKey NoConsent = new("team.support.contact.noConsent", "No consent recorded");

    /// <summary>The copy button's tooltip and accessible name.</summary>
    public static readonly TextKey Copy = new("team.support.contact.copy", "Copy");

    /// <summary>The notice shown after a value was copied.</summary>
    public static readonly TextKey Copied = new("team.support.contact.copied", "Copied");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Title, Name, Email, Phone, Organisation, Consent, NoConsent, Copy, Copied];
}
