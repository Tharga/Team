# Audit retention: `RetentionDays` decides how long entries are kept (#429)

## Goal
`AuditOptions.RetentionDays` controls how long audit entries live, in a new database and in an existing one.

## Scope
1. **`null` means keep forever.** `AuditRepositoryCollection` turns `RetentionDays = null` into 90
   (`?? 90`), so a `Timestamp_TTL` index with a 90-day expiry is created and entries are deleted. The default
   of 90 already lives on the `AuditOptions` property; the collection must pass `null` on to
   `AuditRetention.GetExpireAfter`. Only a missing `IOptions<AuditOptions>` falls back to the default.
2. **A changed setting reaches an existing database.** The TTL index is always named `Timestamp_TTL`, and
   under the default `AssureIndexMode.ByName` an index that already exists is left alone. On startup, when
   audit storage is MongoDB, the audit collection makes the index match the setting:
   - existing `expireAfterSeconds` differs → `collMod` changes it in place (no rebuild);
   - retention is now "keep forever" and the index exists → drop it;
   - retention is finite and there is no index → create it;
   - nothing differs → do nothing.
   Every change is logged at Information level, with the old and the new value.
3. Docs: the `AuditOptions.RetentionDays` remark that says the index may have to be dropped by hand is
   replaced; `implementation-guide.md` and the `Tharga.Team.Service` README say what happens on startup.

## Out of scope
- The retention text and UI in `Tharga.Team.Blazor` (`AuditRetentionText`) already follows `RetentionDays`
  and is not changed.
- No new option. The setting is the one that exists.

## Acceptance criteria
- `RetentionDays = null` (and `0`, negative) creates no TTL index and leaves entries to be kept.
- A database that has `Timestamp_TTL` at 90 days, started with 427, has it at 427 days afterwards.
- A database that has `Timestamp_TTL`, started with `null`, has no `Timestamp_TTL` afterwards.
- A database with no `Timestamp_TTL`, started with 90, has one at 90 days afterwards.
- A start where nothing differs writes nothing and logs nothing at Information.
- A failure of the reconciliation is logged and does not stop the host from starting.
- Tests cover the decision (pure) and, where a MongoDB is available, the real `collMod`.

## Done when
The acceptance criteria hold, the full suite passes, docs are updated, and #429 is closed through the PR
with a comment naming the type and member that carries each half.
