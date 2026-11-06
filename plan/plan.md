# Plan: granting limited to what the granter holds (#409)

Status: **decisions approved 2026-11-06 (all five recommendations).** Step 1 in progress.

## Decisions (approved: all recommendations taken)

1. **Administrators and Owners.** Exempt from the subset rule (they hold every registered scope), but a grant-only scope is held by
   no level, so an Administrator does not hold it either. Does an Administrator granting a grant-only scope as an override to a
   member or key stay allowed? Recommended: **yes for members and keys, refused only when the name is unregistered** — a grant-only
   scope exists to be handed out by override or role, and refusing it would make it ungrantable.
2. **Roles.** The issue offers "optionally no roles at all, as a host option" for a lower caller. Recommended: **not now**; the
   subset rule already limits roles, and an option is surface nobody has asked for.
3. **Opt-out.** Recommended: **always on, no switch.** A host that handed `member:manage` to a lower level and relies on raising
   others will see refusals; the release notes say so.
4. **Self-edit.** Refuse a `member:manage` holder changing their own level, roles or overrides. Recommended: **yes**, refused for
   everyone below Owner, with a message naming who may do it instead.
5. **Release.** Behavior change in a security direction: minor release, flagged in the PR notes.

## Steps

- [ ] 1. `GrantCeiling`: the caller's scopes for a team (from the principal) and the scopes a proposed grant would carry (through
      the grant resolver); the missing scopes; the exemptions (Administrator/Owner, `apikeys:oversee`).
- [ ] 2. API keys: create and set-roles / set-overrides refuse a grant above the caller's, Owner, and unregistered overrides.
- [ ] 3. API keys: a key holding more than the caller cannot be refreshed, locked, disabled, deleted or re-scoped, and its stored
      value is not shown.
- [ ] 4. Members: invite, change level, roles and overrides refuse a grant above the caller's; suspend and remove refuse a target
      above the caller; no self-edit.
- [ ] 5. UI: the level dropdown, role and override pickers offer only what the caller may grant; actions on a key or member above the
      caller are hidden or disabled.
- [ ] 6. Tests per step, including the escalation cases from the issue as failing-then-passing, and a guard that no path reaches
      the inner services without the check.
- [ ] 7. Docs: implementation guide and a release-notes style PR description.
- [ ] 8. Close-out: archive this plan, `git rm -r plan`, final commit, push and PR.

## Notes

- `TeamGrantResolver` is internal and the single copy of effective-scope resolution; the ceiling goes through it rather than calling
  `GetEffectiveScopes` again.
- The member path has no host seam today; the check sits inside `AuthorizationTeamServiceDecorator`, the one enforcement point.

## Last session

2026-11-04: read #409, #412, #405 and the code; wrote this plan. Next: decisions above.
