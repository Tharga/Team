# Feature: audit interactive sign-in and first-sign-in user creation

## Goal

Raise the two events Tharga/Team#142 names first — "user logs on" and "user created" — so they can be routed
to Slack, and so the audit log records that someone arrived rather than only what they did.

## Why they were missing

Every other audited action passes through a service the auditing decorators wrap. These two do not:

- **A sign-in** completes inside the authentication handshake, not in any service call.
- **A first-sign-in user record** is created while *resolving the caller*, before any service is invoked.

`Tharga.Team.Support`'s own README recorded the consequence: *"Not yet: user sign-in and user creation.
Neither is an audited event today."* Two of the three events #142 asked for could not be routed, for three
products, and the Slack package could not fix it — the gap was upstream of it.

It is also an audit gap in its own right: **interactive sign-ins were not recorded at all**, so the trail
could not answer "when did this person last sign in".

## Design

**Sign-in — OIDC `OnTokenValidated`.** Fires once per interactive sign-in, unlike the claims transformation
which runs on every authenticating request. Post-configured and chained, so a host's own handler still runs.

**User creation — an event, not a constructor dependency.** The creation happens in `Tharga.Team.MongoDB`,
which cannot see the audit types; they live in `Tharga.Team.Service`. An optional constructor parameter would
also be one more thing a host's service must remember to forward — the hazard `TeamCacheWiringCheck` exists
to catch. `UserServiceBase` raises `UserCreatedEvent`; the registration subscribes per scope and audits.

**Raised only by the caller that actually created the record.** `UserServiceRepositoryBase` catches a
duplicate key and re-reads the winner (the #65 race fix); that path must not report a creation it did not
perform, or one user yields two audit entries.

**Neither can fail the operation it observes.** Both are wrapped and logged as a warning — the rule
`Tharga.Team.Support` already states for Slack, and `UserServiceBase` already applies to activity stamping.

## Entry shapes

| | `auth:signin` | `auth:user-created` |
|---|---|---|
| Event type | `AuthSuccess` | `DataChange` |
| Caller | User / Web | User / Web — the new user is the actor |
| Team | none — sign-in precedes team selection | none |
| Metadata | — | `user.key`, `user.email` |

Two entries rather than one, so a reader can see that a person arrived *and* that a record appeared.

## Acceptance criteria

- [x] Both entries carry the right event type, feature, action, caller type and source.
- [x] Sign-in names no team, and survives a principal with nothing identifiable, or none at all.
- [x] Creation attributes the new user as the actor and carries the user metadata.
- [x] The duplicate-key race path does not raise a creation.
- [x] Full suite green, no new warnings.
- [x] `Tharga.Team.Support`'s README updated — it documented both events as unavailable.

## Known test gaps, stated rather than left

- **The OIDC handler is not integration-tested.** Exercising it needs a full authentication pipeline; the
  entry it produces is tested directly instead.
- ~~The race path is enforced by placement, not by a test.~~ **Now tested** — `UserCreatedEventTests`
  covers first sign-in, a returning user, **losing the insert race**, and no subscriber. Getting that test
  honest took three attempts: the first two threw a `MongoWriteException` the production filter did not match
  (it guards on `WriteError?.Category == DuplicateKey`), so the exception propagated and the test was
  exercising the wrong path while appearing to fail for the right reason.

## What this unblocks

`auth:signin` and `auth:user-created` become routable in `Tharga.Team.Support` with no change to that
package — two of the three events #142 named. Team creation, the third, was already routable.
