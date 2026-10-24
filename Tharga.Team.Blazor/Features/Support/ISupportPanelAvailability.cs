namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// Who may be asked in the support panel for a team, for a host that switches support or the assistant per team.
/// </summary>
/// <remarks>
/// <b>Optional.</b> Without one the panel offers a person always, and the assistant whenever the team is ready for
/// one. Register one to switch either off per team, for example behind a feature a team has or has not bought.
/// <para>
/// It can only narrow: the assistant is never offered to a team that is not ready for it, whatever this says.
/// </para>
/// </remarks>
public interface ISupportPanelAvailability
{
    /// <summary>
    /// Who may be asked in this team: <see cref="SupportAssistance.Assistant"/> for the assistant,
    /// <see cref="SupportAssistance.None"/> for a person.
    /// </summary>
    Task<SupportAssistance[]> GetOfferedAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Raised when what is offered may have changed, so an open panel asks again.</summary>
    event EventHandler Changed;
}
