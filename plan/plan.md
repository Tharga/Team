# Plan: Sessions part 2 - live sign-out across instances and a "Still here?" warning (Tharga/Team#471)

Branch `feature/sessions-part-2-live-sign-out`, from `origin/master` 737eecf6. Version: additive, a patch (MAJOR_MINOR stays 4.8).

## Missing documents - maintainer to check

This session ran with `$DOC_ROOT` unset. `$DOC_ROOT/Tharga/shared-instructions.md` and
`$DOC_ROOT/Tharga/plans/Toolkit/Platform/architecture-v4.md` could not be read. Gustav (repo collaborator, who runs this machine) decided on
2026-11-26 to proceed without them. Everything else in `.claude/CLAUDE.md` and `.claude/mission.md` was followed. The maintainer should check
this plan, the code and the PR against both documents (versioning, changelog, PR rules, the v4 design rules) before merge. The design is
believed neutral to v4: every new contract type is plain data (no generics, no interface returns), the end notification names the person and never
the session, and the enforcement point stays the domain service plus the store's conditional writes.

## Scope

1. An open circuit on any instance learns within seconds that its session ended (this instance at once; other instances when the host relays).
2. A "Still here?" warning before an idle sign-out, coordinated across browser tabs.

Not included: ending another person's sessions, a heads-up before max age, an audit entry for `EndOtherSessionsAsync` (filed as Tharga/Team#569).

## Public API

- `IUserSessionNotifier` (`SessionEnded` event, `Notify`) and `UserSessionEndedEventArgs { required UserKey, required Reason, FromRemote }`.
- `ITeamEntryNotifier` (`Invalidated` event, `Notify`) and public `TeamEntryInvalidatedEventArgs { required UserKey, TeamKey, FromRemote }`.
- `SessionOptions.IdleWarning` (default 1 minute, Zero = none, clamped to IdleTimeout - 2 x ActivityInterval).
- `<SessionWatch />` component; `SessionText` keys.

## Steps

- [x] 1. Plan (this file).
- [~] 2. Tests first, with stubs so both target frameworks compile (public API with docs, internal types throwing NotImplementedException, empty `SessionWatch.razor`, JS exports as no-ops).
- [ ] 3. `UserSessionService`: notifier, `SignOutAsync(principal)`, notify only when this call ended something.
- [ ] 4. `SessionCache` / `TeamEntryCache` as notifiers; the revalidating provider subscribes to `ITeamEntryNotifier`.
- [ ] 5. `SessionEvaluator`: `SessionCheck.Session/Limits`, `InspectCircuitAsync`, `ConfirmPresenceAsync`, `SignOutAsync`.
- [ ] 6. `SessionDeadlines`, `SessionCircuit`, `SessionCircuitHandler`, registration.
- [ ] 7. `/logout?reason=signedout`; `LoginDisplay` renders `<SessionWatch />`; `SessionWatch.razor`; `session-activity.js`.
- [ ] 8. Docs (implementation guide "Session limits", entry guard, READMEs), sample (ActivityInterval 20 s).
- [ ] 9. PR "Sessions: live sign-out across instances and a 'Still here?' warning (#471)", closes #471; remove `plan/` before the PR.

## Decisions (made while writing the tests; the code must follow them)

- Internal shape: `ISessionCircuit` { Active, State (Watching / Warning / Ended), IdleAt, Remaining, EndedLocation, Changed, Attach(), StayAsync(), SignOutAsync(), CheckAsync(), StartAsync(), Dispose }.
  `SessionCircuit` adds `SettledAsync()` (the in-flight check, for tests).
- Constructor: `SessionCircuit(SessionEvaluator, AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider, IUserSessionNotifier, IOptions<ThargaBlazorOptions>, TimeProvider, ILogger<SessionCircuit> = null)`.
- The circuit learns the person's key from `SessionCheck.Session.UserKey`, so it needs no `IUserService`.
- `SessionEvaluator.InspectCircuitAsync` lets a store/policy failure propagate (the circuit tells failure from refusal); `Rejected` is not an end either.
- No idle warning when max age ends the session no later than the idle deadline (staying would not help).
- `EnterTeamExclusiveAsync` raises one `NewerSignIn` notification per call that ended something (superseded or caller), not one per session.
- A relayed `Notify(FromRemote = true)` on `TeamEntryCache` forgets that user's cached answers before the handlers run (like `SessionCache` forgets sessions first); otherwise a re-evaluation would read the stale answer.
- `/logout?reason=signedout` (that literal only) lands on `SessionCookieEvents.EndedLocation(EndedPath, ...)` when sessions are active; every other value, or sessions off, leaves the redirect as it was.
- `SessionWatch` loads the module and attaches only when `ISessionCircuit.Active`.

## Last session

2026-11-26: plan written; tests-first commit in progress.

README changes needed when complete: `Tharga.Team.Blazor` README (SessionWatch, IdleWarning), root README if it lists the options.
