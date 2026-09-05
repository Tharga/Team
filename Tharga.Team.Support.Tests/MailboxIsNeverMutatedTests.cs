using System.Text.RegularExpressions;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The mailbox is read and never written.
/// </summary>
/// <remarks>
/// <b>This cannot be asserted against a running server, and it is too important to leave to inspection.</b>
/// The whole shared-mailbox design rests on it: two sites may read one <c>support@</c> address, and mailbox
/// flags are shared state. Opening the folder read-write lets a fetch set <c>\Seen</c> as a
/// <i>side effect</i> — nobody writes a line saying "mark this handled" — and the message then looks handled
/// to the instance that actually wanted it. Moving a message is the same failure with the evidence removed.
/// <para>
/// <b>So this scans the source.</b> A guard keyed on a symbol rather than on behaviour is a weak test in
/// general; here the behaviour needs an IMAP server and a second deployment to observe, and the defect is
/// silent, permanent mail loss. The marker is what the fix would have to change.
/// </para>
/// <para>
/// The read position exists precisely because this holds — progress is the deployment's own, kept outside the
/// mailbox. See <c>SupportMailPollerTests</c>.
/// </para>
/// </remarks>
public class MailboxIsNeverMutatedTests
{
    /// <summary>How the folder must be opened, as it appears in the source.</summary>
    private const string ReadOnlyOpen = "FolderAccess.ReadOnly";

    /// <summary>
    /// Anything that writes to a mailbox. <c>MoveTo</c> and <c>CopyTo</c> relocate a message;
    /// <c>AddFlags</c> and <c>SetFlags</c> are how <c>\Seen</c> would be set deliberately;
    /// <c>Expunge</c> deletes.
    /// </summary>
    private static readonly string[] Mutations =
    [
        "FolderAccess.ReadWrite",
        "MoveToAsync",
        "CopyToAsync",
        "AddFlagsAsync",
        "SetFlagsAsync",
        "ExpungeAsync"
    ];

    private static readonly Regex TransportFile = new(@"\.cs$", RegexOptions.Compiled);

    private static (string Name, string Text)[] TransportSources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Support"))) dir = dir.Parent;

        Assert.NotNull(dir);

        var root = new DirectoryInfo(Path.Combine(dir.FullName, "Tharga.Team.Support", "Email"));

        Assert.True(root.Exists, $"The mail transport was not found at {root.FullName}.");

        return [.. root.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => TransportFile.IsMatch(f.Name))
            .Select(f => (f.Name, File.ReadAllText(f.FullName)))];
    }

    /// <summary>The self-check: an empty scan would satisfy everything below.</summary>
    [Fact]
    public void TheScanFindsTheTransport()
    {
        var sources = TransportSources();

        Assert.NotEmpty(sources);
        Assert.Contains(sources, x => x.Name == "SupportMailClient.cs");
    }

    [Fact]
    public void TheMailboxIsOpenedReadOnly()
    {
        var opens = TransportSources().Where(x => x.Text.Contains("OpenAsync")).ToArray();

        Assert.NotEmpty(opens);
        Assert.All(opens, x => Assert.Contains(ReadOnlyOpen, x.Text));
    }

    [Fact]
    public void NothingInTheTransportWritesToTheMailbox()
    {
        var offenders = new List<string>();

        foreach (var (name, text) in TransportSources())
        {
            foreach (var mutation in Mutations)
            {
                if (text.Contains(mutation, StringComparison.Ordinal)) offenders.Add($"{name}: {mutation}");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The detector's own check: it has to be able to see a mutation, or "no offenders" only means it never
    /// looks.
    /// </summary>
    [Fact]
    public void TheDetectorRecognisesAMutation()
    {
        const string written = "await folder.AddFlagsAsync(uid, MessageFlags.Seen, true);";

        Assert.Contains(Mutations, m => written.Contains(m, StringComparison.Ordinal));
    }
}
