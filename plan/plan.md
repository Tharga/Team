# Plan: make an unset access level visible (interim mitigation)

Feature scope: `plan/feature.md`. Conventional-commit prefix for this branch: **`feat:`**.

## Steps

- [ ] **0. Package updates — HELD.** Only the xunit 4.0 / Microsoft.Testing.Platform pair, per the standing
      decision. Re-checked at branch time; unchanged.

- [x] **1. Confirm the read surface — DONE 2026-08-24. Better than assumed: a cheap count is available.**
      Established by compiling a throwaway probe against `Tharga.MongoDB` 2.15.1 (the XML docs are
      incomplete and PowerShell 5.1 cannot load a net10 assembly, so the compiler was the only authority).
      Probe removed.
      - **`CountAsync(FilterDefinition<TTeamEntity>)` compiles.**
      - **`GetAsync(FilterDefinition<TTeamEntity>)` compiles.**
      - `GetProjectionAsync<T>` exists but takes `Expression<Func<T, bool>>`, **not** a `FilterDefinition`.

      **The projection route is unusable, and the reason matters:** a LINQ expression cannot express field
      *absence*. `$exists: false` has no `Expression<Func<T,bool>>` equivalent, so any typed-predicate API is
      incapable of finding this condition however convenient its signature looks. `FilterDefinition` is the
      only route, and it forgoes projection.

      **Resulting shape, which is cheaper than the plan assumed.** The clean case costs one count:
      1. `CountAsync(filter)` always — one cheap query; if `0`, log nothing and stop.
      2. Only when non-zero, `GetAsync(filter)` and take the first N (10) keys to name in the message,
         reporting the true total from the count.

      So a healthy host pays a single count at startup and never materialises a team document, and an
      affected host materialises at most ten — the cap the plan asked for, now needed only on the unhappy
      path.

- [~] **2. Write the failing tests first, and build the fixture as raw BSON.**
      This is the step most likely to be got wrong: a test that constructs `new TestMember { … }` without
      setting `AccessLevel` produces an entity whose property reads `Owner`, and **serialising that writes
      the field**, so it would not reproduce the condition at all. The fixture has to be a `BsonDocument`
      with the field genuinely absent, as `AccessLevelDefaultTests.AStoredMemberMissingTheField_…` already
      does.
      Cases:
      - a member with no `AccessLevel` field → detected;
      - a member stored as `"Owner"` → **not** detected (the discrimination that makes this worth shipping);
      - a mix in one team → detected once, team named;
      - no affected data → silent, no warning;
      - empty or absent collection → silent, no throw.

- [ ] **3. The check itself, in `Tharga.Team.MongoDB`.**
      An `IHostedService` mirroring `UserServiceCompletenessCheck` / `TeamServiceCompletenessCheck`. Filter:
      `ElemMatch(x => x.Members, Exists(m => m.AccessLevel, false))`.
      **Log a warning; never throw, on any path** — wrap the query itself, because a store that is
      unreachable at startup must not take the host down for a diagnostic. The message must state that the
      affected members are being **treated as Owner** today, name the team keys (capped, with the total), and
      point at the documented remedy.

- [ ] **4. The off switch.**
      An option on the Team/Mongo options surface, defaulting to **on**. Rationale to record in the XML docs:
      it is one query at startup and the finding is a silent privilege grant, so silence should be opt-in
      rather than the default. Mirror how `ThrowOnIncompleteUserService` is documented.

- [x] **5. The non-behaviour-change claim — asserted at the data layer, and here is what that does and does
      not cover.**
      A before/after comparison of effective scopes is not constructible: "before" and "after" differ by no
      code on the resolution path at all, so any such test would be comparing an expression with itself and
      would pass no matter what this feature did. Writing one would look like evidence and be none.
      What is asserted instead, and is meaningful:
      - `AMemberDocumentWrittenBeforeTheFieldExisted_LacksIt` asserts the deserialized level is **still
        `Owner`** — the behaviour this feature deliberately preserves rather than changes.
      - `OnceDeserialized_AMissingLevelIsIndistinguishableFromAStoredOwner` pins the property that makes it
        so.
      What carries the rest of the claim is structural, and is stated here rather than dressed up as a test:
      the feature adds one `IHostedService` that performs two reads and a log, and one filter. It registers
      nothing into the authorization path, writes nothing, and no existing type's behaviour was edited —
      `ThargaTeamOptions` and `ThargaTeamRegistration` gained a flag and a registration, and nothing else was
      touched. The full-suite run in step 6 is the real backstop: 2110 pre-existing tests, many of them over
      scope resolution, all still green.

- [ ] **6. Full test suite.** `dotnet test -c Release` across the solution. Green before any commit.

- [ ] **7. Docs.**
      - `implementation-guide.md` — the check, the option, and the **back-fill query** a host runs to find
        and fix affected members. Give the query in a form that can be pasted into `mongosh`.
      - State plainly that **4.0 will deny these members rather than granting them Owner**, so fixing the
        data now is migration work, not optional hygiene.
      - Separate `docs:` commit before close-out.

- [ ] **8. Close the records.**
      - Backlog `Toolkit/Team.md` — note under the `default(AccessLevel)` entry that detection shipped in
        3.14.x and that the entry itself stays open until plan 05 item 7 lands.
      - `plans/…/planned/05-v4-release.md` item 7 — note that the back-fill query now exists and where, so
        the v4 migration notes can reference it instead of re-deriving it.
      - `Requests.md` — no entry for this; nothing to update. Do not invent one.
      - No GitHub issue exists for this either; it came from the backlog. Do not open one to close it.

- [ ] **9. Close out.**
      Archive `feature.md` to `$DOC_ROOT/…/done/`, `git rm -r plan`, final commit
      `feat: make an unset access level visible complete`, push, PR. Do not merge locally.

## Notes and decisions

- **2026-08-24** — Branch created from `master` at `8d5585d` (post-#238 merge). Tree clean.
- **2026-08-24** — Chosen over starting v4 or deepening the design only. The deciding factor: this is the
  only option that reduces a live Critical exposure on a version consumers actually run, and its main
  artifact — a way to find affected documents — is needed for the 4.0 migration regardless, so it is not
  throwaway work.
- **2026-08-24** — Established before planning: the check **cannot** be written in C# against
  `ITeamMember.AccessLevel`, because a missing field and a stored `Owner` deserialize to the same value.
  Detection must be on field presence at the storage layer, which is what puts this in `Tharga.Team.MongoDB`.
- **2026-08-24** — Members are an embedded array on the team document, so one `ElemMatch` query covers every
  member without a second collection read.

## Last session

Branch created, `feature.md` and `plan.md` written, awaiting plan confirmation. No code changed yet.
