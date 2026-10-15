# Plan: team mailbox

Branch `feature/team-mailbox` off `origin/master`. FortDocs `feature/FD-676/inbox-addresses` is linked to this source with its switcher while both are developed.

## Steps

- [x] 0. NuGet update (`chore(deps)`); all 3378 tests green.
- [ ] 1. **Contracts** (`Tharga.Team`): `TeamMailKey` (key, primary/secondary, created, retires), `InboundMailItem` (id, team or none, envelope, route, retiring-key flag, outcome), `MailboxPage` with cursor; `ITeamMailboxService` (wholly team-bound, `[RequireScope]`), `IPostmasterService` (wholly system-wide), `IInboundMailContentStore` (host port), `MailboxScopes` / `SystemMailboxScopes`; notifier event for "mail arrived" so a host can push live updates.
- [ ] 2. **Address and key rules** (`Tharga.Team.Support`): key generation (RandomNumberGenerator, lowercase alphanumeric, length from options, unique), address build/parse for `{key}{suffix}[+{item}]@{domain}`, resolution key → team / primary / secondary / retired / unknown. Pure functions first, tests first.
- [ ] 3. **Rotation**: `RotateKeyAsync` makes a new primary and demotes the current one to secondary with a retirement date (option default 90 days); a third key retires the oldest at once; the first key is issued on demand (`EnsureKeyAsync`). Authorization and auditing decorators, like support.
- [ ] 4. **Mongo adapters** (`Tharga.Team.MongoDB`): key collection (unique key), inbound mail collection (unique Message-Id; team key absent = postmaster), mailbox read position; enums stored by name; purge participant; TryAdd + TrackMongoCollection.
- [ ] 5. **Receiving**: a mailbox poller over the existing `Email` transport (raw MIME added to the fetch), routing by delivery headers, content handed to `IInboundMailContentStore` before the unique insert so a crash can only re-read, never lose. Multi-instance tests (same message twice → one item).
- [ ] 6. **Postmaster**: list (sender, subject, received only), open (audited), route to a team (marked re-routed), retention purge (option, audited).
- [ ] 7. **Blazor**: `PostmasterView` and a team-mail-addresses view with `*Text` keys.
- [ ] 8. **Docs and version**: README, `docs/articles/team-mailbox.md`, bump `MAJOR_MINOR` to 3.26.

## Last session

- 2026-10-15: Branch created, packages updated. Decisions recorded in `feature.md`. Next: confirm the plan, then step 1.
