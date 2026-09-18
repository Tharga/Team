using Tharga.Team;

namespace Tharga.Team.Blazor.Features.Team;

public interface ITeamStateService
{
    event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent;

    /// <summary>
    /// Raised when the selected team changes — a different team, or the same team under a new name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised only on an actual change. A resolution that lands on the team already selected is silent, and
    /// so is one that finds no team when none was selected.
    /// </para>
    /// <para>
    /// A handler that needs the current selection should read
    /// <see cref="SelectedTeamChangedEventArgs.SelectedTeam"/>, which carries the team the handler is being
    /// told about. Calling <see cref="GetSelectedTeamAsync"/> from a handler resolves the selection again
    /// and is not what the handler wants.
    /// </para>
    /// </remarks>
    event EventHandler<SelectedTeamChangedEventArgs> SelectedTeamChangedEvent;

    /// <summary>
    /// The caller's selected team, resolving one when the current selection is missing or no longer valid.
    /// </summary>
    /// <returns>The selected team, or <c>null</c> when the caller is unauthenticated or has no team.</returns>
    /// <remarks>
    /// <para>
    /// <b>This resolves as well as reads, so it is not free to call in a loop.</b> It reads the remembered
    /// team from browser local storage over JS interop, may create the caller's first team when
    /// <c>AutoCreateFirstTeam</c> is set, may force a page refresh so the team's claims are applied, and
    /// raises <see cref="SelectedTeamChangedEvent"/> when the selection changes.
    /// </para>
    /// <para>
    /// <b>Do not call it from a <see cref="SelectedTeamChangedEvent"/> handler.</b> Read
    /// <see cref="SelectedTeamChangedEventArgs.SelectedTeam"/> instead. To read the selection already
    /// resolved on this circuit without a browser round trip, use <see cref="TryGetSelectedTeam"/>.
    /// </para>
    /// </remarks>
    Task<ITeam> GetSelectedTeamAsync();

    /// <summary>
    /// The selection already resolved on this circuit, read without interop, without resolving and without
    /// raising <see cref="SelectedTeamChangedEvent"/>.
    /// </summary>
    /// <param name="team">The selected team, or <c>null</c> when this returns <c>false</c>.</param>
    /// <returns>
    /// <c>true</c> when a team has already been resolved and is known, otherwise <c>false</c>.
    /// </returns>
    /// <remarks>
    /// For a caller that only wants the current value — notably one reacting to
    /// <see cref="SelectedTeamChangedEvent"/>, where <see cref="GetSelectedTeamAsync"/> would resolve the
    /// selection all over again.
    /// <para>
    /// Treat <c>false</c> as "nothing known cheaply, ask <see cref="GetSelectedTeamAsync"/>" rather than as
    /// "no team is selected". The default implementation reports nothing known, so an implementation that
    /// keeps no cache of its own needs no change and cannot report a wrong answer.
    /// </para>
    /// </remarks>
    bool TryGetSelectedTeam(out ITeam team)
    {
        team = null;
        return false;
    }

    Task SetSelectedTeamAsync(ITeam selectedTeam);

    /// <summary>
    /// Records the selection, optionally without the page reload that normally follows it.
    /// </summary>
    /// <param name="selectedTeam">The team to select.</param>
    /// <param name="reload">
    /// <c>false</c> when the caller is about to navigate itself, so the selection does not race a
    /// navigation of its own.
    /// </param>
    /// <remarks>
    /// <b>The reload is not optional in general</b> — it is what applies the newly selected team's claims,
    /// so a caller passing <c>false</c> takes on the duty of navigating. It exists for the one caller that
    /// was already navigating: answering an invitation ends on the site's landing page rather than on the
    /// invitation page, and two navigations for one click is a race rather than a reload.
    /// <para>
    /// <b>A default interface method</b>, so a host with its own <see cref="ITeamStateService"/> keeps
    /// compiling. The default ignores <paramref name="reload"/> and reloads, which is what such an
    /// implementation did before this existed — the safe direction, since a missing reload leaves stale
    /// claims while a surplus one only costs a round trip.
    /// </para>
    /// </remarks>
    Task SetSelectedTeamAsync(ITeam selectedTeam, bool reload) => SetSelectedTeamAsync(selectedTeam);
}
