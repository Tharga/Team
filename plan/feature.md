# Feature: make an unset access level visible (interim mitigation)

**Type:** feat (additive, non-breaking)
**Branch:** `feature/access-level-completeness-check`
**Target release:** 3.14.x
**Relates to:** backlog *"`default(AccessLevel)` is `Owner`"*; plan 05 item 7 is the real fix

## Goal

A host can find out, on the version it actually runs, whether any of its stored team members carry no
access level and are therefore being treated as **Owner**. Today that condition is completely silent.

## Why this exists, and what it is not

`AccessLevel` is declared `Owner, Administrator, User, Viewer, Custom`, so the zero value — a stored document
with no such field, or a `CreateTeamMember` override that forgot to copy it — is `Owner`. It fails open with
no error and no log.

**The real fix is plan 05 item 7** (`AccessLevel?`, where absence is preserved as null and grants nothing),
decided 2026-08-24 and queued for 4.0 because it is breaking. **This is not that fix and must not pretend to
be.** It changes no behaviour at all: an affected member keeps being treated as Owner. It only makes the
condition *visible*, so a host can correct its data before 4.0 starts denying those members.

The backlog rejected "boundary defence" as *the* fix, and that judgement stands — a boundary check cannot
distinguish "unset" from "deliberately Owner" for a non-nullable enum. **This is a different proposition:**
detection at the storage layer, where the two *are* distinguishable, shipped alongside a queued real fix
rather than instead of one.

## The constraint that determines the whole design

**This cannot be a C# check.** Once a document is deserialized, a missing `AccessLevel` field and a stored
`Owner` are the same value — that is the defect itself. Any check that inspects `ITeamMember.AccessLevel`
would report every genuine Owner as suspect and every legacy member as fine.

Detection therefore has to happen at the storage layer, on field presence:

```csharp
Builders<TTeamEntity>.Filter.ElemMatch(
    x => x.Members,
    Builders<TMember>.Filter.Exists(m => m.AccessLevel, false))
```

which puts this in **`Tharga.Team.MongoDB`**, not in `Tharga.Team` or `.Service`. That is also why it can
only ever be a Mongo-side capability: a future non-Mongo store would need its own.

Members are an embedded array on the team document (`TeamEntityBase<TMember>.Members`), confirmed, so one
query over the team collection finds every affected member without touching a second collection.

## Scope

In:

- A startup check in `Tharga.Team.MongoDB`, in the shape of the existing `UserServiceCompletenessCheck` /
  `TeamServiceCompletenessCheck` (`IHostedService`), running **one** query.
- It **logs a warning and never throws.** Killing a host's startup over legacy data would be a worse outcome
  than the bug — and unlike the incomplete-service checks, nothing here is a wiring mistake the developer can
  fix in code before deploying.
- The message names the affected team keys (capped, with a total), states that those members are currently
  being **treated as Owner**, and points at the remedy.
- An option to turn it off.
- Docs: a back-fill query a host can run, plus a note that 4.0 will deny these members rather than granting
  them Owner.

Out:

- **Any behaviour change.** No member's effective access changes. If this PR alters what anyone can do, it
  is wrong.
- **Auto-repair.** Writing a level into a document the toolkit cannot interpret is a guess about
  authorization, and the safe guess (`Viewer`) may silently demote a real owner. The host decides.
- **The nullable contract change** — that is plan 05 item 7 and stays there.

## Open question for step 1

Whether `IDiskRepositoryCollection<T>` exposes a read taking a `FilterDefinition<T>` that can project just
the keys. `GetOneAsync(filter)` is used at `TeamRepository.cs:55`, so a filter-taking read exists; the exact
overload and whether a projection is available needs confirming before the check is written. If no projection
is available, fetching whole team documents at startup is acceptable only with a cap — see step 1.

## Acceptance criteria

- [ ] A team document containing a member with no `AccessLevel` field is detected — proven by a test that
      builds such a document as raw BSON, not by one that sets the property.
- [ ] A team whose members all carry a stored level produces no warning, including members stored as
      `Owner` — the check must not confuse a real Owner with an absent field.
- [ ] The warning names the affected team keys and says those members are being treated as Owner.
- [ ] The check never throws, on any input, including when the collection is empty or missing.
- [ ] The check can be turned off by a documented option.
- [ ] No behavioural change: effective scopes and access decisions are identical before and after.
- [ ] Docs carry the back-fill query and the 4.0 warning.
- [ ] Full test suite green.

## Done condition

A host upgrading to this version learns at startup whether it has affected data, and has a documented query
to fix it — before 4.0 turns those members from Owner into denied.

## Package updates — held again, same reasoning

Re-checked at branch time: `xunit.v3` 3.2.2 → 4.0.0 and `xunit.runner.visualstudio` 3.1.5 → 4.0.0 remain the
only outstanding updates, and remain the twice-backed-out Microsoft.Testing.Platform migration that fails on
the Linux runner. Held per the standing decision; the backlog says it needs its own PR.
