# Plan: finish the text catalogue (#204)

Branch `feature/text-teamcomponent-auditlogview`, off `master` at `8280fa5`.

## Scope correction, 2026-08-09 — read this before trusting any earlier number

The plan originally said **104 strings** (`TeamComponent` 61 + `AuditLogView` 43), taken from the ratchet in
`TextCoverageTests`. **The ratchet understates the work**, for two reasons found before any code was
written:

1. **It reads `.razor` only.** `AuditLogView` keeps 554 lines of C# in `AuditLogView.razor.cs` holding 9
   user-facing strings — "Export failed", "Query failed", "No data to export", the failure-detail tooltip.
   It is the only component with a code-behind, so the fix is small, but the reported 43 was really 52.
2. **It tracks five hand-picked files.** `UsersView` is recorded as **Migrated at zero** and is a 124-line
   wrapper — the tabs a user reads are `UsersListView` (51) and `TeamsListView` (30), untracked. A consumer
   would supply a full translation table and still find both tabs in English, which is exactly the
   complaint in #204.

The earlier "honest scan" commit (`ae0f371`) fixed **category** coverage — attributes vs inline vs code
strings. It did not fix **file** coverage, so the number still read as more complete than it was.

**Scope decided with the user 2026-08-09: #204 as the consumer experiences it** — the four named components
*and the sub-views and dialogs they render*, since that is what a user sees on those four surfaces.
**~238 strings across 16 files.** Out of scope, and left tracked-but-pending: `ApiKeyView` (47),
`SystemApiKeyView` (35), `ScopeView` (15), `UserProfileView` (13), `AccessSimulationDialog` (12),
`TenantRoleManager` (11), `AccessSimulationBar` (3), `ApiKeyRevealDialog` (2).

## Steps

- [x] **1. Package updates, up front.** `dotnet-outdated` is not installed on this machine; used
      `dotnet list package --outdated` instead. Applied: `Microsoft.AspNetCore.DataProtection` 10.0.9 →
      10.0.10, `Microsoft.Identity.Web` 4.12.0 → 4.14.2, `NSubstitute` 6.0.0 → 6.1.0 in all five test
      projects. Build clean, 1,884 tests green. Commit `73544d1`.
      **`SixLabors.ImageSharp` 3.1.12 → 4.0.0 deliberately held** (user's call) — a major, and the icon
      processor is the only consumer. Do not pick it up in the close-out re-check.

- [x] **2. Fix the scan's file coverage, and re-baseline honestly.** Done. `CountLiterals` now adds the
      sibling `.razor.cs`; new `EveryComponentWithText_IsTracked` walks every `.razor` in the library and
      fails if one carrying literals is in neither table; the self-check no longer proves itself against a
      file that is about to be migrated. **Re-baselined: 376 strings across 23 components**, against the
      104 across 2 the old record claimed. 29 tests green, and every recorded count matched the file
      exactly — no drift, so the numbers below are trustworthy.
      - Scan a component's sibling `.razor.cs` together with its `.razor`.
      - **Discover components rather than listing them.** Every `.razor` under `Tharga.Team.Blazor` is
        either migrated (zero, and stays zero) or pending with a recorded count. A file nobody added to a
        list is what produced this whole correction.
      - Re-baseline every count, including the out-of-scope components above, so the record shows the real
        remaining work for the library rather than for one feature.
      - Expect `UsersView` to *leave* `Migrated` in spirit — the wrapper stays at zero, but its tabs enter
        the record as pending.

- [x] **3. `AuditLogView` (52 → 0).** Done, commit `82c1777`. New `AuditLogViewText` with 57 keys; the
      component resolves them **before** the not-configured early return, or the one message a misconfigured
      host ever sees would stay English. `BuildFailureDetail` took an optional `TextSet` defaulting to null
      so its existing static tests keep compiling and fall back to the English defaults.
      **Three scan corrections fell out of doing it, all false positives rather than tuning:**
      - **Razor comment blocks are stripped.** They routinely quote the strings a component renders while
        explaining a past defect — this file's own header quotes *"Access denied."* — so they counted as
        work that did not exist. Same category as the XML docs already skipped.
      - **Plain `//` comments are skipped**, as `///` already was. Missed until now only because the earlier
        components kept their C# in the `.razor`, where such comments are rarer.
      - **A PascalCase identifier written as a call** (`AddThargaAuditLogging()`) is an identifier.
      **The CSV/JSON export headers stay literal by design** — an interchange format a downstream import
      parses by name; translating them would break every consumer's import on a language switch. The scan
      excludes comma-separated field lists as data.
      Net effect on the record: 376 → **319 across 22 components**, of which 52 was real migration and 5 was
      the scan no longer asking for strings nobody renders.

- [ ] **4. The `UsersView` surface (~101).** `UsersListView` 51, `TeamsListView` 30,
      `DirectoryOnlyUsersView` 6, `DeleteUserDialog` 6, `UserIconDialog` 8, `AssignOwnerDialog` 2.
      This is the half that was invisible, and the tenant noun in plural lives here.

- [ ] **5. `TeamComponent` (61).** Most of it is dialog titles, `NotificationService.Notify` messages and
      confirmation prompts in the C# block — most of what a user actually reads, and why the first estimate
      of 24 was low. Messages naming something use `TextSet.Format` with positional placeholders, never
      interpolation: an interpolated string cannot be translated at all.

- [ ] **6. The dialogs `TeamComponent` opens (22).** `TeamIconDialog` 7, `InviteUserDialog` 4,
      `TeamInviteView` 3, `RoleEditor` 3, `SuspendedTeamNotice` 2, `ScopeOverrideEditor` 2, `TeamDialog` 1.

- [ ] **7. Re-point the scan's self-check, and assert the consumer's entry point.**
      `TheScan_ActuallyFindsLiterals` proves the regex works by asserting `AuditLogView` *has* literals, and
      the pending theory is driven from a dictionary asserted non-empty. **Both break by succeeding.**
      Re-point the self-check at the inline fixture strings it already carries. Then assert every key in
      every new catalogue appears in `ThargaTextKeys.All` — that is the list FortDocs generates their
      table from, so a catalogue reflection misses is invisible to them exactly as if it were still literal.

- [ ] **8. Correct the stale `<remarks>` on `TextCoverageTests`** — it says the scan covers *"**attribute**
      strings only"*, which `ae0f371` already made untrue, and which understates what the number means.

- [ ] **9. Docs (`docs:` commit).** `README.md` and `docs/articles/` both. Name `IThargaTextProvider`,
      `o.Blazor.AddTextProvider<T>()`, `ThargaTextKeys.All`, and the whole-strings-not-noun-tokens rule
      including *why* — the Swedish definite-article argument is what stops the next person adding a
      `Text["Team"]` map.

- [ ] **10. Close-out.** Re-run the package check (**excluding ImageSharp**); bump `MAJOR_MINOR` in
      `build.yml` if the release adds public API — nothing in CI does it; close #204 citing the zero counts
      **and naming the components still pending**, so the record does not repeat the overstatement this
      plan had to correct; set the entry Done in `Requests.md` with a `## Follow-up` line naming the
      version; update `Eplicta/requests.md`; archive `plan/feature.md` to
      `$DOC_ROOT/Tharga/plans/Toolkit/Platform/done/`; `git rm -r plan`; final commit
      `feat: text-teamcomponent-auditlogview complete`; push; open the PR.

## Notes and decisions

- **Do not compose strings from a shared noun token.** `ThargaTextKeys` documents why: Swedish suffixes the
  definite article, so *"medlem i ett team"* → *"teamet"* is unreachable by substitution and word order
  moves besides. FortDocs asked for this explicitly.
- **The ratchet is the source of truth for remaining work**, not this file — once step 2 makes it tell the
  truth.
- **`plan/` is feature-branch-only** and is removed in the close-out commit.

## Last session

2026-08-09 — branch created, packages applied (`73544d1`), commits re-authored to `daniel.bohlin@live.se`,
scope corrected upward from 104 to ~238 after scanning the whole library. Scan fixed and re-baselined
(`ce0f99d`), `AuditLogView` migrated (`82c1777`). Full suite green at **1,906 tests**.

**Remaining for this feature: 213 strings** — `UsersListView` 50, `TeamsListView` 30,
`DirectoryOnlyUsersView` 6, `DeleteUserDialog` 6, `UserIconDialog` 8, `AssignOwnerDialog` 2 (step 4);
`TeamComponent` 60 (step 5); `TeamIconDialog` 7, `InviteUserDialog` 4, `TeamInviteView` 3, `RoleEditor` 3,
`SuspendedTeamNotice` 2, `ScopeOverrideEditor` 2, `TeamDialog` 1 (step 6). Then steps 7–10.
**Next: step 4, `UsersListView`** — the largest single file left and the one carrying the tenant noun.
