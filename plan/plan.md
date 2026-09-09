# Plan: two registration holes in AddThargaTeamBlazor (#261/#265, #266)

Branch `feature/blazor-registration-holes`, off `master` at `2975a49` (the #264 merge).

**Written alongside the work rather than before it** — the two defects were already diagnosed against the
code before the branch existed, and the fixes are one line each. The design content is in `feature.md`.

## Steps

- [x] **1. NuGet update.** *(2026-09-09)* `dotnet outdated` clean — `Tharga.MongoDB` 2.16.0 came in with
      #264, which this branches from. No `chore(deps)` commit; recorded so the absence reads as "checked".

- [x] **2. Fix #261/#265** — pass the seventh argument, `IOptions<InvitationOptions>`, to the
      `Activator.CreateInstance` call. The comment above it now says why the list is positional and what
      that costs, so the next person adding a constructor parameter meets the trap before falling into it.

- [x] **3. Fix #266** — `TryAddScoped<IIconProcessor, NoOpIconProcessor>` in `AddThargaTeamBlazor`.
      TryAdd, so `AddThargaImageProcessing`'s real implementation wins in either registration order.

- [x] **4. `TeamServiceResolutionTests`** — resolves every facet and the concrete management service from a
      real container. **Verified by reintroducing the defect: 6 of 7 fail.**

- [x] **5. `ComponentDependencyResolutionTests`** — every `[Inject]`ed Tharga-owned type on every component
      in the package resolves. **Verified by reintroducing the defect: fails naming `IIconProcessor`.**
      Carries two explicit exclusion lists (optional-package services, host-supplied services) so adding one
      is a visible decision rather than a silent widening.

- [x] **6. Full suite.** 2606 → **2628, 0 failed**. Build warnings 6.

- [ ] **7. Push, and hand to the user to test from origin.** No PR yet.

- [ ] **8. Close-out (only on the user's word).** Re-run `dotnet outdated`; answer and close #261 and #266,
      and close **#265 as a duplicate of #261**; archive `plan/feature.md` to the Plan directory `done/`;
      `git rm -r plan`; final commit `fix: blazor-registration-holes complete`; push; open the PR.

## Decisions taken

- **No documentation change.** The docs already describe the processor as optional with a no-op default —
  they were right and the registration was wrong. Stated rather than assumed, per the workflow's
  requirement to review both surfaces before skipping.
- **No `MAJOR_MINOR` bump.** Fixes with no new public API, and 3.21 is still unpublished (nuget.org is at
  3.20.1), so these ride with it.
- **The reflective construction stays.** It exists so the container cannot silently select a lesser
  constructor; the fix is the guard, not switching to `ActivatorUtilities`, which would reintroduce exactly
  that hazard.

## Last session

2026-09-09 — both defects fixed, both guards written and each verified against its own defect. Suite green
at 2628. **Step 7 (push) is next.**
