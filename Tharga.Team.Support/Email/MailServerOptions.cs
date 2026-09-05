namespace Tharga.Team.Support.Email;

/// <summary>
/// Connection settings for one mail server — the IMAP side or the SMTP side.
/// </summary>
/// <remarks>
/// One type for both because the fields are the same. They are configured separately because the hosts
/// routinely differ, and a single "mail server" setting is the shape that forces a host with split
/// infrastructure to give up and implement its own transport.
/// </remarks>
public class MailServerOptions
{
    /// <summary>Server host name. Until this is set the transport does nothing at all.</summary>
    public string Host { get; set; }

    /// <summary>Port. Unset means the default for the protocol and <see cref="UseSsl"/>.</summary>
    public int? Port { get; set; }

    /// <summary>Whether to connect over TLS. Default <c>true</c>.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>Account the toolkit signs in as. Unset means an anonymous connection.</summary>
    public string UserName { get; set; }

    /// <summary>Password or app password for <see cref="UserName"/>.</summary>
    public string Password { get; set; }

    /// <summary>
    /// Whether this side of the transport has been configured at all.
    /// </summary>
    /// <remarks>
    /// <b>One definition, so registration and the client cannot disagree.</b> Registration decides whether
    /// to wire a channel and a poller, and the client decides whether to attempt a connection; the two
    /// answering that question separately is how a host ends up with a poller that can never read.
    /// </remarks>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
