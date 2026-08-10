# Plan: close #204 — the last 153 strings

Branch: `feature/text-remaining-team-surfaces` (from `master`).

## Order

Smallest first, so the pattern is settled before the 60-string component, and so the two hard shapes
(composed sentences, plural agreement) are solved on a small surface rather than discovered inside a large
one.

## Steps

- [x] 1. Package updates up front. `dotnet list package --outdated` across the whole solution: nothing
      outstanding. Nothing to apply.
- [ ] 2. `TeamInviteView` (3) — smallest, no composed sentences. Establishes the catalogue shape.
- [ ] 3. `InviteUserDialog` (4).
- [ ] 4. `DeleteUserDialog` (6) — **the hard one.** Three interleaved plural ternaries in one paragraph.
      Two whole-sentence keys per message per the decision; no fragments.
- [ ] 5. `TeamsListView` (30) — one composed sentence.
- [ ] 6. `UsersListView` (50) — no composed sentences, largest mechanical one.
- [ ] 7. `TeamComponent` (60) — three composed sentences, and the component #204 was filed about.
- [ ] 8. Move all six from `Pending` to `Migrated` in `TextCoverageTests`, leaving the eight out-of-scope
      components pending at 135.
- [ ] 9. `dotnet build -c Release` + `dotnet test -c Release`, full suite.
- [ ] 10. Docs: the published coverage table in `docs/articles/implementation-guide.md`, the "fully routed"
      list, and the remaining count. Land as a `docs:` commit.
- [ ] 11. Commit, push, ask the user to verify.

## Close-out (only once the user confirms)

- [ ] 12. Re-check package updates.
- [ ] 13. Close the records: **GitHub #204** (comment naming what shipped and what FortDocs can now
      override, then close), the `Requests.md` entry and a follow-up, and `$DOC_ROOT/Eplicta/requests.md`
      — the consuming project's own file, which records this as their open ask.
- [ ] 14. Archive `feature.md` to the Plan directory `done/`, `git rm -r plan`, close-out commit, PR.

## Working rules for this migration

- **Never change an English default.** It must be byte-identical to what renders today, or this stops
  being a localizability change and becomes an unreviewed copy edit across 153 strings.
- **No key is a fragment.** A sentence assembled at runtime from two keys is untranslatable in the same
  way the ternaries are; that is the whole reason those three components were left until a decision.
- **Placeholders are positional** (`{0}`, `{1}`), because a translator reorders clauses and a name-bearing
  string must let them.
- Counts in `TextCoverageTests` may only reach zero by real migration. The ratchet already fails a build
  where a count shrinks without the published number being updated, so the docs step is not optional.

## Version

`MAJOR_MINOR` is `3.12`. New public catalogues are additive API, so a **minor bump to 3.13** at close-out.

## Last session

2026-08-10 — branch and plan created; decisions confirmed (two keys per plural message; all six
components). Step 1 done. Starting step 2.
