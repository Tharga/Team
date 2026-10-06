# Plan: configurable user identity claim

- [x] NuGet update (chore(deps) commit) - all suites green before feature work
- [x] UserIdentityResolver + tests (default unchanged, oid over sub, mapped/unmapped oid)
  - Two defaults kept separate on purpose: user identity = Toolkit GetIdentity chain, subject = NameIdentifier only. Configured, both read the listed types with no fallback.
- [x] Option on ThargaTeamOptions, registered per host; wire into UserServiceBase via the registration
  - `UserIdentityClaimTypes`; AddThargaTeam registers the resolver, AddThargaTeamBlazor TryAdds the default. The concrete user service is now built by a factory so it is wired whether resolved directly or via IUserService.
- [x] Call sites: UserServiceBase, UserServiceRepositoryBase, TeamAuthorizer, AuditHelper, AuthAuditEntries, MCP
  - Display-name chains (CallerIdentity, API-key createdBy, GetDisplayNameAsync) left alone: human-readable, not keys.
- [x] Call-site tests (repository, current user, authorizer subject, audit, MCP, registration wiring) - 49 new, 3181 total green
- [~] Docs: README sections
- [ ] Close-out: remove plan/, push, PR
