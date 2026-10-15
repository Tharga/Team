# Plan: Tharga.Team.Email and the team mailbox

Branch `feature/team-mailbox` off `origin/master`. FortDocs `feature/FD-676/inbox-addresses` is linked to this source with its switcher while both are developed.

## Steps

- [x] 0. NuGet update (`chore(deps)`); all 3378 tests green.
- [x] 1. **New package, pure move.** `Tharga.Team.Email` + `Tharga.Team.Email.Tests` (in the solution, on the pack list, in the root README): the transport moved with history into namespace `Tharga.Team.Email`, MailKit with it; `ISupportMailClient` → `IMailClient`, `SupportMailClient` → `MailClient` (renamed now so the new package is not renamed twice). Support references the package; internals visible to Support and the tests. Transport tests moved (79); Support 402; all 3457 green; FortDocs Web 11080 green against it. The namespace-isolation guard scans both assemblies; the read-only scan points at the new folder and skips build output. Migration table in the package README.
- [ ] 2. **Contracts** (`Tharga.Team`): `TeamMailKey` (key, primary/secondary, created, retires), `InboundMailItem` (id, team or none, envelope, route, retiring-key flag, outcome, owner), `MailThreadOwner` (type, id), paged results with cursor; `ITeamMailboxService` (wholly team-bound, `[RequireScope]`), `IPostmasterService` (wholly system-wide), `IInboundMailContentStore` (host port), `MailboxScopes` / `SystemMailboxScopes`; a "mail arrived" notification for hosts.
- [ ] 3. **Address and key rules** (`Tharga.Team.Email`): key generation (RandomNumberGenerator, lowercase alphanumeric, length from options, unique), address build/parse `{key}{suffix}[+{item}]@{domain}`, resolution key → team / primary / secondary / retired / unknown. Pure functions, tests first.
- [ ] 4. **Rotation**: `EnsureKeyAsync` issues the first key; `RotateKeyAsync` makes a new primary and demotes the current one to secondary with a retirement date (option default 90 days); a third key retires the oldest at once. Authorization and auditing decorators.
- [ ] 5. **Thread tracker**: Message-Id and tracking reference for outgoing mail; for incoming mail, find the owner from In-Reply-To/References, the plus-address and a subject token. Durable (no TTL), and recorded with the item in one unique insert.
- [ ] 6. **Mongo adapters** (`Tharga.Team.MongoDB`): key collection (unique key), inbound mail collection (unique Message-Id; no team = postmaster), thread index, read position; enums by name; purge participant; TryAdd + TrackMongoCollection.
- [ ] 7. **Receiving**: mailbox poller over the moved transport (raw MIME added to the fetch), routing by delivery headers, raw content handed to `IInboundMailContentStore` before the unique insert. Multi-instance test: the same message twice → one item.
- [ ] 8. **Postmaster**: list (sender, subject, received only), open (audited), route to a team (marked re-routed), retention purge (option, audited).
- [ ] 9. **Blazor**: `PostmasterView` and a team mail-address view with `*Text` keys.
- [ ] 10. **Invitations onto the shared transport.** `SmtpTeamEmailSender` (System.Net.Mail, in `Tharga.Team.Service`) is replaced by a MailKit-based `ITeamEmailSender` in `Tharga.Team.Email`, switched on with `AddThargaEmailInvitations()` and keeping its own options (`noreply@` stays separate from support's reply address, as `ITeamEmailSender`'s remarks require) and the `IOutboundMailPolicy`. `Tharga.Team.Service` and `Tharga.Team.Blazor` keep no SMTP code and never reference `Tharga.Team.Email`. A startup warning when SMTP invitation settings exist but no sender is registered, since the manual-link fallback looks like success. Migration note: install `Tharga.Team.Email` and add the call; custom senders are unaffected.
- [ ] 11. **Docs and version**: README, `docs/articles/team-email.md`, bump `MAJOR_MINOR` to 3.26.

Follow-up PR (not this branch): support onto the thread tracker, closing the ledger gaps. With FortDocs FD-769: outbound send with tracking reference, and the rich-text composer component.

## Last session

- 2026-10-15: Decided to move the invitation sender onto the shared transport too (step 10), in the same 3.26 release so consumers migrate once.
- 2026-10-15: Branch created, packages updated. Decided: a separate `Tharga.Team.Email` package shared by support and the organisation mailbox; the case stays per product. Next: confirm the plan, then step 1.
