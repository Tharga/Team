# Plan: per-method audit mode (#262)

Branch `feature/per-method-audit-mode`, off `master` at `d480c3c` (the #263 merge).

## Steps

- [x] **1. NuGet update, whole solution.** *(2026-09-09)* `dotnet outdated` reports nothing — everything was
      applied at the start of the 3.21 branch a few hours earlier and nothing has published since. No
      `chore(deps)` commit this time; recorded here so the absence reads as "checked", not "skipped".

- [x] **2. `AuditMode` and the attribute parameters.** *(done 2026-09-09)*
      The enum in `Tharga.Team` (`Default` = 0, `None`, `Access`, `Change`), and an `Audit` property on both
      `RequireScopeAttribute` and `RequireAccessLevelAttribute`. Named properties on an attribute are
      additive — no constructor changes, so nothing existing needs recompiling.
      Tests: an unannotated attribute reports `Default`; an annotated one round-trips its value.

- [x] **3. `AuditOptions.DefaultAuditMode`, defaulting to `Access`.** *(done 2026-09-09)*
      The line that keeps every existing consumer unchanged. Test that the default is `Access` explicitly —
      it is the whole safety property, and a silent change to it is exactly the failure this design exists
      to avoid.

- [x] **4. Resolve the mode in `ScopeProxy`, and honour it.** *(done 2026-09-09)*
      Precedence: the method's `Audit` when it is not `Default`, else the host's `DefaultAuditMode`.
      `None` skips the write; `Access` writes `ServiceCall`; `Change` writes `DataChange`.
      **A denial is written regardless** — see `feature.md` §5.
      The proxy needs the host default, and `ScopeProxy.Create` is public: add an **overload** rather than an
      optional parameter, for the same binary-compatibility reason as 3.21's `IAuditEntryFactory` change —
      call sites bake the full optional-argument list into the emitted call.
      Tests: the four modes; the denial case; and the regression guard that an unannotated method with an
      unconfigured host still audits exactly as it does today.

- [x] **5. The same for `AccessLevelProxy`.** *(done 2026-09-09)*
      `AccessLevelProxy.LogAudit` already takes an `AuditEventType?`, so the mode maps onto a parameter that
      exists. Same test set, so the two proxies are pinned to behave identically rather than assumed to.

- [x] **6. Confirm the global filters still apply.** *(done 2026-09-09)*
      `CompositeAuditLogger.ShouldLog` is untouched by design; a test asserts an `Access` entry is still
      dropped by a host `EventFilter` that excludes `ServiceCalls`, and that a `Change` entry survives it —
      the compliance escape hatch, pinned rather than described.

- [x] **7. Documentation.** *(done 2026-09-09)*
      Implementation guide: a *Deciding per method what gets audited* section before the reading sections —
      the mode table, the host default, why silence is not the shipped one, and the two properties a host can
      rely on (a refusal is always recorded; the annotation writes and the configuration still filters).
      README: the writing half added to **Reading the audit log**, plus a note that the two denial-recording
      corrections change what an existing log contains.
      **`MAJOR_MINOR` stays 3.21** — re-checked, nuget.org is still at 3.20.1, so this rides in the same
      unpublished release rather than becoming 3.22.
      Original:
      `docs/articles/implementation-guide.md` — a section under the audit steps covering the mode, the host
      default, and why a denial is always recorded. `README.md` — extend the **Reading the audit log**
      section added in 3.21, since this is the writing half of the same story.
      Check `MAJOR_MINOR` again here: 3.21 if it still has not published, 3.22 if it has.
      Land as a `docs:` commit.

- [ ] **8. Push, and hand to the user to test from origin.** No PR yet.

- [ ] **9. Close-out (only on the user's word).** Re-run `dotnet outdated`; comment on #262 with what
      shipped **and with the premise correction**, then close it; archive `plan/feature.md` to the Plan
      directory `done/`; `git rm -r plan`; final commit `feat: per-method-audit-mode complete`; push; open
      the PR with the description written as release notes.

## Two defects found while building this, both fixed here

The feature's promise -- *a denial is always recorded* -- was false twice over, and neither failure was
mine. Both are security-relevant and both are fixed in this branch, because acceptance criterion 4 cannot
be met without them.

- **A refused team call was recorded as an allowed one.** `ScopeProxy` classified denials by
  substring-matching the exception text for `"Missing required scope"` -- wording only the **system** path
  produces. A team-scope refusal says *"requires the '...' scope on that team"*, so it never matched: the
  entry was written as `ServiceCall` with `ScopeResult = Allowed`. The log stated that a call which was
  refused had been permitted. Now classified by exception type, which is what `AccessLevelProxy` already
  did -- so the two proxies also stop disagreeing.
- **Every access-level denial was discarded.** `CompositeAuditLogger.ShouldLog` maps an event type onto an
  `AuditEventFilter` flag and had no case for `AccessLevelDenial`, so it fell to `AuditEventFilter.None`,
  matched nothing and was dropped on every host whatever the configuration said. `Denials` existed and was
  in `All`; it simply never matched this type.

**A rejected approach worth recording.** The first fix introduced a `ScopeDeniedException` deriving from
`UnauthorizedAccessException`. It broke 16 existing tests, because xUnit's `Assert.Throws<T>` requires an
**exact** type match -- and it would have broken consumer test suites the same way, for no gain. The
exception type already carried the signal; only the message match had to go.

**Consequence for a host reading its own log:** team-scope denials change from `ServiceCall`/`Allowed` to
`ScopeDenial`/`Denied`, and access-level denials start appearing at all. Both are corrections, and both are
visible in existing reporting.

## Decisions already taken

- **Host-level default with a per-method override**, rather than the filed default-off (user, this session).
  Default-off would strip the access trace from every existing consumer silently.
- **Both attributes**, not just `[RequireScope]` (user, this session).
- **Annotation writes, configuration filters** — no precedence exception (user, this session).
- **A denial is recorded even under `AuditMode.None`** — my call, stated in `feature.md` §5 for objection.
- **`AuditMode.Default` takes ordinal zero**, deliberately, so an unannotated method defers rather than
  decides — the inverse of the `default(AccessLevel)` = `Owner` trap already on this project's backlog.

## Last session

2026-09-09 (second) — steps 2–6 complete in one commit. Whole suite **2606, 0 failed** (2572 before). Two
pre-existing denial-recording defects found and fixed on the way; see above. **Step 7 (docs) is next.**

2026-09-09 — #262 verified against the code (the issue's "does not write anything today" premise is
inverted; the proxy audits everything). Scope agreed with the user, branch created, plan written. Step 1
done by inspection.
