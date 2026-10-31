# Plan: audit retention (#429)

Branch `fix/audit-retention-days`, from `master` at b1ab0da.

## Decisions (2026-10-31, from the user)
- Q1: a hosted service (`AuditRetentionIndexService`), registered with the MongoDB audit storage.
- Q2: skip when the host turned index assurance off (`AssureIndex = AssureIndexMode.Disabled`).
- Q3: release note names the behaviour change (a host that set `null` stops losing entries; its 90-day index is dropped on first start).
- Not configured at all → 90 days. Explicit `null` → forever.

## Steps
- [x] 1. `chore(deps): nuget update` — `AngleSharp` 1.8.6 → 1.9.0 (Tharga.Team.Email); full suite green (6,465 tests). `Microsoft.AspNetCore.App.Internal.Assets` 10.0.9 → 10.0.14 is an SDK auto-reference of the sample, not in any csproj, so it follows the installed SDK and cannot be updated here.
- [x] 2. Tests first: `AuditRetentionTests` (`ResolveDays`: no options → 90, unset → 90, explicit null → null, 427 → 427, and through `AddThargaAuditLogging`) and `AuditRepositoryCollectionIndexTests` (null → no `Timestamp_TTL`; 427 → 427 days).
- [x] 3. Part 1: `AuditRetention.ResolveDays` replaces `?? 90` in `AuditRepositoryCollection`.
- [x] 4. Tests first for the decision: `AuditRetentionIndexTests` (`Decide`, `Find`, `ChangeCommand`, registration, skip, failure).
- [x] 5. Part 2: `AuditRetentionIndex` (pure decision, index lookup, `collMod` command) and `AuditRetentionIndexService` (BackgroundService; create / `collMod` / drop; Information log per change; warning and carry on on failure; skipped when assurance is disabled).
- [~] 6. Real-database test of `collMod`: **not possible here** — no `mongod` on this machine and the repo has no database-test setup. The MongoDB calls themselves (`Indexes.ListAsync`, `RunCommandAsync`, `DropOneAsync`, `CreateOneAsync`) are therefore untested against a server. Needs a run against a real database before release.
- [x] 7. Docs: `AuditOptions.RetentionDays` remark, `implementation-guide.md` (reconciliation + upgrade note), `Tharga.Team.Service/README.md`.
- [x] 8. Full suite green (Service 1,566; Blazor 2,368; Email 985; Support 865; MongoDB 481; Mcp 121; Entra 58; Anthropic 17; OpenAI 16; Images 11).
- [ ] 9. Push the branch (waiting for the user's yes). Do not open the PR until the user has tested.

## Notes
- Cannot reflect `Tharga.MongoDB` here (the shell refused it); relied on its XML docs and the `GridFsSupportAttachmentBucket` pattern. `AddMongoDB`'s `AssureIndex` was verified to reach `IOptions<DatabaseOptions>` by a test.
- At close: version line / release-notes PR text (behaviour change, upgrade note), close #429 with a comment naming `AuditRetention.ResolveDays`, `AuditRetentionIndexService`.

## Last session
2026-10-31: steps 1–8 done, committed locally on the branch, not pushed. Next: user decides on pushing; someone runs the service once against a real MongoDB (an environment with an audit collection whose `Timestamp_TTL` differs from the setting) to confirm `collMod`.
