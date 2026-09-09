# Feature: audit log readability — classification, identity, grouping

**Issue:** [Tharga/Team#260](https://github.com/Tharga/Team/issues/260), filed by SimonEkberg (FortDocs / Eplicta)
**Type:** feat (additive public API) + one defect fix
**Target release:** 3.21 — `MAJOR_MINOR` moves `3.20` → `3.21` in `.github/workflows/build.yml`
**New published packages:** none

## Goal

A reader opening the audit log sees **what people did**. The per-call access traces the scope proxy
writes stay in storage and stay queryable, but they no longer drown the entries that carry object
identity.

Four changes, in the order they unblock each other. The issue's original asks 1 and 2 (an EventType
filter on `AuditQuery`, a matching top-bar control) are **already shipped** and are not in scope — the
reporter retracted them himself after inspecting 3.17.

## Scope

### 1. A consumer can classify its own entries

`AuditHelper.BuildEntry` hardcodes `EventType = AuditEventType.ServiceCall`, and
`IAuditEntryFactory.Create` has no event-type parameter — so every entry a consumer writes carries the
same value the scope proxy writes for its access traces, while the toolkit's own entries are classified
meaningfully (`AuthAuditEntries` writes `AuthSuccess` and `DataChange`). The Event filter that already
ships cannot make the one distinction a reader needs.

**Shape:** a new overload on `IAuditEntryFactory.Create` taking the event type, defaulting behaviour
unchanged for the existing signature. An overload rather than an added optional parameter: call sites
bake the full optional-argument list into the emitted call, so adding a parameter is binary-breaking for
anything compiled against 3.x. Precedent for the parameter itself is `AccessLevelProxy`, which already
takes an optional `eventType` and falls back to `ServiceCall`.

### 2. The reading grid shows Feature and Action

Consumer entries have `ScopeChecked = null` (only `ScopeProxy` sets it), so they render as an anonymous
"Web + a timestamp" row while a thin access trace looks fully labelled — the importance is inverted. The
top bar already offers Feature and Action *filters*, and the slow-calls grid on the Charts tab already
has both *columns*; the main reading grid has neither.

### 3. Soft filter defaults, and an exclusion form

`AuditLogView` takes three parameters, and the only filter lever is `PinnedFilter`, which hides and locks
the control by design. A host cannot offer a default the reader may change. Separately, every filter in
`AuditQuery` is include-only (`Scopes[]` maps to `builder.In`, there is no `Nin` anywhere), so
"everything except `audit:read`" is inexpressible — and an include list ages badly, since the host must
enumerate every category and remember to add new ones.

### 4. Correlation actually correlates (defect)

`AuditHelper` derives the correlation id from `HttpContext.TraceIdentifier` parsed as a `Guid`. That
identifier is `{ConnectionId}:{RequestNumber:X8}` and never parses as one, so on every HTTP request the
fallback fires and each entry gets a fresh `Guid.NewGuid()`. The reporter measured 417 entries with 417
distinct ids; the grouping the field is documented to provide has never worked outside declared
background work. Deriving it from `Activity.Current` makes both writers stamp the same value per request.

## Out of scope

- **`DateTimeView` stale relative timestamps** ("34 seconds ago" and "39 seconds ago" for two entries
  written in the same millisecond). The component comes from `Tharga.Blazor` 2.3.3, a different repo with
  its own rules. To be investigated there after this feature; not fixed on this branch.
- Storage-side suppression. The reporter is explicit that the access traces are wanted as evidence — the
  problem is the default reading view, not what is kept. `AuditOptions.ExcludedActions` already exists for
  hosts who do want to drop them.
- Folding the trace into its entry in the UI (one row per unit of work). Item 4 makes it *possible*;
  presenting it that way is a separate design question and is not attempted here.

## Acceptance criteria

1. A consumer can write an audit entry typed `DataChange`, and the shipped Event filter separates it from
   the scope proxy's `ServiceCall` traces. The existing `Create` signature still compiles **and still
   binds** for an assembly built against 3.20.
2. A consumer-written entry is identifiable in the reading grid without expanding the row — its Feature
   and Action are visible where today both the Scope and Method columns are blank.
3. A host can open `AuditLogView` on a chosen filter state that the reader can then change, and can
   express an exclusion ("everything except `audit:read`") without enumerating what to include.
4. Two entries written by the same HTTP request carry the same `CorrelationId`, and the correlation
   survives a declared `AuditActor` taking precedence for background work.
5. Full suite green, and the new behaviour is pinned by tests in each affected assembly — including a
   test that fails if `CorrelationId` regresses to a per-entry value on the HTTP path.
6. `MAJOR_MINOR` bumped to `3.21` in the same PR, per the project rule that nothing in CI does it.

## Done condition

All acceptance criteria met, documentation updated (`README.md` and `docs/articles/` both reviewed —
the audit surface appears in the implementation guide), issue #260 answered with what shipped and what
the reporter can now delete, and the reporter has confirmed.
