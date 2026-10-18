using MimeKit;

namespace Tharga.Team.Email.Users;

/// <summary>Who contact mail is from, where replies go and which product it names: the contact options where set, otherwise those of mail to users.</summary>
internal static class ContactMailSender
{
    /// <summary>The sending address.</summary>
    public static string Address(UserMailOptions mail, ContactMessageOptions contact) => First(contact?.FromAddress, mail?.Mail?.FromAddress);

    /// <summary>The sending name.</summary>
    public static string Name(UserMailOptions mail, ContactMessageOptions contact) => First(contact?.FromName, mail?.Mail?.FromName);

    /// <summary>Where replies go, or null for the sending address.</summary>
    public static string ReplyTo(UserMailOptions mail, ContactMessageOptions contact) => First(contact?.ReplyTo, mail?.ReplyTo);

    /// <summary>The product the footer and the unsubscribe page name: <see cref="UserMailOptions.ProductName"/>, then the sending name, then the sending address.</summary>
    public static string Product(UserMailOptions mail, ContactMessageOptions contact) => First(mail?.ProductName, Name(mail, contact), Address(mail, contact)) ?? string.Empty;

    /// <summary>The sender as a recipient sees it, name and address; null while there is no sending address.</summary>
    public static string Display(UserMailOptions mail, ContactMessageOptions contact)
    {
        var address = Address(mail, contact);
        if (address == null) return null;

        var name = Name(mail, contact);
        return name == null ? address : new MailboxAddress(name, address).ToString();
    }

    /// <summary>The transport settings contact mail is sent with: the SMTP server of mail to users, and the contact sender.</summary>
    public static MailOptions MailOptions(UserMailOptions mail, ContactMessageOptions contact)
    {
        var source = mail?.Mail ?? new MailOptions();
        var options = new MailOptions { FromAddress = Address(mail, contact), FromName = Name(mail, contact), Timeout = source.Timeout };

        options.Smtp.Host = source.Smtp.Host;
        options.Smtp.Port = source.Smtp.Port;
        options.Smtp.UseSsl = source.Smtp.UseSsl;
        options.Smtp.UserName = source.Smtp.UserName;
        options.Smtp.Password = source.Smtp.Password;

        return options;
    }

    private static string First(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
}
