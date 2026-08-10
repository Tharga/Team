# Feature: close #204 — the last 153 literal strings on the surfaces it names

## Goal

Finish [Tharga/Team#204](https://github.com/Tharga/Team/issues/204). Route every remaining user-facing
string on the four surfaces the issue names through `IThargaTextProvider`, so a host can call the tenant an
Organisation, ship Swedish, or both — without the toolkit's own components contradicting them.

Filed by **Eplicta FortDocs** (FD-14). Their product says *Organisation* in 83 strings across 35 files, and
these components say *Team* beside them, which reads as an unfinished rename rather than a boundary.

## What remains — the third and final increment

Two increments have shipped (3.10.5 and 3.10.9), migrating twelve components. **153 strings across six**
remain on the surfaces #204 names:

| Component | Strings |
|---|---|
| `TeamComponent` | 60 |
| `UsersListView` | 50 |
| `TeamsListView` | 30 |
| `DeleteUserDialog` | 6 |
| `InviteUserDialog` | 4 |
| `TeamInviteView` | 3 |

A further **135** sit on API-key, scope and simulation surfaces. **Those are out of scope** — #204 does not
name them, and finishing them would double the work without closing anything.

## Decisions taken up front (user, 2026-08-10)

- **Plurals get two keys per message**, each holding a whole sentence — the precedent set in 3.10.9 for
  runtime-composed sentences. No change to `IThargaTextProvider`.
  - **The limitation, stated so it is a decision and not an accident:** a language with more than two
    plural categories (Polish, Russian, Arabic) cannot be expressed. English and Swedish, the actual
    consumers, both have two. Revisiting means an additive overload taking a count, which stays possible.
- **All six components**, closing the issue rather than leaving a fourth increment.

## Why this is not a mechanical find-and-replace

Three of the six compose sentences at runtime from a fixed head and a chosen tail — `TeamComponent` (3
sites), `DeleteUserDialog` (3), `TeamsListView` (1). That reads correctly in English and **cannot be
translated**: the clauses reorder in other languages, and in several the tail changes agreement in the
head. `DeleteUserDialog` stacks plural agreement on top — *"a team"* vs *"{n} teams"*, *"it"* vs *"them"*,
*"this team is"* vs *"these teams are"*, interleaved in one paragraph.

Each variant becomes one key holding a whole sentence, as 3.10.9 did for the two dialogs with the same
shape.

## Scope

- New text catalogues for the six components, named and placed like the existing ones in `Framework/`.
- Every literal routed through a resolved `TextSet`, one pass per component.
- Each component moves from `TextCoverageTests.Pending` to `Migrated`, so it can never regress.
- The published coverage table in `docs/articles/implementation-guide.md` updated — the build fails if it
  drifts.

## Out of scope

- The 135 strings on API-key, scope and simulation surfaces.
- Changing any wording. A string's English default is exactly what it renders today; this is a
  localizability change, not a copy edit.
- Changing `IThargaTextProvider`.

## Acceptance criteria

- [ ] All six components report zero literals and sit in `TextCoverageTests.Migrated`.
- [ ] Every new key appears in `ThargaTextKeys.All`, which is what a consumer generates a table from.
- [ ] No English default differs from the string rendered today.
- [ ] The plural and composed sentences are whole-sentence keys — no key is a fragment concatenated at
      runtime.
- [ ] The published coverage table matches reality; remaining count reads **135**, all out of scope.
- [ ] Build clean, full test suite green.

## Done condition

#204 closed with what shipped and what FortDocs can now override, and the user confirms.
