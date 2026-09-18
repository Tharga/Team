# Feature: invite-accept-selects-team

GitHub issue: [Tharga/Team#287](https://github.com/Tharga/Team/issues/287)

## Goal

Accepting an invitation selects the team that was joined, and leaves the invitee at the root of the
site instead of on the invitation page.

## Background — what the investigation found

The starting assumption was that nothing selects the team. That is not quite it. `TeamServiceBase`
already raises `SelectTeamEvent` with the joined team from `SetInvitationResponseAsync` (accept branch,
`TeamServiceBase.cs:648`), and `TeamStateService` bridges that event to `SetSelectedTeamAsync`
(`TeamStateService.cs:61`). The decorators forward event subscriptions correctly, and every service in
the chain is scoped, so the event does reach the state service.

What is wrong is the shape of the bridge against what the view then does:

- The bridge handler is `async (_, e) => ...` assigned to `EventHandler<T>` — an `async void`. The
  raiser cannot await it, so it is fire-and-forget.
- The handler suspends immediately on `await _teamService.SetMemberLastSeenAsync(...)`, a database call.
- Control returns to `JoinTeam`, which calls
  `NavigationManager.NavigateTo(currentUri minus query, forceLoad: true)` — a full page load that tears
  down the circuit while the handler is still mid-flight writing local storage and the team cookie.
- If the handler does get far enough, it calls `_navigationManager.Refresh(true)` of its own, so two
  navigations race, and both land back on the invitation page.

So the selection is written unreliably at best, and the destination is wrong either way. A user with no
team at all still ends up on the new team, but by `TeamSelectionResolver`'s `ownTeams.FirstOrDefault()`
fallback rather than by intent — which is why this looks fine in a fresh-account demo and fails for
every established user.

## Scope

- `TeamInviteView` selects the joined team explicitly and awaited, rather than depending on a
  fire-and-forget event it then races.
- `TeamInviteView` navigates once, to the site root by default.
- A host can override that destination, following the `ProfilePath` / `TeamPath` / `InvitePath` pattern
  already on `ThargaBlazorOptions`.
- `Decline` navigates to the same destination and must keep *not* selecting the team.

## Out of scope

- The `TeamInviteView` wording problems in #285 — a separate issue on the same component.
- Changing what `CreateTeamAsync` does with `SelectTeamEvent`.
- Adding `OnAccepted` / `OnDeclined` callbacks. Worth having, but the default has to be complete first,
  and nothing has asked for the callbacks yet.

## Acceptance criteria

1. Accepting an invitation as a user who already belongs to another team leaves the joined team
   selected after the reload.
2. Accepting an invitation navigates to the site root, not back to the invitation page.
3. A host that sets the new path option lands there instead.
4. Declining navigates to the same destination and changes no selection.
5. Exactly one navigation happens on each path.
6. Tests cover 1-5 and fail against the current code where they describe changed behaviour.

## Done condition

All acceptance criteria met, full test suite green, docs updated, and #287 closed with what shipped.
