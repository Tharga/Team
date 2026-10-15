# Plan: refresh a user's name and email from trusted sign-ins (#320)

- [x] 0. NuGet update — `chore(deps): nuget update` (dcf0f63): DataProtection 10.0.13, Identity.Web 4.16.1,
      Extensions.AI 10.11.0. 3378 tests green from the executables.
- [x] 0b. `mission.md` records the tracker decision: file-based with GitHub issues.
- [x] 1. `IUserRepository.SetEMailAsync` (default throws) + `UserRepository` implementation; tests in
      Tharga.Team.MongoDB.Tests.
      `UserRepositoryEMailTests` (3): writes `EMail` on the user found by key; a host repository that never
      implemented it gets `NotSupportedException` naming the type. No shape guard - `EMail` is a required `IUser`
      member, unlike Name/DirectoryId/LastSeen. Full suite 3381 green.
- [x] 2. `UserServiceBase`: `ShouldRefreshProfile`, the refresh in `TouchUserAsync`, `SetUserProfileAsync` (default
      throws), `UserProfileRefreshedEvent` + args, per-process attempt guard; tests beside
      `UserServiceBaseActivityStampingTests`.
      `UserServiceBaseProfileRefreshTests` (14). The refresh has its own try block after the activity stamp, so one
      failing never skips the other. Decision: the name comes from `GetDisplayName` over the claims *without* the
      email claims - its last fallback is a name guessed from the address ("anna.svensson@" -> "Anna Svensson"),
      which a test caught; a guess must never overwrite a real name. Email compared case-insensitively, name
      ordinally; values trimmed; empty never overwrites. Full suite 3395 green.
- [x] 3. `UserServiceRepositoryBase.SetUserProfileAsync` through the repository; tests.
      `UserServiceRepositoryBaseProfileRefreshTests` (3), through the real resolve path: only the changed value is
      written (`SetNameAsync` / `SetEMailAsync`). Full suite 3398 green; no new warnings (all 13 unique warnings on a
      clean build are pre-existing XML-doc ones in files this branch does not touch).
- [x] 4. Audit: `AuthAuditEntries.UserProfileRefreshed` + subscription in `ThargaBlazorRegistration`; tests.
      Action `user-profile-refreshed`, DataChange, the user as actor, subject via the host's resolver. Metadata:
      `user.key`, `changed` (`name`, `email` or both) and the resulting `user.email`; the name itself is not copied
      into the log. Subscribed beside `UserCreatedEvent`, same try/log guard. Tests: 3 shape
      (`AuthAuditEntriesTests`), 2 wiring (`UserProfileRefreshAuditWiringTests`: audited; a throwing audit logger
      does not fail the resolve). Full suite 3403 green.
- [~] 5. Docs: `docs/articles/user-management.md`, `README.md`.
- [ ] 6. Close-out: NuGet re-check, #320 comment, archive `plan/feature.md`, remove `plan/`, push + PR (with approval).

## Last session

2026-10-15: issue #320 filed, branch created, packages updated, plan written. Awaiting plan confirmation.
