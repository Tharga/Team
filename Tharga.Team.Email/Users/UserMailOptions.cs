namespace Tharga.Team.Email.Users;

/// <summary>How mail to users is sent: the account and addresses it goes out from, the categories it goes out in, where unsubscribe links lead, and how the queue retries.</summary>
/// <remarks>
/// <b>There is no default sending address, and no no-reply one.</b> Until <see cref="MailOptions.FromAddress"/> on
/// <see cref="Mail"/> and its SMTP host are set, mail is queued and kept but nothing is sent, which is logged once.
/// Set them to an address a reply reaches; <see cref="ReplyTo"/> sends replies somewhere else when that is wanted.
/// </remarks>
public class UserMailOptions
{
    /// <summary>The default for <see cref="UnsubscribePath"/>.</summary>
    public const string DefaultUnsubscribePath = "/mail/unsubscribe";

    /// <summary>The highest <see cref="MaxAttachmentBytes"/> accepted, so a queued mail always fits in one stored document.</summary>
    public const long AttachmentBytesCeiling = 15L * 1024 * 1024;

    private readonly List<UserMailCategory> _categories = [];

    /// <summary>The SMTP server, <see cref="MailOptions.FromAddress"/> and <see cref="MailOptions.FromName"/> mail goes out with; the IMAP side is not used.</summary>
    public MailOptions Mail { get; set; } = new();

    /// <summary>Where replies go when not to the sending address; null sends them to the sending address.</summary>
    public string ReplyTo { get; set; }

    /// <summary>The absolute address of the site, such as <c>https://app.example.com</c>, that unsubscribe links are built on; required once an optional category is registered.</summary>
    public string BaseUrl { get; set; }

    /// <summary>The path <c>MapThargaMailUnsubscribe</c> answers on and unsubscribe links lead to, default <c>/mail/unsubscribe</c>.</summary>
    public string UnsubscribePath { get; set; } = DefaultUnsubscribePath;

    /// <summary>The version written into unsubscribe links; raising it makes every link already sent invalid, default 1.</summary>
    public int UnsubscribeTokenVersion { get; set; } = 1;

    /// <summary>The product's name, as the unsubscribe page says other mail from it is not affected; null uses <see cref="MailOptions.FromName"/> on <see cref="Mail"/>, then its sending address.</summary>
    public string ProductName { get; set; }

    /// <summary>Where the host's mail settings are, a site path such as <c>/profile/mail</c> or an absolute address; the unsubscribe page links there, and returns there after sign-in. Null shows no such link.</summary>
    public string PreferencesPath { get; set; }

    /// <summary>Where a user signs in, a site path or an absolute address; the unsubscribe page links there with <c>returnUrl</c> set to <see cref="PreferencesPath"/> for anyone not signed in as the user the link is for. Null shows no such link.</summary>
    public string SignInPath { get; set; }

    /// <summary>Style rules the unsubscribe page adds after its own, inside its one inline style element, so it looks like the host; the page's policy loads nothing from elsewhere, so no images or fonts by address.</summary>
    public string PageCss { get; set; }

    /// <summary>How many times sending one mail is attempted before it is given up, default 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>How long after the first failed attempt the next is made, doubling after each, default one minute.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The longest wait between two attempts, default one hour.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long a sender holds a mail it claimed before another instance may take it, default five minutes.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the queue is looked at when it was last found empty, default 30 seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The most mails one pass sends before looking again at once, default 50.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>The most all files on one mail may add up to, in bytes; at most 15 MB, default 10 MB.</summary>
    public long MaxAttachmentBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>The most files one mail may carry, default 10.</summary>
    public int MaxAttachmentCount { get; set; } = 10;

    /// <summary>The most characters the HTML and text bodies of one mail may hold together, default 200 000.</summary>
    public int MaxBodyLength { get; set; } = 200_000;

    /// <summary>Whether the built-in service messages category is required, default true; false lets each user turn it off — on until they do — with an unsubscribe link in its mail, as any optional category.</summary>
    /// <remarks>The account category is always required. Making service messages optional makes <see cref="BaseUrl"/> required, as any optional category does.</remarks>
    public bool ServiceRequired { get; set; } = true;

    /// <summary>Where the terms of use are, as a site path such as <c>/terms</c> or an absolute address; a required category reads as always sent, part of the terms of use, linking here, and says the same with no link when unset.</summary>
    public string TermsUrl { get; set; }

    /// <summary>The host's own categories, in the order they were added; the built-in ones in <see cref="UserMailCategories"/> come first and are not listed here.</summary>
    public IReadOnlyList<UserMailCategory> Categories => _categories;

    /// <summary>Registers a category; its key must be unique and not one of the built-in ones.</summary>
    public UserMailOptions AddCategory(UserMailCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        _categories.Add(category);
        return this;
    }
}
