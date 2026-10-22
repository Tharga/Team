using Tharga.Team.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// How support cases reach the outside world.
/// </summary>
/// <remarks>
/// Separate from the notification options on purpose: a host may want Slack notifications without support
/// cases, or support cases without Slack. Neither should configure the other into existence.
/// <para>
/// <b>Why <see cref="Email"/> is a section while the Slack settings are flat.</b> Not taste, and not worth
/// tidying: <see cref="SlackChannel"/> and <see cref="SigningSecret"/> shipped at this level, and moving them
/// for symmetry would break the configuration of every host that has set them. A breaking change buys
/// nothing here. The reshape belongs in the release that is already breaking.
/// </para>
/// <para>
/// <b>Why mail is configured here and Slack's token is not.</b> The Slack transport is configured on
/// <c>AddThargaSupport</c> because notifications use it too. Nothing but support cases sends or reads mail,
/// so putting it there would force a host wanting email cases to register the notification module — a sink
/// and a hosted service it never asked for.
/// </para>
/// </remarks>
public class SupportCaseOptions
{
    /// <summary>
    /// Channel that support-case threads are posted to — a name (<c>#support</c>) or an id
    /// (<c>C0123456789</c>). Leave unset to keep cases entirely on the site.
    /// </summary>
    /// <remarks>
    /// <b>Unset is a supported configuration, not a misconfiguration.</b> A case with no channel is complete
    /// and trackable; the projection is optional. Nothing warns about this being empty.
    /// </remarks>
    public string SlackChannel { get; set; }

    /// <summary>
    /// Secret Slack signs inbound event requests with. Required only to receive replies from Slack.
    /// </summary>
    /// <remarks>
    /// Without it the inbound endpoint refuses everything, which is the correct failure: an endpoint that
    /// cannot verify a signature must not accept the request rather than trusting it.
    /// </remarks>
    public string SigningSecret { get; set; }

    /// <summary>
    /// Whether a component asks for a subject when a case is raised. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <b>A hint to the UI, not a rule the service enforces</b>, and the distinction is deliberate. With this
    /// off, a component shows no subject field and the message supplies one. With it on, the field appears —
    /// but a case raised without a subject anyway still gets a derived one, because
    /// <see cref="SupportCase.Subject"/> is not nullable and a half-filled form must not produce a case that
    /// renders as a blank line in every list.
    /// <para>
    /// So this decides what a person is asked for, never whether the case ends up with a subject.
    /// </para>
    /// </remarks>
    public bool UseSubject { get; set; }

    /// <summary>
    /// How long a case waits on the customer before it closes itself. Default 7 days;
    /// <see cref="TimeSpan.Zero"/> turns it off.
    /// </summary>
    /// <remarks>
    /// <b>The clock runs only while the case is waiting on the *customer*</b> — support answered and nobody
    /// came back. A case whose newest entry is the customer's is waiting on support, and closing that would
    /// hide the backlog rather than tidy it.
    /// <para>
    /// <b>Zero registers no background work at all</b>, rather than registering a sweep that finds nothing. A
    /// host that does not want cases closing itself runs nothing on its behalf.
    /// </para>
    /// </remarks>
    public TimeSpan AutoCloseAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How often the sweep runs. Default one hour.</summary>
    /// <remarks>
    /// It decides only how promptly a case closes after it becomes eligible, so an hour is generous for a
    /// span measured in days. Frequent sweeps cost a query and buy nothing.
    /// </remarks>
    public TimeSpan AutoCloseSweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Most cases one sweep will close, so a large backlog cannot become one enormous pass.</summary>
    public int AutoCloseBatchSize { get; set; } = 100;

    /// <summary>
    /// The access level that is granted <c>support:read</c> and <c>support:manage</c> automatically.
    /// Default <see cref="AccessLevel.Administrator"/>; <c>null</c> registers them so that no access level
    /// grants them at all.
    /// </summary>
    /// <remarks>
    /// <b>This decides who reads other people's conversations, which is not the same question for every
    /// product.</b> Where support is a shared team function, the default is right: an administrator of the
    /// team can see what their team has raised. Where a support conversation is <i>personal</i> — one
    /// member talking to the product's own staff — it is wrong, and a member's manager reading it is a
    /// different product from the one being built. The scope's own description says why the stake is high:
    /// a case holds whatever a user typed into it.
    /// <para>
    /// <b><c>null</c> means grant-only, not unregistered.</b> The scopes still appear in the catalogue with
    /// their descriptions and are still enforced; they are simply granted by no access level, rejected in
    /// tenant-defined custom roles, and left out of the scope-override pickers. Holding one then requires a
    /// code-registered tenant role or an explicit override — a recorded decision rather than a consequence
    /// of being an Owner. See <see cref="ScopeRegistry.RegisterGrantOnly(string,string)"/>.
    /// </para>
    /// <para>
    /// <b>Only some values differ from each other.</b> Owner and Administrator are granted every registered
    /// scope regardless of the declared minimum, so <see cref="AccessLevel.Owner"/> behaves exactly as the
    /// default does. The choices that actually differ are <see cref="AccessLevel.Administrator"/>,
    /// <see cref="AccessLevel.User"/>, <see cref="AccessLevel.Viewer"/> and <c>null</c>. Do not use
    /// <see cref="AccessLevel.Custom"/> to mean "nobody": it grants the scope to <i>every</i> level, which
    /// is the trap <see cref="ScopeRegistry.RegisterGrantOnly(string,string)"/> exists to avoid.
    /// </para>
    /// <para>
    /// <b>Changing this does not change enforcement</b>, only who is granted. Every check still runs
    /// exactly as before, and the author of a case still reaches their own without holding anything.
    /// </para>
    /// </remarks>
    public AccessLevel? TeamScopeAccessLevel { get; set; } = AccessLevel.Administrator;

    /// <summary>
    /// Whether the team's support readers reach every case, or only the ones each author shares.
    /// Default <see cref="SupportCaseVisibilityMode.TeamWide"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TeamScopeAccessLevel"/> decides <i>who</i> holds the team's support scopes;
    /// this decides <i>which cases</i> those scopes reach. Under <see cref="SupportCaseVisibilityMode.PerCase"/>
    /// a case starts private and its author may share it while it is open, so a conversation that concerns
    /// the team can be shared without every personal one being readable by an administrator.
    /// </remarks>
    public SupportCaseVisibilityMode Visibility { get; set; } = SupportCaseVisibilityMode.TeamWide;

    /// <summary>
    /// What a new case starts as under <see cref="SupportCaseVisibilityMode.PerCase"/>. Default
    /// <see cref="SupportCaseVisibility.Private"/>; the author may change it either way while the case is open.
    /// </summary>
    public SupportCaseVisibility DefaultVisibility { get; set; } = SupportCaseVisibility.Private;

    /// <summary>
    /// What a conversation with the assistant starts as, when it differs from <see cref="DefaultVisibility"/>;
    /// <c>null</c> uses that. A conversation handed to a person takes <see cref="DefaultVisibility"/> then.
    /// </summary>
    public SupportCaseVisibility? AssistantDefaultVisibility { get; set; }

    /// <summary>
    /// How long after support's last answer the product's own staff may close a case themselves; <c>null</c>
    /// lets them close at any time, which is the default.
    /// </summary>
    /// <remarks>
    /// Closing is the customer's word that the problem is solved. A host that wants it kept that way sets a
    /// window — staff may close a case only once support answered last and the customer has said nothing for
    /// that long. The team's own support readers and the author are not limited by it.
    /// </remarks>
    public TimeSpan? StaffCloseAfter { get; set; }

    /// <summary>The name the mailbox configured on <see cref="Email"/> is stored under on the cases that come in through it.</summary>
    public const string PrimaryMailboxName = "primary";

    private readonly List<SupportMailbox> _mailboxes = [];

    /// <summary>
    /// Reading and sending mail, and which recipients this instance answers for. Leave the hosts unset to
    /// keep email off.
    /// </summary>
    public MailOptions Email { get; } = new();

    /// <summary>Connects one more mailbox to support, read and answered from as <see cref="Email"/> is, under a name that stays stable in stored data.</summary>
    /// <exception cref="ArgumentException">The name is empty, is <see cref="PrimaryMailboxName"/>, or is already used.</exception>
    public SupportCaseOptions AddMailbox(string name, Action<MailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var key = name?.Trim();
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("A support mailbox needs a name: it is stored on every case that comes in through it.", nameof(name));
        if (string.Equals(key, PrimaryMailboxName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"'{PrimaryMailboxName}' is the name of the mailbox configured on {nameof(Email)}; give this one another.", nameof(name));
        if (_mailboxes.FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.OrdinalIgnoreCase)) is { } taken) throw new ArgumentException($"A support mailbox named '{taken.Name}' is already added; each name must be unique, whatever its case.", nameof(name));

        var mail = new MailOptions();
        configure(mail);
        _mailboxes.Add(new SupportMailbox(key, mail));

        return this;
    }

    internal IReadOnlyList<SupportMailbox> AdditionalMailboxes => _mailboxes;

    /// <summary>How an answer mail speaks for support, from every mailbox: the name earlier support entries are attributed to, the signature, and the time zone of quoted dates.</summary>
    public SupportAnswerOptions Answers { get; } = new();

    /// <summary>
    /// How the assistant behaves, when the host has registered an <c>IChatClient</c> for it to use.
    /// </summary>
    /// <remarks>
    /// <b>Configuring this does not turn an assistant on.</b> What does is registering an <c>IChatClient</c>;
    /// with none registered no responder exists, every case is answered by a person, and nothing here has any
    /// effect. That is deliberate — which model answers your customers is not a setting the toolkit should
    /// own.
    /// </remarks>
    public SupportAssistantOptions Assistant { get; } = new();
}
