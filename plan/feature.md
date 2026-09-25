# Feature: invite-link-wiring

Closes [Tharga/Team#286](https://github.com/Tharga/Team/issues/286) and
[Tharga/Team#285](https://github.com/Tharga/Team/issues/285).

## Goal

A short invitation link that cannot work says so at startup, and an invitation that cannot be accepted
tells the recipient why, rather than reporting that there is nothing waiting.

## Scope

### #286 — wiring check for the invite-key seam (option 1 only)

Short links are the only form `TeamComponent` mints, and resolving one needs the store to answer
"which team holds this invite key". Three places can silently answer null:

1. `TeamServiceBase.GetTeamKeyByInviteKeyInternalAsync` not overridden (host on `TeamServiceBase`).
2. `ITeamService.GetTeamKeyByInviteKeyAsync` default member not implemented (host implementing the
   interface directly, without `TeamServiceBase`).
3. `ITeamRepository.GetByInviteKeyAsync` default member not implemented (host with its own repository
   under `TeamServiceRepositoryBase`).

- 1 and 2 become a `TeamServiceGap` from `TeamServiceCompleteness.Find`, reported by the existing
  `TeamServiceCompletenessCheck` (logs an error; throws under `ThrowOnIncompleteTeamService`).
  Unconditional — not opt-in like expiry, because every invitation link is a short one.
- 3 becomes a startup check in `Tharga.Team.MongoDB`, registered beside the team repository, that
  inspects the resolved repository's interface map. Logs an error; never throws.
- Correct the XML docs that call the null default "degrading is the correct behaviour".

**Out of scope:** option 2 (`InvitationOptions.LinkFormat`). The issue itself says it is only worth doing
if a host needs invitations without implementing the lookup; nobody has asked, and `Long` re-opens what
#249 closed.

### #285 — `TeamInviteView` distinguishes its outcomes

| State | Shown | Join button |
|---|---|---|
| No code present | `NoInvitations` (only with `ShowEmptyMessage`, as today) | — |
| Code present, resolved to null | new `InvalidLink` | no |
| `AlreadyMember` | new `AlreadyMember` (`{0}` = team name) | no |
| `Status == Expired` | new `Expired` (`{0}` = team name) | no |
| Open | `Invitation`, as today | yes |

The three new messages show whenever a code was presented, independent of `ShowEmptyMessage` — a code
means the visitor came from a link, so saying nothing is the defect. The stored code is cleared in all
three, as it is today for the null case.

## Acceptance criteria

- A team service on `TeamServiceBase` without `GetTeamKeyByInviteKeyInternalAsync` is reported at startup,
  naming the member; the built-in Mongo service and its derivatives report nothing.
- A service implementing `ITeamService` directly without `GetTeamKeyByInviteKeyAsync` is reported.
- A custom `ITeamRepository` without `GetByInviteKeyAsync` is reported at startup; `TeamRepository` is not.
- `TeamInviteView` renders the distinct text for each state above, with no join button unless Open.
- Each new text key is in `TeamInviteViewText.All`.
- Full test suite green.

## Done condition

Both issues closed with a comment naming what shipped; backlog and README/docs updated.
