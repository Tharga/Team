# Plan: audit entries name the caller inside a Blazor Server circuit (#572)

## Decisions (to confirm)

- **Fix it once, in `AuditHelper.BuildEntry`, not only in the team decorator.** The issue suggests giving the
  team decorator the principal accessor the proxies use. That would fix only that decorator: the API key and
  icon settings decorators and every `IAuditEntryFactory` writer (support, mail, team entry) have the same gap.
  `IAuditEntryFactory` is a singleton and cannot hold a per-circuit accessor. The team decorator also logs
  synchronously from about twenty places. One fallback in the helper covers all of them, with no public API
  change.
- **Published per inbound circuit activity**, the way `AccessSimulationCircuitHandler` already does it for
  simulation metadata (#220). One handler, always registered for Blazor Server, replaces that one, and the
  simulation enricher reads the shared ambient.
- **Who wins:** an authenticated request principal, then the circuit's principal, then a declared background
  actor (`AuditActor`), then unknown. This keeps the existing rule that a person, when there is one, wins over
  an ambient actor.
- **It flows into continuations, and ends with the circuit.** ASP.NET Core wraps only the start of an event
  handler in the inbound activity (events arrive as JS-to-.NET calls), so the rest of an async handler runs
  after the activity returns. The published value therefore flows like any `AsyncLocal` and is restored, not
  cleared, when the activity ends. To keep a timer or background loop started during an activity from naming
  the person later, the circuit holds one shared holder, refreshed on each activity and cleared when the
  circuit closes.
- **Internal, so it cannot be forged.** The ambient is internal to `Tharga.Team.Service` and visible to
  `Tharga.Team.Blazor` only. Host code cannot set who an entry is attributed to.
- **`InvitedBy` on invitations** (the issue's "also worth considering") is left for its own issue.

## Steps

- [ ] 0. NuGet check: SkiaSharp and SkiaSharp.NativeAssets.Linux.NoDependencies 4.157.0 → 4.158.0 in
      Tharga.Team.Images (both targets) is the only update not under a recorded hold; apply it as its own
      `chore(deps): nuget update` commit.
- [ ] 1. Service: internal circuit-caller ambient; `BuildEntry` falls back to it. Tests.
- [ ] 2. Blazor: one always-registered circuit handler publishes it; simulation enricher reads it; the old
      accessor and handler go. Tests, including continuations and circuit close.
- [ ] 3. The issue's case end to end: team decorator in a circuit records the inviter, and the `team:invite`
      notification names them. Test.
- [ ] 4. Docs: audit article (circuit callers), Blazor README if it mentions audit in circuits.
- [ ] 5. Full test suite, push the branch for testing.

## Last session

2026-11-27: branch created, issue assigned, plan written. Next: confirm the decisions above, then step 0.

## README changes needed at completion

Probably none beyond the audit article; check the Blazor README for any note on circuits and audit.
