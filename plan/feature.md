# Feature: soft-delete teams, with restore and purge — and fix the delete path (#224)

Closes [Tharga/Team#224](https://github.com/Tharga/Team/issues/224) and the **Soft-delete teams** backlog
item, together, because the issue's own third section asks for exactly that: *"if soft delete marks the team
and hides it, and only Purge drops the database, then the everyday delete path needs no elevated database
rights at all."*

## Goal

Make team deletion recoverable, and confine `dropDatabase` to a rare, deliberate **Purge**.

Reported by **Eplicta FortDocs**, running `DatabasePart = teamKey` so every team is its own database.
Deleting a team throws a raw `MongoCommandException` into the UI because their CI Atlas user cannot
`dropDatabase` — and `readWriteAnyDatabase` does not include it. **Team deletion is blocked in their CI
right now.**

## The three defects in #224

1. **No error handling.** `TeamServiceRepositoryBase.DeleteTeamAsync` wraps nothing, so a permission refusal
   reaches the error page as a driver stack trace for what is really "this deployment may not drop
   databases".
2. **Ordered the dangerous way round.** The database is dropped *before* the team record is deleted, with
   nothing tying them together. FortDocs was lucky — the drop threw first. Reversed, a transient failure
   after a successful drop leaves a **live team pointing at deleted data**: it still lists, still resolves,
   still authorizes, and every read returns empty. Deleting the record first fails safe, leaving an orphaned
   database a sweep can find.
3. **Everyday deletion needs a high privilege.** A permanent, wide `dropDatabase` grant held just so an
   admin can occasionally delete a team.

## Decisions taken up front (user, 2026-08-12)

- **Soft delete is the default**, and this ships as a **patch — 3.13.1. `MAJOR_MINOR` is not bumped.**
  - Defensible because no public signature changes and `DeleteTeamAsync` still makes the team vanish from
    every read; the only observable difference is that the document survives in storage.
  - **That holds only if the read-path sweep is complete.** A missed read path means a deleted team still
    visible — and shipped as a patch, which is worse than shipping it as a minor. The sweep is therefore the
    load-bearing part of this feature, and is treated as such below.
- **`teams:purge` is a new system scope.** `teams:delete` covers soft delete *and* restore — restore is
  strictly less destructive than the delete it undoes. Purge gets its own because it is the only
  irreversible operation and the only one needing elevated database rights. A deployment that never purges
  can withhold both the scope and the `dropDatabase` grant entirely, which is the privilege boundary #224
  asks for.
- **A soft-deleted team's key stays reserved.** Re-creating with the same key is refused, naming the deleted
  team so the operator can restore or purge. In a `DatabasePart` deployment, releasing the key would point a
  new team at the old team's database — the corruption #224 is worried about, arriving by a different route.

## Shape, against the v4 target

Read `architecture-v4.md` first, per the mission. This lands on the storage seam, which v4 says survives
untouched — so its shape matters more than most.

- **Rule 1, operations not CRUD.** Three operations — `DeleteTeamAsync` (soft), `RestoreTeamAsync`,
  `PurgeTeamAsync`. Not a `SetTeamDeleted(bool)`, which could not be authorized or audited as one fact.
- **Rule 4, ports speak the domain's language.** The seam expresses *purge*, never *drop database*. Dropping
  is one adapter's way of purging; a SQL adapter would delete rows.
- **Rule 5, the port expresses atomicity.** Purge spans two writes — record and storage — and the ordering
  is the fix for defect 2. The seam states the order it requires rather than leaving it to each adapter.
- **Read-path filtering lives behind the port**, as the backlog insists, so v4 does not have to redo it.

**New seam members are `virtual`, never `abstract`.** Adding an abstract member to `TeamServiceBase` breaks
every host that derives from it, at compile time — unacceptable in a patch. Precedent: the cross-team
visibility work shipped `virtual` base members plus a default interface method on `ITeamRepository<,>` for
exactly this reason.

## The read-path sweep

Every read must exclude soft-deleted teams. Missing one is a silent data leak rather than a visible bug:

`GetTeamsAsync` · `GetAllTeamsAsync` · `GetTeamByKeyAsync` · `GetConsentedTeamsAsync` · team selection
(`TeamStateService`, `TeamSelector`) · the MCP `team://` resources · **the claims pipeline** — a user must
not stay authorized on a deleted team.

**This gets a test that enumerates the paths, not spot fixes.** The natural shape is a guard that reflects
over the read surface and fails when a new read is added without filtering — the same reasoning as
`InternalServiceInjectionTests`: a convention nobody can run is how the hole reopens.

## Out of scope

- Automatic purge after a retention period. Worth having, but it needs the `AuditOptions.RetentionDays`
  lesson applied (`null` means forever, `0` must not mean instant) and that is a separate decision.
- Soft-deleting **users**. Different lifecycle, different scope, not asked for.
- Changing what `dropDatabase` does when it is called; only *when* it is called.

## Acceptance criteria

- [ ] `DeleteTeamAsync` soft-deletes by default and needs no `dropDatabase` right.
- [ ] `PurgeTeamAsync` deletes the team record **before** dropping storage, and requires `teams:purge`.
- [ ] A storage failure during purge surfaces as a caught, readable error naming the missing privilege —
      not a driver stack trace.
- [ ] `RestoreTeamAsync` brings a soft-deleted team back, authorized by `teams:delete`, never gated on
      anything a delete could remove.
- [ ] Every read path listed above excludes soft-deleted teams, **proven by an enumerating test** rather
      than by spot checks.
- [ ] The claims pipeline does not authorize a caller on a soft-deleted team.
- [ ] Re-creating a team whose key belongs to a soft-deleted team is refused, naming that team.
- [ ] Delete, restore and purge are audited, with metadata.
- [ ] No new `abstract` member on `TeamServiceBase`; an existing host compiles unchanged.
- [ ] `MAJOR_MINOR` stays `3.13`.
- [ ] Build clean, full test suite green.

## Done condition

FortDocs can delete a team in an environment without `dropDatabase` rights, restore one, and purge only
where the grant exists — and the user confirms.
