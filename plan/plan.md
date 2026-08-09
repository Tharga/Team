# Plan: finish the text catalogue (#204)

Branch `feature/text-teamcomponent-auditlogview`, off `master` at `8280fa5`.

## Steps

- [x] **1. Package updates, up front.** `dotnet-outdated` is not installed on this machine; used
      `dotnet list package --outdated` instead. Applied: `Microsoft.AspNetCore.DataProtection` 10.0.9 →
      10.0.10, `Microsoft.Identity.Web` 4.12.0 → 4.14.2, `NSubstitute` 6.0.0 → 6.1.0 in all five test
      projects. Build clean, 1,884 tests green. Commit `f0610b3`.
      **`SixLabors.ImageSharp` 3.1.12 → 4.0.0 deliberately held** (user's call) — a major, and the icon
      processor is the only consumer. Do not pick it up in the step-9 re-check.

- [ ] **2. `AuditLogViewText` catalogue + migrate `AuditLogView`** (43 → 0). The smaller of the two, so it
      settles the shape for C#-block strings — notifications and dialog titles — before the 1,329-line
      component. Move it into `Migrated` in the same commit.

- [ ] **3. `TeamComponentText` catalogue + migrate `TeamComponent`, markup half.** Attribute strings:
      `Text=`, `Title=`, `Placeholder=`, `title=`.

- [ ] **4. `TeamComponent`, C# half** — dialog titles, `NotificationService.Notify` messages and
      confirmation prompts. This is most of the 61 and most of what a user actually reads. Messages naming
      something use `TextSet.Format` with positional placeholders, never interpolation: an interpolated
      string cannot be translated at all. Move the component into `Migrated`.

- [ ] **5. Re-point the scan's self-check.** `TheScan_ActuallyFindsLiterals` currently proves the regex
      still works by asserting `AuditLogView` has literals, and `APendingComponent_DoesNotGrow` is driven
      from a `Pending` dictionary the test asserts is non-empty. **Both break by succeeding** — when the
      migration lands there is no pending component and no production file with literals left to prove
      anything against. Re-point the self-check at the inline fixture strings it already carries (it
      asserts all three categories and three false-positive shapes), and let `Pending` legitimately be
      empty. Without this, finishing the work fails the suite for the wrong reason.

- [ ] **6. Assert the consumer's entry point.** A test that every key in both new catalogues appears in
      `ThargaTextKeys.All` — that is the list FortDocs generates their translation table from, so a
      catalogue the reflection misses is invisible to them exactly as if it were still literal.

- [ ] **7. Correct the stale `<remarks>` on `TextCoverageTests`.** It says the scan covers *"**attribute**
      strings only"*, but `TheScan_ActuallyFindsLiterals` already asserts it finds inline prose
      (`<RadzenText>You are not a member.</RadzenText>`) and notification calls. The comment predates
      commit `ae0f371` ("an honest text scan") and now understates what the number means — which is the
      opposite of the problem the honest scan was written to fix.

- [ ] **8. Docs (`docs:` commit).** Check both surfaces per the workflow: the repo `README.md` and
      `docs/articles/`. The text provider is the consumer-facing half of #204, so this needs a section
      naming `IThargaTextProvider`, `o.Blazor.AddTextProvider<T>()`, `ThargaTextKeys.All` and the
      whole-strings-not-noun-tokens rule — including *why*, since the Swedish definite-article argument is
      what stops the next person adding a `Text["Team"]` map.

- [ ] **9. Close-out.** Re-run the package check (**excluding ImageSharp**); bump `MAJOR_MINOR` in
      `build.yml` if the release adds public API — nothing in CI does it; close #204 with the zero counts
      as evidence; set the entry Done in `Requests.md` with a `## Follow-up` line telling FortDocs which
      version to take; update `Eplicta/requests.md`; archive `plan/feature.md` to
      `$DOC_ROOT/Tharga/plans/Toolkit/Platform/done/`; `git rm -r plan`; final commit
      `feat: text-teamcomponent-auditlogview complete`; push; open the PR.

## Notes and decisions

- **The ratchet is the source of truth for remaining work**, not this file. `TextCoverageTests` fails if a
  count goes up *and* fails if it goes down without the record being updated, so the two numbers cannot
  drift from reality.
- **Do not compose strings from a shared noun token.** `ThargaTextKeys` documents why: Swedish suffixes the
  definite article, so *"medlem i ett team"* → *"teamet"* is unreachable by substitution and word order
  moves besides. FortDocs asked for this explicitly.
- **`plan/` is feature-branch-only** and is removed in the close-out commit (step 9).

## Last session

2026-08-09 — branch created off `master`, package updates applied and committed (`f0610b3`), scope and plan
written. Next: step 2, `AuditLogView`.
