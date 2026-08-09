# Plan: format keys and honest text measurement

## Steps

- [x] 1. NuGet check. Only `SixLabors.ImageSharp` 3.1.12 -> 4.0.0, held for its paid build-time licence.
- [x] 2. Read `TeamComponent` rather than grep it - which is what exposed the code-block strings and the
      interpolated ones the mechanism could not express.
- [x] 3. `TextSet.Format` with positional placeholders and no-throw fallbacks.
- [x] 4. `TextScan` covering attributes, inline prose and code-block strings.
- [x] 5. Validate the exclusions by running the scan over the migrated components and reading every hit.
      **Found a real miss in `TeamSelector`** and routed it.
- [x] 6. Reprice: `TeamComponent` 24 -> **61**, `AuditLogView` 47 -> **43**.
- [x] 7. Tests - 8 for `Format`, plus both-directions assertions on the scan.
- [x] 8. Build + full suite green, warnings at the **11** baseline.
- [x] 9. Correct the guide and Eplicta's entry, stating why the numbers moved.
- [ ] 10. Close-out: archive, `git rm -r plan`, final commit, push, PR. **Only when the user confirms.**

## Notes / decisions

- **Measurement before sweep.** Migrating 61 strings against a number that said 24 would have produced a
  component that looked done and was not.
- **The scan is heuristic and says so.** It needs to be stable and directional, not exact.
- Exception messages are excluded as developer-facing; XML doc comments as not rendered.

## Last session

Steps 1-9 complete. Nothing pushed, no PR.

Remaining for #204: `TeamComponent` (61) and `AuditLogView` (43), both now accurately counted.
