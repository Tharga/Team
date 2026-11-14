namespace Tharga.Team;

/// <summary>
/// The host's answer to "what address can support write to for this case": for a case raised in the app, whose author the package knows
/// only by identity and name.
/// </summary>
/// <remarks>
/// <b>The package does not know people's addresses; the host does.</b> A case raised on the site has no mail thread, so its answers are shown on
/// the site only. When the person who raised it cannot read answers there (an organisation without access to the support panel, say), staff
/// need to write to them. Register an implementation and, for a case it returns an address for, support is offered <i>Answer by mail</i>; the
/// first mail answer binds the case to the mail channel, and it then continues as an ordinary mail thread.
/// <para>
/// Without one registered nothing is offered and nothing changes. Return null for a case that has no address to write to. The address is what
/// the host holds for the author, never anything typed by the person answering, so staff cannot send a case's answers to an address of their own
/// choosing. It is called as a route is read, so keep it cheap; one that throws is logged and treated as no address.
/// </para>
/// </remarks>
public interface ISupportCaseContactResolver
{
    /// <summary>The single plain address to write to for this case, or null when there is none.</summary>
    Task<string> GetMailAddressAsync(SupportCase supportCase, CancellationToken cancellationToken = default);
}
