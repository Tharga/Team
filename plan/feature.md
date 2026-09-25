# Feature: invitation seams on a custom store fail loudly — or stop needing to

Two records, one mechanism:

- **Backlog** (`Toolkit/Team.md` → *Bugs*, filed 2026-09-24): "Two XML docs describe safeguards that do not
  exist". Verified 2026-09-25 to be a **security defect**, not only a wrong comment — see below.
- **[Tharga/Team#286](https://github.com/Tharga/Team/issues/286)**: short invite links silently require a store
  lookup a host can omit.

Branched from `origin/master` at `bc4346f` (PR #301 merged). `dotnet outdated` found Moq 4.20.72 → 4.21.0 in
`Tharga.Team.Blazor.Tests`; applied as `chore(deps): nuget update`, full suite 3,028 green.

## 1. Expired invitations are accepted on a custom store — tier 1

`SetInvitationResponseAsync` refuses an expired accept by asking `GetInvitationInternalAsync` for the
invitation. That member's default returns **null**, and `InvitationPolicy.HasExpired(null, …)` is false — no
invitation reads as "never expires". So a host deriving `TeamServiceBase` directly, with
`InvitationOptions.Lifetime` set, **accepts expired invitations and grants the membership**.

- The invite *screen* is not fooled: `TeamManagementService.GetInvitationAsync` reads the roster and shows
  "expired". The enforcement point is. Anything reaching `SetInvitationResponseAsync` without that screen —
  the REST or MCP surface, a host's own page — joins the team. That is rule 2 of the target architecture being
  broken exactly where it matters: the domain check is the one that fails open.
- The XML doc says an `InvitationExpiryWiringCheck` "fails the boot" in this case. **It does not exist** —
  the only reference in the repository is that sentence.
- `TeamServiceRepositoryBase` overrides it, so the built-in store is unaffected.

**Fix: make the default right rather than demand an override.** `TeamServiceBase` can already read any team's
roster (`GetMembersAsync`, used by the same screen), and `ITeamMember` carries `Invitation`. So:

- `GetInvitationInternalAsync`'s default finds the invitation on the roster. A store that exposes its members —
  which every store the rest of the base relies on does — gets expiry enforced with no code.
- **Accept fails closed**: with a lifetime configured and no invitation found for the code, refuse with the same
  "no outstanding invitation" error `ExtendInvitationAsync` already raises. Accepting a code nobody holds should
  never succeed, and "cannot tell whether it expired" must not read as "has not".
- `GetInvitedMemberNameAsync` has the same shape (default null, so the inviter's typed name is lost on accept)
  and gets the same roster-reading default. Same five lines; leaving it would be odd.
- Replace the orphaned doc (it sits above `GetTeamKeyByInviteKeyInternalAsync`'s summary; the member itself has
  none) with a correct one on the right member.

## 2. Short links on a custom store resolve to nothing — tier 2 (#286)

The toolkit generates only the short link form (`?tic=<code>`), which carries no team key. Resolving it needs
`GetTeamKeyByInviteKeyInternalAsync`, whose default returns null. A host deriving `TeamServiceBase` directly
mints links that every recipient sees as "You have no pending invitations" (#285), with nothing logged.

The base cannot answer this itself without scanning every team on every link open, so here the right fix is the
one the issue asks for: **report it at startup.** A rule in `TeamServiceCompleteness` — always reachable, since
short links are the only form generated — so `TeamServiceCompletenessCheck` logs it (fatal under
`ThrowOnIncompleteTeamService`). Change the member's remarks: "degrading is the correct behaviour" stopped being
true when the long form stopped being generated.

## 3. A replaced `ITeamRepository` — the same two gaps one layer down

`TeamServiceRepositoryBase` forwards both seams to `ITeamRepository<,>`, whose default interface members are
silent too: `GetByInviteKeyAsync` returns null, and `SetInvitationExpiryAsync` **no-ops while documented as
throwing** — extending an invitation reports success and changes nothing. The toolkit registers its own
`TeamRepository<,>`, which implements both, so this only reaches a host that replaced that registration.

- `SetInvitationExpiryAsync`'s default **throws** `NotSupportedException`, as its own doc already claims and as
  its access-request siblings in the same interface do.
- `GetByInviteKeyAsync` keeps its null default (it is the resolution path; a throw would 500 a link page), and a
  startup check in `Tharga.Team.MongoDB` reports a registered repository that implements neither member.

## Not in scope

- **#286 option 2, `InvitationOptions.LinkFormat`.** The issue itself says option 1 alone closes the failure, and
  that `Long` re-opens what #249 closed. Answer it on the issue; build it only if a host asks with a reason.
- #285 (four outcomes, one message) — the natural next feature, not this one.

## Acceptance criteria

- [ ] A `TeamServiceBase` derivative with a lifetime set refuses an expired accept without overriding anything.
- [ ] With a lifetime set, accepting a code with no matching invitation is refused.
- [ ] Declining an expired invitation still works.
- [ ] The inviter's typed name is carried over on accept without an override.
- [ ] The startup check reports an un-overridden `GetTeamKeyByInviteKeyInternalAsync`, and stays silent on the
      built-in store and on a host that overrides it.
- [ ] A replaced `ITeamRepository` that omits `GetByInviteKeyAsync` or `SetInvitationExpiryAsync` is reported
      at startup; `SetInvitationExpiryAsync` throws when called rather than no-opping.
- [ ] No XML doc references a check that does not exist; the orphaned summary is on the member it describes.
- [ ] Docs: the `TeamServiceBase` contract table (added in #301) and the implementation guide's invitation
      sections reflect the new defaults.
- [ ] Full suite green.

## Done condition

Criteria met; #286 commented (what shipped, why option 2 is not built) and closed; the backlog entry removed;
`plan/` removed in the close-out commit; PR open.

## Version

**No `MAJOR_MINOR` bump.** Nobody who works today has to change anything: the accept that now refuses was a
security defect, and the throwing repository default reaches only a host whose extensions already silently did
nothing.
