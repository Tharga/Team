namespace Tharga.Team.Email.Users;

/// <summary>How staff write to contacts — people who are not users of the product: the categories contact mail goes out in, who it says it is from, and who its footer says sends it.</summary>
/// <remarks>
/// Contact mail goes out through the SMTP server of mail to users (<see cref="UserMailOptions.Mail"/>), through its queue
/// and once its sending address and SMTP host are set, and carries an unsubscribe link built on
/// <see cref="UserMailOptions.BaseUrl"/>, which is therefore required.
/// </remarks>
public class ContactMessageOptions
{
    /// <summary>The highest <see cref="MaxAddressees"/> accepted, so the log of one message, which keeps its list, fits in one stored document.</summary>
    public const int AddresseesCeiling = 50_000;

    private readonly List<ContactMailCategory> _categories = [];

    /// <summary>The most rows one list of addressees may hold; at most 50 000, default 10 000.</summary>
    public int MaxAddressees { get; set; } = 10_000;

    /// <summary>The address contact mail is sent from; null uses <see cref="MailOptions.FromAddress"/> of mail to users. Use an address a reply reaches.</summary>
    public string FromAddress { get; set; }

    /// <summary>The name contact mail is sent from; null uses <see cref="MailOptions.FromName"/> of mail to users.</summary>
    public string FromName { get; set; }

    /// <summary>Where replies to contact mail go; null uses <see cref="UserMailOptions.ReplyTo"/>, and with that unset the sending address.</summary>
    public string ReplyTo { get; set; }

    /// <summary>Who sends the mail, as its footer says, such as the company's name and postal address; null names the product.</summary>
    /// <remarks>Mail to people who did not ask for it should say who sends it and where they can be reached. The footer reads "Sent by {0}." with this in its place, through the text key <c>team.contactMail.footer.sender</c>; a line break is kept.</remarks>
    public string SenderLine { get; set; }

    /// <summary>The categories contact mail may be sent in, in the order they were added.</summary>
    public IReadOnlyList<ContactMailCategory> Categories => _categories;

    /// <summary>Registers a category; its key must be unique and not the key of a category of mail to users.</summary>
    public ContactMessageOptions AddCategory(ContactMailCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        _categories.Add(category);
        return this;
    }
}
