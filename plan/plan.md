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
- [~] 3. `UserServiceRepositoryBase.SetUserProfileAsync` through the repository; tests.
- [ ] 4. Audit: `AuthAuditEntries.UserProfileRefreshed` + subscription in `ThargaBlazorRegistration`; tests.
- [ ] 5. Docs: `docs/articles/user-management.md`, `README.md`.
- [ ] 6. Close-out: NuGet re-check, #320 comment, archive `plan/feature.md`, remove `plan/`, push + PR (with approval).

## Last session

2026-10-15: issue #320 filed, branch created, packages updated, plan written. Awaiting plan confirmation.
