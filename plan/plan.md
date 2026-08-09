# Plan: close #204 (TeamComponent + UsersView surface)

Branch `feature/text-team-and-users-surfaces`, off `master` at `7cca961`.

## Steps

- [x] **1. Package check.** `dotnet list package --outdated`: only `SixLabors.ImageSharp` 3.1.12 → 4.0.0,
      deliberately held (major, own pass, decided 2026-08-09). Nothing to apply.

- [ ] **2. `UsersListView` (50).** The largest file in this half and the one carrying the tenant noun in the
      grid a user reads first.

- [ ] **3. `TeamsListView` (30).**

- [ ] **4. The user dialogs (22).** `UserIconDialog` 8, `DirectoryOnlyUsersView` 6, `DeleteUserDialog` 6,
      `AssignOwnerDialog` 2.

- [ ] **5. `TeamComponent` (60).** Most of it is dialog titles, notifications and confirmation prompts in
      the C# block. Positional placeholders via `TextSet.Format`, never interpolation.

- [~] **6. The team dialogs (22 → 12 left).** Done (`e9bdd30`): `RoleEditor` 3, `ScopeOverrideEditor` 1,
      `SuspendedTeamNotice` 2, `TeamDialog` 1, `AssignOwnerDialog` 2 — all at zero and in `Migrated`.
      Remaining: `TeamIconDialog` 7, `InviteUserDialog` 4, `TeamInviteView` 3.
      Two things fell out of it:
      - **A sixth scan false positive.** `DisabledProperty="Inherited"` was counted as the label
        Inherited. A `…Property="…"` value names a member of the bound item, never text, and it is
        indistinguishable from display text once separated from its attribute — so it is excluded at
        extraction rather than by value.
      - **`AssignOwnerDialog` hand-rolled its Cancel button**, which `shared-instructions.md` forbids.
        Replaced with the shared `CancelButton`; ordering was already correct.
      `SuspendedTeamNotice` keeps its `Title`/`Message` parameters — an explicitly supplied one still
      wins, and the catalogue supplies the default it falls back to.

- [ ] **7. Move all 14 into `Migrated`;** assert each new catalogue against `ThargaTextKeys.All`.

- [ ] **8. Docs (`docs:` commit).** Update the coverage table: the four named surfaces complete, and the 135
      strings across 8 components that remain **outside** #204 — so closing the issue cannot be read as
      "the toolkit is fully localizable".

- [ ] **9. Close-out.** Re-check packages (excluding ImageSharp); bump `MAJOR_MINOR` in `build.yml` — this
      adds public API (new catalogues), and nothing in CI does it; close #204 with the zero counts; set Done
      in `Requests.md` + `Eplicta/requests.md` naming the version; archive `feature.md` to the Plan
      directory `done/`; `git rm -r plan`; final commit; push; PR.

## Method — settled while doing `AuditLogView`, reuse it

- One `*Text` catalogue per component in `Tharga.Team.Blazor/Framework/`, `All` array at the bottom.
- Key format `team.<component>.<name>`, whole strings, **never a substitutable noun** — Swedish suffixes the
  definite article, so *"medlem i ett team"* → *"teamet"* is unreachable by substitution.
- Resolve once in `OnInitializedAsync` into a `TextSet`; read synchronously in markup.
- **Resolve before any early return** that renders user-facing text — the `AuditLogView` not-configured
  alert was exactly that trap.
- A `static` helper that renders text takes an optional `TextSet` defaulting to null, so existing static
  tests keep compiling and fall back to English.
- Reuse one key where the same word does the same job twice (a filter label and its column header).

## Last session

2026-08-09 — branch created off `7cca961`; five small components migrated (`e9bdd30`); suite green at
1,906. **Remaining: 174 strings across 9 files** — `UsersListView` 50, `TeamsListView` 30,
`DirectoryOnlyUsersView` 6, `UserIconDialog` 8, `DeleteUserDialog` 6 (steps 2-4); `TeamComponent` 60
(step 5); `TeamIconDialog` 7, `InviteUserDialog` 4, `TeamInviteView` 3 (step 6). Then steps 7-9.
**Next: step 2, `UsersListView`** — 604 lines, the largest single file left.
