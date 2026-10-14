# Feature: Support staff work every team's cases, and each case says who in its team may read it

## Goal
A host's own support staff — holders of `support:all:read` / `support:all:manage`, members of no customer team —
get a working queue across every team and a count of what is waiting for them, and a case's author decides
whether the rest of their team may read it.

## Background
`GetCasesAcrossTeamsAsync` and the cross-team scopes exist, but nothing renders them and nothing counts them: a
host has to build its own queue view and cannot show staff how much is waiting. Separately, `support:read` on a
team reaches every case in that team, while a support conversation is often personal — a host can only turn team
reading off entirely (`TeamScopeAccessLevel = null`), not per conversation.

## Scope
1. **Counts for staff.**
   - `GetAwaitingSupportCountAcrossTeamsAsync()` — open cases awaiting support in every team; `support:all:read`
     or `support:all:manage`.
   - `GetAwaitingUnassignedCountAsync()` — unassigned cases awaiting support; `support:unassigned:read` or
     `support:unassigned:manage`.
   - Authorization and auditing decorators; Mongo store queries.
2. **Cross-team queue view** in Tharga.Team.Blazor: open cases across teams with the team named, the transcript
   read and answered in place, the same text provider as the other support views. Depends only on
   `ISupportCaseService` and contracts (no server-only dependencies).
3. **Per-case team visibility.**
   - `SupportCaseVisibility { Team, Private }`, stored by name, on `SupportCase`.
   - Host option `SupportCaseOptions.Visibility`: `TeamWide` (default — today's behaviour, every case readable by
     team `support:read`) or `PerCase` (a new case starts `Private`; the author chooses).
   - `SetCaseVisibilityAsync(teamKey, caseId, visibility)` — the author only, while the case is open; audited.
   - Under `PerCase`, team `support:read` / `support:manage` reach a case only when it is `Team`; the author and
     cross-team staff reach it either way. Enforced in the authorization decorator; team listings and the team
     awaiting count include only what the caller may read.
   - The case view shows the setting to its author while open.
4. **Docs**: `docs/articles/support-cases.md` and the Support README.

## Acceptance criteria
- A caller with only `support:all:read` gets the cross-team count and listing; a caller without it is refused, pinned by decorator tests.
- The unassigned count needs `support:unassigned:*` and nothing else confers it.
- Under `TeamWide`, behaviour is unchanged (existing tests untouched and green).
- Under `PerCase`, a team `support:read` holder cannot read, list or count a `Private` case, can once the author sets `Team`, and cannot after the author sets it back; nobody but the author can change it; a closed case's visibility cannot change.
- A case stored before the field existed reads as `Team` under `TeamWide` and is treated per the option default under `PerCase`.
- The cross-team view renders cases from several teams with the team shown, and answers a case in place.
- Full test suite passes.

## Done condition
Merged to master via PR, docs and README updated, released as a package.
