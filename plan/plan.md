# Plan: refresh a user's name and email from trusted sign-ins (#320)

- [x] 0. NuGet update — `chore(deps): nuget update` (dcf0f63): DataProtection 10.0.13, Identity.Web 4.16.1,
      Extensions.AI 10.11.0. 3378 tests green from the executables.
- [x] 0b. `mission.md` records the tracker decision: file-based with GitHub issues.
- [ ] 1. `IUserRepository.SetEMailAsync` (default throws) + `UserRepository` implementation; tests in
      Tharga.Team.MongoDB.Tests.
- [ ] 2. `UserServiceBase`: `ShouldRefreshProfile`, the refresh in `TouchUserAsync`, `SetUserProfileAsync` (default
      throws), `UserProfileRefreshedEvent` + args, per-process attempt guard; tests beside
      `UserServiceBaseActivityStampingTests`.
- [ ] 3. `UserServiceRepositoryBase.SetUserProfileAsync` through the repository; tests.
- [ ] 4. Audit: `AuthAuditEntries.UserProfileRefreshed` + subscription in `ThargaBlazorRegistration`; tests.
- [ ] 5. Docs: `docs/articles/user-management.md`, `README.md`.
- [ ] 6. Close-out: NuGet re-check, #320 comment, archive `plan/feature.md`, remove `plan/`, push + PR (with approval).

## Last session

2026-10-15: issue #320 filed, branch created, packages updated, plan written. Awaiting plan confirmation.
