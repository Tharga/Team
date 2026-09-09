# Plan: Razor comment inside a tag (#268)

Branch `feature/razor-comment-in-attributes`, off `master` at `f732d80` — which now carries both #267 and
#269, merged while the previous branch was being finished.

## Steps

- [x] **1. NuGet update.** *(2026-09-09)* `dotnet outdated` clean.
- [x] **2. Fix** `TeamComponent.razor` — comment moved above the tag.
- [x] **3. Guard** — `RazorCommentPlacementTests`, a character scan tracking quotes and parentheses. Six
      tests: the sweep, plus a self-check that it reads files, catches the shipped shape, is not fooled by
      angle brackets inside an attribute value, and accepts both correct placements.
      **Verified by reintroducing the defect in the real file** — it failed and named
      `Features/Team/TeamComponent.razor:108`.
- [x] **4. Recorded follow-up** — assert the invitation throttle is applied, now that #267's resolution
      harness is on master.
- [x] **5. Full suite.** 2647 → **2654, 0 failed**.
- [ ] **6. Push, hand over for testing.** No PR yet.
- [ ] **7. Close-out (on the user's word).** Re-run `dotnet outdated`; answer and close #268; archive
      `plan/feature.md`; `git rm -r plan`; final commit `fix: razor-comment-in-attributes complete`; push;
      open the PR.

## Decisions taken

- **No documentation change.** A defect with no consumer-visible surface — the component renders as it was
  always meant to. Reviewed both surfaces before skipping, per the workflow.
- **No `MAJOR_MINOR` bump.** A fix, and 3.21 is still unpublished.

## Last session

2026-09-09 — fixed, guarded, and the guard verified against the real defect. Suite 2654. Step 6 next.
