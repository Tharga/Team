# Plan: invite-link-wiring

- [x] 1. NuGet update — Microsoft.Identity.Web 4.14.2 → 4.15.0, Moq 4.20.72 → 4.21.0. Build green, 3028 tests
  green (run via the test executables; local SDK 10.0.301 reports zero tests through `dotnet test`).
- [x] 2. Confirm the plan with the user. Confirmed as written.
- [x] 3. #286 service half: `TeamServiceCompleteness.Find` reports `GetTeamKeyByInviteKeyInternalAsync`
  unconditionally. The direct-`ITeamService` case was dropped: `RegisterTeamService` constrains to
  `TeamServiceBase`, so it cannot reach the check. The check's message is now generic (each gap carries its own
  remedy). `StubTeamService` gained the override; `StubTeamServiceWithoutInviteLookup` is the reported shape.
- [x] 4. #286 repository half: `InviteLookupRepositoryCheck` in `Tharga.Team.MongoDB`, registered beside the
  repository, detects a default-bodied `GetByInviteKeyAsync` via the interface map. Logs an error, never throws.
- [x] 5. #286 docs: corrected on `ITeamService`, `TeamServiceBase` and `ITeamRepository`. Also moved the
  orphaned `GetInvitationInternalAsync` doc back onto its member and removed its claim about a non-existent
  `InvitationExpiryWiringCheck` (the expiry check itself stays out of scope).
- [x] 6. #285: `InvalidLink`, `AlreadyMember`, `Expired` keys; the view maps the resolved invitation to one
  outcome and shows its message outside `AuthorizeView` (anonymous visitors see it too). Join buttons only when
  Open. Stored code cleared for all three. `InviteViewOutcomeTests`: 12 cases.
- [~] 7. Full suite (3054 green), commit, push for user testing — push awaits the user's approval.
- [ ] 8. Close-out (on user's go): NuGet re-check, README/docs, backlog + issues, archive, remove `plan/`.

## Notes

README changes expected: the extension-point table in `Tharga.Team/README.md` (GetTeamKeyByInviteKeyInternalAsync
row), and `docs/articles/implementation-guide.md` wherever it describes the completeness check.
