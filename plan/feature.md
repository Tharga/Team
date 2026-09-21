# Feature: Support case authorization

Closes [#291](https://github.com/Tharga/Team/issues/291) and
[#295](https://github.com/Tharga/Team/issues/295), plus one unfiled defect found while verifying them.

## Goal

Make the support module able to express the ordinary shape of a support queue: **a customer raises a case
inside their own organisation, and the product's own support staff answer it** — without putting staff in
every tenant's membership list, and without a team administrator gaining the ability to read their own
members' private conversations.

## Background

Three problems in one authorization gate, `AuthorizationSupportCaseServiceDecorator.RequireCaseAccessAsync`
(`Tharga.Team.Support/Cases/AuthorizationSupportCaseServiceDecorator.cs:201`). All three verified against
`master` at `094ead0`.

### 1. Staff cannot answer a case that belongs to a team (#291)

The check takes a system scope **only when the case has no team**. With a team it requires membership of
that team. Support staff belong to none of the customers' teams, so there is no path by which they read or
answer a case. The package therefore serves the unassigned queue and a member's own case, but not the one
in the middle — which is the ordinary one.

The three obvious ways round are all closed: raising the case unassigned hides it from its own author;
giving staff a role in each tenant does not scale and puts staff in tenant membership lists; access
simulation is a deliberate *reduction* of access and would misattribute the reply.

### 2. Team support scopes arrive with the access level (#295)

`SupportRegistration.cs:170-173` registers `support:read` and `support:manage` at
`AccessLevel.Administrator`, so every Owner and Administrator of every tenant holds them by construction
and a host cannot decline — the registration is inside the package and a second registration of the same
name throws.

The scope's own description says why that is the wrong default: *"A case holds whatever a user typed into
it."* For a host whose support conversations are personal, a member's manager reading them is a different
product.

`ScopeRegistry.RegisterGrantOnly` already implements exactly the behaviour wanted — no access level grants
the scope, it is held only through a code-registered role or an explicit override, and its own XML doc
names this as its purpose. The module simply never offers it.

### 3. `support:read` authorizes writing (unfiled)

`RequireCaseAccessAsync` is the single gate for both reads and writes, and its team branch accepts
`SupportScopes.Read` for either:

```csharp
if (await authorizer.HasTeamScopeAsync(SupportScopes.Manage, teamKey)) return;
if (await authorizer.HasTeamScopeAsync(SupportScopes.Read, teamKey)) return;
```

So a member holding only `support:read` can answer, hand over, reply to, close and reopen. The teamless
branch already distinguishes the two — the read verbs pass `SystemSupportScopes.Read` explicitly — so this
is an oversight in the team path rather than a decision. `SupportScopes.Manage`'s own summary says "Reply
to and close any case in the team", which is the behaviour being restored.

## Scope

**In**

- A system scope pair satisfying the case check regardless of team: `support:all:read` / `support:all:manage`.
- A cross-team listing companion so a queue view has something to enumerate.
- An option letting a host take the team support scopes off the access level.
- Splitting the team-scope check into read and write.
- Tests, XML docs, `Tharga.Team.Support/README.md`, `docs/articles/support-cases.md`.

**Out**

- `ScopeRegistry.Replace` / a general "re-level or suppress a library scope" API. That is the PlutusWave
  request of 2026-09-20 in `Requests.md`, and it stays open — `AddThargaScopes` mutates the registry
  eagerly in call order, so a general override needs deferred application to stop being an ordering trap.
  Decided with the user 2026-09-21.
- #293's cross-team escalation queries. Adjacent and from the same reporter, but a separate ask.
- Any change to `support:unassigned:*`. Holding a scope for one queue must not confer sight of another —
  the reasoning already written into `SystemSupportScopes`.

## Design

### The new scopes

Added to `SystemSupportScopes` (in `Tharga.Team`) rather than a new class, so support's system scopes stay
in one place. The class summary currently says "for support cases that belong to no team" and must be
corrected — it will no longer be true of the whole class.

| Scope | Grants |
|---|---|
| `support:all:read` | Read and list cases in **any** team |
| `support:all:manage` | Reply to, close, reopen and hand over in **any** team |

Kept separate from `support:unassigned:*` rather than widening it, for the reason that class already
records: this is the strongest grant in the package, so it should be nameable and grantable on its own.

### The gate

`RequireCaseAccessAsync` gains a read/write distinction and tries the cross-team scopes before falling
through to membership:

1. `support:all:manage` — satisfies any verb.
2. `support:all:read` — satisfies a read verb.
3. No team → the existing system-scope branch, unchanged.
4. Membership, then `support:manage` for any verb, `support:read` for a read verb only.
5. Authorship.

Steps 1 and 2 come before membership deliberately: a staff holder is not a member and never will be.

### The option

`SupportCaseOptions.TeamScopeAccessLevel`, an `AccessLevel?` defaulting to `AccessLevel.Administrator`.
Set it to `null` and `SupportRegistration` calls `RegisterGrantOnly` instead of `Register`. The default
keeps every existing host behaving exactly as it does today.

## Acceptance criteria

1. A caller holding `support:all:manage` and no membership can read, reply to, close and reopen a case in
   any team; the audit entry attributes the reply to that caller.
2. A caller holding `support:all:read` and no membership can read and list but is refused every write verb.
3. A cross-team list exists for the `support:all:read` holder and is refused without it.
3b. A `support:all:read` holder can list **one team's** cases and count those awaiting support, without
   membership. Added 2026-09-21 during step 4: the scope is registered as granting "read *and list*
   support cases in any team", so a listing that refused it would contradict the catalogue entry a host
   reads before granting it.
4. Holding `support:read` on a team no longer authorizes answer, hand over, reply, close or reopen; it
   still authorizes read, list and mark-read.
5. `support:unassigned:*` grants nothing in a team, and `support:all:*` grants nothing over the unassigned
   queue.
6. With `TeamScopeAccessLevel` unset, an Administrator holds `support:read` and `support:manage` exactly as
   today.
7. With `TeamScopeAccessLevel = null`, no access level grants either scope, a custom role naming them is
   rejected, the override pickers do not offer them, and a member holding the role still gets the scopes.
8. An author still reads and replies to their own case under every configuration above.
9. A store written before this feature still compiles and returns an empty page from the new cross-team
   read.
10. Full suite green; `docs/articles/support-cases.md` and `Tharga.Team.Support/README.md` describe the new
    scopes and the option.

## Done condition

All ten criteria met, `MAJOR_MINOR` moved to `3.22`, both GitHub issues commented with what shipped and
closed, and the PR open against `master`.

## Version

`MAJOR_MINOR` in `.github/workflows/build.yml` goes `3.21` → `3.22`.

The cross-team scopes and the option are purely additive and would be a patch on their own. The read/write
split is not: a host that granted only `support:read` to someone who has been replying must now grant
`support:manage` for them to keep working. That is "a new grant required for an existing capability to keep
working", which is the bump test.
