# Feature: Scope enforcement check

## Goal

The suite fails when a `[RequireScope]` attribute is not enforced. Today nothing reads the attribute on a
service that is registered plainly, and only a code comment warns about it.

This closes §3b of `plans/Toolkit/Platform/planned/01-team-bound-service-authorization.md`, using option 1
from the 2026-08-09 note: a CI test plus a documented exemption list. Decided with the user 2026-10-25.

## Background

`[RequireScope]` is enforced in one of two ways:

- **By `ScopeProxy`**, when the interface is registered with `AddTeamService` / `AddSystemService` (or the
  proxy is built by hand, as `IIconSettingsService` does). The proxy reads the attribute and fails closed on
  a method without one.
- **By hand**, for three interfaces that cannot take the proxy:
  - `ITeamManagementService`. Its reads are checked in `TeamManagementService<TMember>`
    (`RequireTeamReadAsync`). Its changes are checked downstream, by `AuthorizationTeamServiceDecorator`
    over `ITeamService`. The proxy cannot be used because the claims pipeline reads team data while building
    the principal.
  - `IUserService`, through `AuthorizationUserServiceDecorator`. It mixes self-service with administration.
  - `IUserManagementService`, through `AuthorizationUserManagementServiceDecorator`.

On those three the attribute is documentation. Adding a method and attributing it gets no check, unless
someone also writes the check by hand.

## Scope

1. **Registration test.**
   - Discover every public interface with a `[RequireScope]` method, in every shipped `Tharga.Team.*`
     assembly.
   - Resolve each one from the real composition.
   - Each must resolve to a `ScopeProxy`, or appear on the exemption list.
   - A stale entry fails too: an exempted interface that is now proxied, or no longer carries the attribute.
   - So does an interface the test cannot resolve. The fix is to compose its module in the test.
2. **Exemption list.** One entry per self-checking interface, naming where its checks live and why the
   proxy cannot be used.
3. **Proof test.** Every `[RequireScope]` method on an exempted interface must refuse a caller who holds no
   scope, with `UnauthorizedAccessException`.
   - Called by reflection through the real composition, over an in-memory store.
   - A method that is checked some other way is listed with its reason. A new method fails until it is
     checked or listed.
   - This makes the list a proof rather than a promise, which is why the plan rejected option 2 (the
     marker).
4. **Findings.** Methods the proof test shows are not checked by their scope get a decision each, shown to
   the user before any product code changes. Expected from reading the code:
   - `SetInvitationResponseAsync` carries `team:read` but is authorized by its invite code.
   - `SetMemberLastSeenAsync` carries `team:read` and passes straight through.
   - `TransferOwnershipAsync` is checked inside `TeamServiceBase`, with `InvalidOperationException`.
5. **Docs.**
   - Remarks on `RequireScopeAttribute`: it is enforced only through the proxy.
   - Mention the guard in `docs/articles/architecture.md`.
   - Mark Plan 01 §3b done, and update the backlog line.

## Out of scope

- A check at startup (option 2) and moving the facets onto the proxy (option 3).
- Plan 01 §6b, the database-layer guard.
- A consumer's own interface registered with a plain `AddScoped`. That is already documented
  (`implementation-guide.md`, "A service registered with a plain `AddScoped`…").

## Acceptance criteria

- [ ] An attributed method on a plainly registered interface fails the suite, naming the interface and both
      fixes (register through the proxy, or add an exemption).
- [ ] An attributed method added to an exempted interface without a check fails the suite, naming the
      method.
- [ ] Every exemption is proven by behaviour, or listed per method with its reason.
- [ ] Discovery is guarded against covering nothing.
- [ ] Public behaviour is unchanged, unless a step-4 decision says otherwise. Any such change is called out
      in the PR.
- [ ] Full suite green; warnings stay at 14.

## Done condition

The user confirms it is done, and the PR is merged.
