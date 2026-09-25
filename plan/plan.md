# Plan: invitation seams on a custom store

Spec: `plan/feature.md`. Branched from `origin/master` at `bc4346f`.

- [x] 0. `chore(deps): nuget update` — Moq 4.20.72 → 4.21.0 (test project only). Full suite 3,028 green.
- [ ] 1. **Expiry — tests first.** A `TeamServiceBase` derivative that does *not* override
      `GetInvitationInternalAsync`, lifetime set: expired accept refused; unexpired accept succeeds; decline of an
      expired invitation succeeds; accept of an unknown code refused. Watch them go red.
- [ ] 2. **Expiry — the fix.** Roster-reading default for `GetInvitationInternalAsync` and
      `GetInvitedMemberNameAsync`; fail closed on accept when a lifetime is set and nothing matches. Correct and
      relocate the orphaned doc; remove the `InvitationExpiryWiringCheck` reference.
- [ ] 3. **Short links — startup rule.** `TeamServiceCompleteness` gains `GetTeamKeyByInviteKeyInternalAsync`,
      always reachable. Extend `TeamServiceCompletenessTests` and `TeamsReadCompletenessCheckTests` (or a sibling)
      so the built-in store and an overriding host stay silent. Rewrite the member's remarks.
- [ ] 4. **Repository layer.** `ITeamRepository.SetInvitationExpiryAsync` default throws; a startup check in
      `Tharga.Team.MongoDB` reports a registered repository implementing neither `GetByInviteKeyAsync` nor
      `SetInvitationExpiryAsync` (interface map: an unimplemented default member maps to the interface itself).
      Built-in `TeamRepository<,>` must report nothing.
- [ ] 5. **Docs.** `Tharga.Team/README.md` contract table rows for the invitation members; implementation guide
      *What an invitation link looks like* and *Invitations that expire* custom-store notes.
- [ ] 6. Full suite; sample boots clean (no false positive on the built-in store).
- [ ] 7. Push for user testing.

## Notes

- Tier 1 part first: expiry is the one granting access it should refuse.
- The existing check-in-the-team-service pattern from #301 (`TeamServiceCompleteness` + `Report`) is where step 3
  goes — one more rule, not a new check.
- Do not add `LinkFormat` (#286 option 2); answer it on the issue at close.
