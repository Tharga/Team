# Feature: short opaque invitation tokens, and invitations that expire

**Issue:** [Tharga/Team#249](https://github.com/Tharga/Team/issues/249)
**Branch:** `feature/short-invite-token` (from `master`)
**Target release:** 3.20 — new public API and a behaviour change
**New published packages:** none

## Goal

Two asks about one record:

1. An invitation link that is **short and opaque**, and does not carry the team key in plain sight.
2. A **configurable lifetime**, enforced at acceptance, that can be **extended without minting a new code**.

## What the research changed about the shape of this

**The contract already takes an opaque code.** `ITeamInvitationService.GetInvitationAsync(string inviteCode)`
accepts a code and *returns* `TeamKey` on the `TeamInvitation` it resolves to. So the public surface needs no
change at all, and the acceptance screen never needed the team key from the URL — it gets it from the resolve.

The team key is in the link for exactly one reason: `InviteKey` lives inside the team document on
`TeamMemberBase.Invitation`, so without the team key there is nothing to look the code up *by*. This is a
storage-reachability problem wearing a link-format costume.

**Indexing into the members array is already an established pattern here**, which removes the main unknown:
`TeamRepositoryCollection.Indices` already defines a multikey index on `"Members.Key"`, by string path.

## Scope

- **A new semantic method on the storage seam** — resolve a team by an outstanding invite key. `TeamServiceBase`
  is where the seam lives (architecture-v4 names its abstract methods as the thing a future `Team.Sql` would
  implement), so this belongs there.
- **A short opaque token** as the new `InviteKey` format: 9 cryptographically random bytes, base64url, 12
  characters. The token *is* the bearer credential, so it is generated with `RandomNumberGenerator`, never
  `Guid.NewGuid()` — which carries 122 bits and promises uniqueness rather than unpredictability.
- **A shorter query parameter**, `tic`, replacing `TeamInviteCode`.
- **`Invitation.ExpiresAt`**, optional, and an `InvitationOptions.Lifetime` to default it from.
- **An extend operation** that moves the expiry on the existing record, keeping the code.
- **Enforcement at acceptance**, reporting expiry as *expired* rather than as an invalid code.

## Explicitly not in scope

- **A separate invitation collection.** It would make the token addressable in its own right and make
  uniqueness structural — but it is a second store, a migration, and a new port, for a problem 72 bits of
  entropy already answers. See the uniqueness decision below.
- **Rotating or revoking a token** beyond what removing the member already does.
- **Anything about who may extend** beyond the scope that already authorizes inviting (`member:manage`).

## Decisions taken from the code, with reasons

**1. The seam method is `virtual`, not `abstract`.** `TeamServiceBase` has 17 abstract members and a host
implements them. Adding an abstract one is a compile break for every derived service. The codebase already
answers this — `GetAllTeamsInternalAsync`, `SoftDeleteTeamAsync` and `SetTeamMemberSuspendedAsync` are all
`virtual` with safe defaults precisely so existing hosts keep compiling. The default returns null, meaning
"this store cannot resolve a bare token", which degrades to the old link format rather than failing.

**2. The index is NOT unique, and this is the subtle one.** A unique multikey index on
`Members.Invitation.InviteKey` looks right and would break team creation. Most members have no invitation, so
their array entries index as null; uniqueness is enforced *across* documents, so the second team containing a
non-invited member collides with the first. `partialFilterExpression` does not save it — the filter is
evaluated per document, not per array element, so a team with both invited and non-invited members still
indexes the nulls. So: a plain index for the lookup, uniqueness from 72 bits of entropy, and **a resolve that
returns null if it somehow matches more than one team** — ambiguity refuses rather than guesses.

**3. `ExpiresAt` is optional, and it has to be.** `Invitation`'s three members are all `required`, and hosts
construct one — the reporter's own `AppTeamService.CreateTeamMember` override does. A new `required` member is
a source break for them. Optional with a null default: null means "fall back to the configured lifetime
measured from `InviteTime`", set means an explicit expiry that extension moves.

**4. Extending is an operation, not a property set** — architecture-v4 rule 1, which names `InviteMember` as
its example. `ExtendInvitationAsync` can be authorized and audited as one fact; exposing a settable expiry
could not.

**5. Old links must keep resolving.** Invitations already mailed carry base64 `{TeamKey, Code}` with GUID
codes. `GetInvitationAsync` tries the old format first and falls back to treating the code as a bare token, so
shipping this does not invalidate every outstanding invitation. The old format stays supported; only newly
minted links are short.

## Acceptance criteria

1. A newly created invitation produces a link of the form `?tic=<12 chars>` containing no team key.
2. That token resolves to the correct team through `GetInvitationAsync` with no team key supplied.
3. A token that matches no team, is malformed, or matches more than one, all return null — indistinguishably.
4. **An invitation link created before this change still resolves**, unchanged.
5. Tokens are generated from a cryptographic source, asserted rather than assumed.
6. With no `Lifetime` configured, invitations do not expire — existing behaviour is the default.
7. With a `Lifetime` configured, an invitation older than it is refused at acceptance **and reports that it
   expired**, distinctly from an invalid code.
8. Extending an invitation moves its expiry and **leaves `InviteKey` unchanged**, so a link already mailed
   keeps working.
9. Re-inviting an address that already has an outstanding invitation extends it rather than replacing it.
10. A host whose store does not implement the new seam method keeps working on the old link format.

## Done condition

All ten met, `docs/` and `README.md` reviewed, #249 commented and closed with evidence, records swept.

## Token length — asked, argued, decided (2026-09-05)

**Six characters was the ask. It was rejected on the numbers, and the numbers are worth keeping.**

The realistic attack is online guessing against the *whole pool* of outstanding invitations rather than one
code: any hit joins a team, and the resolve names the team on a hit, so a success announces itself. At 36 bits
with a thousand live invitations, a distributed guess reaches even odds in **under an hour** at a hundred
thousand sources; even a single source at 1000 requests a second gets there in about thirteen hours. The pool
also only grows, because invitations do not expire by default.

**Rate limiting was considered as a substitute and does not work as one.** There is no HTTP endpoint to limit
— `TeamInviteView` resolves in-process over the Blazor circuit — so it would have to be a service-level
throttle with shared cross-instance state. Even then a per-source limit is strong against one attacker and
close to worthless against a distributed one: the same 36-bit attack still succeeds in 48 minutes from a
hundred thousand addresses.

**12 characters (72 bits) was the settled answer.** The same attack takes about six million years, and the
whole link goes from 63 to 53 characters — two-thirds of the available saving, since the host and path are
most of the length. Going to six would have saved a further six characters of a 47-character link.

**A service-level throttle is still worth building, as defence in depth rather than instead of entropy** —
it would also slow enumeration of the GUID codes still in circulation, and `AuditEventType.RateLimit` already
exists for it. Not in this feature.

## The thing most likely to go wrong

**Criterion 3's "matches more than one" and criterion 10's fallback are both paths that are easy to write and
easy to never execute.** Both need a test that actually drives them, or they are decoration. The uniqueness
reasoning in decision 2 is also the kind that is convincing on paper and wrong in practice — it should be
verified against a real MongoDB index rather than argued, because if it is wrong the failure is at team
creation, in production, for everyone.
