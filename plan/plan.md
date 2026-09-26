# Plan: invitation seams on a custom store

Spec: `plan/feature.md`. Branched from `origin/master` at `bc4346f`; `origin/master` (`f318804`, PR #302) merged
in 2026-09-26 — merged rather than rebased, because the branch was already pushed.

- [x] 0. `chore(deps): nuget update` — Moq 4.20.72 → 4.21.0 (test project only). Full suite 3,028 green.
- [x] 1. **Expiry — tests first.** `InvitationDefaultLookupTests` (Service.Tests), via a new
      `TestTeamService.UseDefaultInvitationLookups` switch. Went red on exactly the three expected cases: expired
      accept, unknown code with a lifetime, invited name not carried over.
- [x] 2. **Expiry — the fix.** `GetInvitationInternalAsync` and `GetInvitedMemberNameAsync` default to reading the
      roster through `GetMembersAsync`; accept refuses a code it cannot find when a lifetime is set (same message as
      `ExtendInvitationAsync`, now a constant). Service.Tests 1,079 green. The orphaned doc had already been fixed on
      master by #302; its "no startup check reports it" remark is replaced by the new behaviour.
- [x] 3. **Short links — startup rule.** Already shipped on master by #302 (`9b0b7a5`,
      `InviteLookupCompletenessCheckTests`). Nothing left to do here.
- [x] 4. **Repository layer.** `ITeamRepository.SetInvitationExpiryAsync` default now throws `NotSupportedException`
      naming the type. Master's check renamed `InviteLookupRepositoryCheck` → `InvitationRepositoryCheck` (internal)
      and also reports `SetInvitationExpiryAsync` left at the default — only when `InvitationOptions.Lifetime` is set,
      since without one nothing extends an invitation on its own. Tests renamed to `InvitationRepositoryCheckTests`
      (11 green), including the built-in `TeamRepository<,>` implementing both.
- [x] 5. **Docs.** `Tharga.Team/README.md` contract table + startup-check paragraph; implementation guide
      *Invitations that expire* now has three custom-store notes. Misplaced `SetDeletedAsync` summary moved onto its
      member in `ITeamRepository.cs`. Version wording avoids naming the unreleased patch (only 3.23.0 is tagged).
- [x] 6. Full suite 3,065 green (`dotnet test`, SDK 10.0.302). Sample boots with no check errors — note it sets no
      lifetime, so the expiry half is covered by the unit test, not the boot.
- [~] 7. Push for user testing — awaiting approval to push.

## Notes

- Tier 1 part first: expiry is the one granting access it should refuse.
- Do not add `LinkFormat` (#286 option 2). #286 was closed by #302.
