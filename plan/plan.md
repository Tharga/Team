# Plan: throttle failed invitation resolves (#256)

Branch `feature/invitation-throttle`, off `master` at `2975a49`.

**Branched from master while [PR #267](https://github.com/Tharga/Team/pull/267) is still open.** Both touch
`ThargaBlazorRegistration.cs`, in different regions — the facet loop here, the icon block and the
`Activator` call there — so a conflict should be trivial. Branching from master is what the strategy says;
the overlap is noted rather than worked around.

## Steps

- [x] **1. NuGet update.** *(2026-09-09)* `dotnet outdated` clean. No commit; recorded so the absence reads
      as "checked".

- [x] **2. Throttle settings on `InvitationOptions`** — `ThrottleFailureThreshold` (5),
      `ThrottleWindow` (5 min), `MaxThrottleDelay` (2 s). Generous rather than off, with the reasoning on
      the members.

- [x] **3. `InvitationThrottle`** — per-source failure counting in a sliding window, returning the delay and
      whether *this* failure crossed the threshold. Takes `TimeProvider`, so the window is testable without
      the suite waiting for it.

- [x] **4. `ThrottledTeamInvitationService`** — the decorator. Counts only failures, delays past the
      threshold, audits the first crossing as `RateLimit`, and still returns null so the throttle does not
      become a second oracle.

- [x] **5. `AddInvitationThrottle()`** in `Tharga.Team.Service`, called from `ThargaBlazorRegistration`
      after the facet loop. Registration lives in the package that owns the types, which are internal.
      **Decorates by replacing the descriptor**, not by ordering: the facets use `TryAdd`, so a decorator
      registered first would win and the inner service would never be built, and one registered last
      without removing the original would be shadowed. Neither mistake errors; both produce an unthrottled
      service that looks wired.

- [x] **6. Tests** — 19 across three files. `InvitationThrottleTests` covers the curve, the cap, per-source
      separation, window expiry and both off switches. `ThrottledTeamInvitationServiceTests` covers the
      behaviour, the single audit entry per run, and that the entry carries no code.
      `InvitationThrottleRegistrationTests` covers that the decoration is really applied — including
      surviving a later `TryAdd` of the same interface.
      Suite 2606 → **2625, 0 failed**.

- [x] **7. Documentation** — a section in the implementation guide under invitations, and a paragraph in
      the README. Both state the three limits plainly (per process, no address in a circuit, not a
      substitute for entropy) rather than leaving them to be discovered.

- [ ] **8. Push, and hand to the user to test from origin.** No PR yet.

- [ ] **9. Close-out (only on the user's word).** Re-run `dotnet outdated`; answer and close #256 with the
      two constraints that reshaped it; archive `plan/feature.md`; `git rm -r plan`; final commit
      `feat: invitation-throttle complete`; push; open the PR.

## Found while building

- **The decorator originally required `IOptions<InvitationOptions>`** and threw where a host had never
  configured it — the same defect class as #261 and #266, which this release already fixed twice. It now
  falls back to the documented defaults. Caught by the registration tests, not by review.

## Follow-up worth doing after #267 merges

That PR adds a container-resolution harness to `Tharga.Team.Blazor.Tests`. **Extend it to assert the
resolved `ITeamInvitationService` is the throttled one** — this branch could not, because the harness does
not exist on master yet and duplicating it here would conflict. The registration extension itself is
covered by `InvitationThrottleRegistrationTests`; what is uncovered is the one-line call site in
`ThargaBlazorRegistration`.

## Last session

2026-09-09 — built and green at 2625. **Step 8 (push) is next.**
