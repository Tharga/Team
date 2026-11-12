# Plan: user-identity-link

- [x] 1. NuGet check: nothing beyond the recorded holds.
- [x] 2. Contracts, repository, service, decorators (authorization, audit, cache), sign-in hook.
- [x] 3. Blazor: badge, row action, dialog, text keys.
- [x] 4. Tests and docs.
- [~] 5. Push for testing; on the users word close-out, PR, close #475.

## Decisions
- Re-point the identity on the kept user instead of moving memberships: key stays, nothing else to migrate.
- Duplicate with team memberships is refused, not merged.
- Auto-link only through a host override, off by default, verified email is the hosts call.

## Release notes
New `IUserManagementService.LinkIdentityAsync` (default member), `IUserService.SetUserIdentityAsync` and `IUserRepository.SetIdentityAsync` (default members that throw), `UserServiceRepositoryBase.ShouldLinkToExistingUserByEmail`. Decorators of `IUserService` must forward `SetUserIdentityAsync`.
