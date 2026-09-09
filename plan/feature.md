# Feature: throttle repeated failed invitation resolves

**Issue:** [Tharga/Team#256](https://github.com/Tharga/Team/issues/256)
**Type:** feat (additive; new options, no API removed)
**Target release:** 3.21 — no bump; 3.21 is still unpublished
**New published packages:** none

## Goal

Guessing invite codes is slow, and an attempt at it is visible in the audit log rather than silent.

## What the code said that the issue did not

Everything the issue claims is accurate — the oracle is deliberate, `AuditEventType.RateLimit` is declared
and raised by nothing, and there is no HTTP endpoint to attach middleware to. Two things changed the shape
of the work:

**Both suggested homes for shared state are ruled out, one by the codebase's own argument.**
`ISupportEventLedger`'s remarks already settled it: *"`ITeamCache` itself is deliberately not reused — it is
purpose-built for three named claims lookups and has no general key/value surface, so putting an event id
through it would be an abuse of a security-sensitive cache rather than a fit."* And adding members to
`ITeamCache` would break every host that implemented it — which multi-instance consumers were explicitly
told to do. `ISupportEventLedger` itself records an event **once**; it has no notion of counting within a
window. So cross-instance state means a **third host-implemented port**.

**The key the design rests on is not available where the call happens.** Nothing in the toolkit reads a
client address anywhere, and a Blazor Server circuit has no supported way to obtain one after connecting —
`CircuitPrincipal` recovers a *principal* from state the circuit already holds, which an IP address is not.
The only production call site is `TeamInviteView.razor:63`, over the circuit.

## Scope — delay and audit, no new port (decided with the user)

- **Count failures only**, per source, in a sliding window. A resolve that finds an invitation is somebody
  holding a real link.
- **Delay past a threshold**, on a doubling curve from 250 ms, capped. Never refuse.
- **Audit the first crossing** as `AuditEventType.RateLimit`, giving that event type its first producer.
- **Per process**, deliberately. An attacker across N instances gets N budgets; every connection they hold
  is still slowed, and being wrong about a count can never lock anybody out — which a refusal budget could.
- **Address when available, one shared bucket otherwise.** `IHttpContextAccessor` answers during a request,
  including the SSR pass that precedes an interactive circuit, and not afterwards.

Settings live on the existing `InvitationOptions`, which already governs invitation policy and sits in the
core package for the reason given there: this decides whether a caller may join a team.

**Generous rather than off by default** — five failures in five minutes, then a curve capped at two seconds.
A real invitee fails once or twice and never reaches five, so the default costs nothing a person notices,
while off-by-default would mean nobody gets it.

## Out of scope

- **Cross-instance counting.** A third port is a new obligation on every multi-instance host, for a threat
  the issue itself rates *Nice* and nobody has reported. The limitation is documented on the type rather
  than left to be discovered.
- **Refusal.** It needs a key that can be trusted, which needs the address, which needs host plumbing.
- **Reducing token length on the strength of this.** Explicitly not a substitute for entropy.

## Acceptance criteria

1. Failures up to the threshold are not delayed; past it they are, growing and capped.
2. A successful resolve is never counted or delayed.
3. A failed resolve still returns null, indistinguishably — the throttle must not become a second oracle.
4. Crossing the threshold writes exactly **one** `RateLimit` entry per run, and that entry carries no
   invite code.
5. The decorator is actually applied to the registered service, asserted by resolution rather than by the
   presence of a descriptor.
6. A host that configured nothing still works — no required registration the feature added.
7. Full suite green.

## Done condition

All criteria met, both doc surfaces updated, #256 answered with what shipped **and with the two constraints
that reshaped it**, and the user has confirmed.
