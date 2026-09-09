# Feature: per-method audit mode on the enforcement attributes

**Issue:** [Tharga/Team#262](https://github.com/Tharga/Team/issues/262)
**Type:** feat (additive public API)
**Target release:** 3.21 — see *Version line* below; **not** a bump, because 3.21 has not published yet
**New published packages:** none

## Goal

The declaration site decides whether a call is audited. A read that nobody needs recorded produces no
entry; a read the law requires recorded produces one; and neither is a global switch, because what
separates them is intent and only the method knows it.

## The premise correction this feature rests on

The issue says `ScopeProxy` *"simply does not write anything today"*. **It writes for every intercepted
call** — `ScopeProxy.cs:68` logs unconditionally, and `ScopeServiceCollectionExtensions.cs:107` hands it the
`CompositeAuditLogger` from DI, so it is on wherever audit logging is registered. #260 measured the
consequence from the same deployment: 274 of 319 entries were these.

So this feature **suppresses by declaration**; it does not add writing. Everything else in the issue —
`ExcludedActions` being the wrong polarity, `EventFilter` unable to separate reads from writes because
every proxy entry is `ServiceCall`, and the proxy being the right choke point — is accurate and verified.

## Scope

### 1. `AuditMode`, in `Tharga.Team`

| Value | Meaning |
|---|---|
| `Default` (0) | Say nothing, get the host's default. |
| `None` | Not audited. |
| `Access` | Audited as an access trace — `AuditEventType.ServiceCall`. |
| `Change` | Audited as a change — `AuditEventType.DataChange`. |

**The zero value defers rather than decides**, which is the opposite of the `default(AccessLevel)` trap this
codebase already carries: there, the unset value silently means maximum privilege. Here an unannotated
method must mean *"the host has not been asked"*, so `Default` takes ordinal zero deliberately.

It lives in `Tharga.Team`, not `Tharga.Team.Service`, because `RequireScopeAttribute` does and the
dependency runs that way.

### 2. The parameter, on both attributes

```csharp
[RequireScope(CaseScopes.Read)]                              // host default
[RequireScope(CaseScopes.Read, Audit = AuditMode.Access)]    // recorded, because the law wants this one
[RequireScope(CaseScopes.Manage, Audit = AuditMode.Change)]  // recorded as a data change
```

`RequireAccessLevelAttribute` gets the same parameter. `AccessLevelProxy.cs:69` audits unconditionally too,
so giving it to one attribute and not the other leaves two interception points with different audit rules —
the shape this codebase already has a written history of being bitten by.

### 3. The host default, on `AuditOptions`

`AuditOptions.DefaultAuditMode`, defaulting to `AuditMode.Access` — **so every existing consumer is
byte-for-byte unchanged**. A host wanting silence-by-default sets it once:

```csharp
o.Audit = new AuditOptions { DefaultAuditMode = AuditMode.None };
```

This is what makes the issue's request safe to grant. *"Adding a method cannot silently start recording who
read what"* holds inside such a host, without stripping the access trace from every consumer who never
asked — which, done by default, would be an audit trail quietly ending with no compile error and nothing in
a diff to notice.

### 4. Precedence: the annotation writes, the configuration filters

The attribute decides whether an entry is produced. `CompositeAuditLogger.ShouldLog` then applies
`CallerFilter`, `EventFilter` and `ExcludedActions` exactly as it does to every other entry. Two rules, each
doing one job, rather than a special case.

The compliance escape hatch falls out of the event type rather than needing a precedence exception: a method
marked `Change` writes `DataChange`, which survives a host filter that drops `ServiceCalls`.

### 5. A denial is always recorded

**`AuditMode.None` suppresses the call, not the refusal.** A scope or access-level denial is still written.

Stated as a decision because getting it wrong is silent and serious: *"who read this"* and *"who tried to
read this and was refused"* are different records with different purposes. The privacy argument in #262 —
that broad read logging is itself a hazard under *meddelarskydd* — is about recording access, not about
concealing attempts, and refusals are low-volume by nature. Suppressing them would quietly delete the
evidence that an audit log exists to hold.

## Out of scope

- Retro-fitting the mode onto the toolkit's own service interfaces. This ships the mechanism; deciding which
  of `Tharga.Team`'s own reads should go quiet is a separate pass with its own review, and doing it in the
  same PR would hide a behaviour change inside a feature.
- Anything about how entries are *read*. That was 3.21's audit-readability work.

## Acceptance criteria

1. An unannotated method behaves exactly as it does today when the host sets nothing — the regression guard
   that protects every existing consumer.
2. A host setting `DefaultAuditMode = None` gets no entry for an unannotated successful call, and a
   `[RequireScope(..., Audit = AuditMode.Access)]` method still produces one.
3. `AuditMode.Change` produces an entry whose `EventType` is `DataChange`, so the Event filter separates it.
4. A denied call is recorded even where the method resolves to `AuditMode.None`.
5. The global `AuditOptions` filters still apply to whatever the annotation produced.
6. Both attributes behave identically, pinned by tests against both proxies.
7. Full suite green.

## Version line

`MAJOR_MINOR` stays at **3.21**. The #263 merge queued a 3.21 release that has **not published** — nuget.org
is still at 3.20.1, and the plan directory records ~20 gated `release` deployments sitting at `waiting`. So
this rides in 3.21 rather than becoming 3.22, and consumers get one release rather than two minors a day
apart.

**If 3.21 is approved and published before this merges, bump to 3.22 at close-out.** Checked again then.

## Done condition

All acceptance criteria met, both doc surfaces updated, issue #262 answered with what shipped and closed,
and the user has confirmed.
