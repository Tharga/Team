# Plan: audit log readability (#260)

Branch `feature/audit-readability`, off `master` at `3b4807e`.

## Steps

- [x] **1. NuGet update, whole solution.** *(done 2026-09-09, commit `052e4ea`)* All ten updates applied,
      no majors, no breakage. Build clean at 16 warnings (CI threshold 35), full suite **2543 tests, 0
      failed** across all seven projects. **Note for the next run:** local SDK is 10.0.301, the version
      `shared-instructions.md` records as reporting "Zero tests ran" for every Toolkit repo — the suite was
      run through the test executables directly, which is the documented workaround. Mandatory first work on a feature branch. `dotnet outdated`
      currently reports patch-level moves only — `10.0.11 → 10.0.12` across the Microsoft.Extensions /
      AspNetCore references in seven projects, `SkiaSharp` 4.151.2 → 4.152.0, `bunit` 2.9.0 → 2.10.3. No
      majors. Apply everything, verify build **and the full test suite**, land as its own commit
      `chore(deps): nuget update`.

- [x] **2. Event type on the audit entry factory.** *(done 2026-09-09)*
      New overload on `IAuditEntryFactory.Create` taking `AuditEventType` **as the first parameter** — the
      leading position means it can never be ambiguous with the existing signature under overload
      resolution, whatever combination of optional arguments a caller supplies. `AuditHelper.BuildEntry`
      gained an optional `eventType` defaulting to `ServiceCall` and stops hardcoding it; that helper is
      `internal`, so the added parameter carries no compatibility cost and the six existing call sites are
      untouched. The original `Create` signature and behaviour are byte-for-byte unchanged.
      Its XML docs now say it records `ServiceCall` and point at the overload — the discoverability half of
      why #260 was filed against a filter that already existed.
      Tests (4, in `AuditEntryFactoryTests`): the overload classifies; the original still defaults to
      `ServiceCall`; classification changes nothing else on the entry; and a consumer entry never sets
      `ScopeChecked`, so classifying one can never read as a claim that a check happened.
      Service suite 891 → **895, 0 failed**.

- [ ] **3. One coalesced Operation column in the reading grid.** *(shape decided 2026-09-09)*
      **Not two new columns.** `ScopeProxy.cs:73-74` sets `Feature`/`Action` *and* `ScopeChecked`, and the
      scope is feature-colon-action — so on the row type that is ~85% of the log, two columns would print
      the same fact three times (`case:manage | case | manage`), and on an `AccessLevelProxy` row they
      would print a C# type name plus a duplicate of Method. Only consumer entries carry anything new.
      - Keep one column in the current Scope position: render `ScopeChecked` when present, `Feature:Action`
        when it is null. Always populated, no width cost.
      - **Retitle it** — a consumer's `case:CaseClosed` under a header reading "Scope" claims an
        authorization check that never happened, and an audit log must not conflate what was authorized
        with what was done. New text key `ColumnOperation`; leave `ColumnScope`'s value alone so a host's
        existing override is not silently repurposed.
      - **Distinguish the two forms on `ScopeChecked != null`, not on EventType.** EventType only
        discriminates for consumers who adopt step 2's overload, and never for rows already stored;
        `ScopeChecked` being null works retroactively.
      - Text goes through `IThargaTextProvider` (`AuditLogViewText`), never a literal, and the coverage
        ratchet must stay green.

- [ ] **4. Exclusion filters on the query layer.**
      `AuditQuery` gains exclusion forms (`ExcludedScopes`, `ExcludedEventTypes`) and `MongoDbAuditLogger`
      maps them to `Nin`. Query-layer only, no UI yet. Tests in `Tharga.Team.Service.Tests` covering
      exclusion alone and exclusion combined with an include on the same dimension.

- [ ] **5. Soft initial filter values on `AuditLogView`.** *(shape decided 2026-09-09)*
      - **A separate type** (`AuditInitialFilter`), not a reuse of `AuditPinnedFilter`. The dimension sets
        differ for a reason: defaults need `EventTypes[]` and the exclusion form, while pins need
        `CallerKeyId`, which as a *soft* default would let a reader un-scope a dialog that exists to show
        one API key.
      - **The pin wins, and a default naming a pinned dimension is ignored rather than merged.** A
        correctness property, not a preference: a default is by definition changeable by the reader, so a
        default that could override a pin would let the reader edit their way out of the pin's scope —
        and `PinnedFilter` is what scopes the per-key and per-member audit dialogs.
      - **Defaults must not reach `AuditFilterVisibility.ShouldShow`'s `isPinned`, and must not narrow the
        option-source query.** `_features`/`_actions`/`_sources` come from `optionQuery =
        ApplyPinnedFilter(...)`, pinned only. Apply defaults there and the excluded category vanishes from
        the dropdown, so the reader can never switch it back on — a default behaving as a pin, which is
        the one outcome this parameter exists to avoid. Pin this with a test.
      - **A "Show access traces" toggle in the top bar.** The bar has Team/Source/Feature/Action/Event/
        Result and no scope control, so a default exclusion of `audit:read` would otherwise be invisible
        and unreachable. Off applies the host's configured exclusion; on clears it. One discoverable
        control matching the question the reader actually has.

- [ ] **6. Correlation id from `Activity.Current`.**
      Replace the `Guid.TryParse(TraceIdentifier)` fallback in `AuditHelper`, which can never succeed on
      the HTTP path. A declared `AuditActor.CorrelationId` still wins — background work keeps its
      grouping. Tests: two entries from one request share an id; a declared actor's id is preferred; a
      call with neither still gets a stable non-empty value.
      **Three writers, not one — found 2026-09-09 while settling step 3.** `ScopeProxy.cs:81` and
      `AccessLevelProxy.cs:69` do **not** go through `AuditHelper`; each builds its entry inline with a
      literal `CorrelationId = Guid.NewGuid()`. Fixing `AuditHelper` alone would leave the consumer entry
      and the proxy trace of the same request still disagreeing, which is exactly the pair the acceptance
      criterion names. All three sites move to one shared resolver.

- [ ] **7. Version line and documentation.**
      `MAJOR_MINOR` `3.20` → `3.21` in `.github/workflows/build.yml`. Review **both** doc surfaces —
      `README.md` and `docs/articles/` (the implementation guide covers the audit surface) — updating
      existing sections and deciding whether the new classification/grouping behaviour warrants new
      content. Land as its own `docs:` commit. While in `AuditQuery`, add the missing XML docs on
      `EventType`/`EventTypes`/`Features`/`Actions`/`Scopes`: their absence is the documented reason the
      reporter filed for a filter that already existed.

- [ ] **8. Push, and hand to the user to test from origin.** No PR yet.

- [ ] **9. Close-out (only on the user's word).** Re-run `dotnet outdated` and apply; update
      `Requests.md` and the backlog; comment on #260 with what shipped and what FortDocs can delete
      (their `methodName`-as-action workaround); archive `plan/feature.md` to the Plan directory `done/`;
      `git rm -r plan`; final commit `feat: audit-readability complete`; push; open the PR.

## Decisions already taken

- **Scope is all four slices, including correlation** (user, this session).
- **Overload rather than an added optional parameter** on `Create` — adding a parameter is binary-breaking
  for callers compiled against 3.20 (user, this session).
- **`MAJOR_MINOR` bumps to 3.21** — added public API.
- **`DateTimeView` is out of scope**, to be investigated in the `Tharga.Blazor` repo after this feature
  (user, this session).
- **Issue asks 1 and 2 are already shipped** and are not work — verified against `master`, and the
  reporter retracted them himself.

## Last session

2026-09-09 — branch created, scope agreed with the user, `feature.md` and `plan.md` written. Nothing
implemented yet; step 1 is next.
