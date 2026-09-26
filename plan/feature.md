# Feature: one outbound-mail policy — non-production mail reaches only the override address or allowed domains

[Tharga/Team#290](https://github.com/Tharga/Team/issues/290): `SmtpTeamEmailSender` mails real people from a test
environment, with no way to stop it. Tier 2 under `mission.md` (filed by a consumer; a host on the built-in
sender cannot work around it).

Branched from `origin/master` at `b4d5a20` (PR #304 merged). `dotnet outdated`: nothing to update.

## Decisions (user, 2026-09-26)

1. **All mail behaves the same.** One policy, applied to every mail the toolkit sends: invitations
   (`SmtpTeamEmailSender`, Tharga.Team.Service) and support mail (`SupportMailClient`, Tharga.Team.Support).
2. **Outside production, mail goes only to the override address, or to recipients in an allowed domain.** Fail
   closed: outside production with nothing configured, nothing is sent.

## The policy

Decided per mail, from `IHostEnvironment` and one options object:

| Environment | Recipient | Outcome |
|---|---|---|
| Production | anyone | **Deliver** unchanged |
| Not production | domain in `AllowedDomains` | **Deliver** unchanged |
| Not production | any other, `Address` set | **Redirect** to `Address`; subject prefixed `[{Environment} -> {original recipient}]` |
| Not production | any other, `Address` unset | **Withhold** — not sent, logged |

- **Production means `IHostEnvironment.IsProduction()`**, so a host gets the safe behaviour by doing nothing.
  No `IHostEnvironment` registered counts as *not* production: fail closed.
- **An unexpanded pipeline variable counts as unset.** A value shaped `$(Name)` is treated as missing, not as an
  address or a domain.
- **`AllowedDomains` is a list** — as many domains as wanted. Each matches exactly and case-insensitively on
  the part after `@`: `eplicta.se` does not admit `mail.eplicta.se`; list it too if wanted. Stated in the docs.
- Every send has one recipient today (an invitation, a case reply), so the "mixed recipients" case in the issue
  does not arise. The email board (plan 14) will send more mail through the same policy; a mail with several
  recipients would be redirected whole, as the issue recommends.

## Placement

**`Tharga.Team.Service`, namespace `Tharga.Team.Service.Email`.** Both senders reach it (`Tharga.Team.Support`
already references `Tharga.Team.Service`), and it adds no dependency to `Tharga.Team`. It is domain logic — a rule
about who may be mailed — so it sits in the Domain-role package; justified today by two existing senders, not by
the target architecture. Neutral to v4.

## Configuration

One options type, `OutboundMailPolicyOptions { Address, AllowedDomains }`, registered once with `TryAdd` by
whichever of the two registrations runs first:

- Bound from configuration section **`Email:Override`** (`Email:Override:Address`,
  `Email:Override:AllowedDomains`), as the issue suggests, when an `IConfiguration` is available.
- Or in code, `services.Configure<OutboundMailPolicyOptions>(…)`.

## Behaviour at each sender

- **Invitations:** redirected or withheld before the SMTP client is built. Withheld logs a warning naming the
  setting to change; the invitation itself is still created, so its link can be copied from the UI.
- **Support mail:** withheld returns a failed `MailSendResult` ("withheld by the non-production mail policy"), so
  the case records the reply as not delivered rather than pretending it went out.
- **At startup, outside production,** one log line states the policy in force — redirect to where, which domains
  pass — and a warning when everything is being withheld.

## Version

**No `MAJOR_MINOR` bump — decided 2026-09-26 (user).** A host whose test or staging environment sends mail today
stops sending after upgrading, until it sets `Email:Override`. That goes at the top of the PR description, which
is the release note.

## Custom senders — decided 2026-09-26 (user)

The policy is a public, injectable service, **`IOutboundMailPolicy`**, registered whatever sender the host uses —
including when `AddEmailService<T>()` registers a custom `ITeamEmailSender`. A custom sender opts in by injecting
it and applying its decision; the toolkit does not force it on code it does not own. The decision is a record
(action, recipient, subject) so a sender applies it without re-implementing any rule.

## Not in scope

- #289 (team key on `ITeamEmailSender`) — separate interface decision.
- Everything in plan 14 (email board): categories, consent, mass send, test send to self.

## Acceptance criteria

- [ ] The four outcomes in the table, each covered by a test on the policy alone.
- [ ] `$(Name)` treated as unset for both the address and a domain.
- [ ] No `IHostEnvironment` behaves as non-production.
- [ ] `SmtpTeamEmailSender` applies the policy: redirect changes recipient and subject; withhold sends nothing.
- [ ] `SupportMailClient` applies the policy: redirect changes recipient and subject; withhold returns a failed result.
- [ ] Options bind from `Email:Override`; registering both modules registers the policy once.
- [ ] Startup log line outside production; warning when withholding everything.
- [ ] `IOutboundMailPolicy` is resolvable when a custom `ITeamEmailSender` is registered, and when only the support
      module is.
- [ ] Docs: README (Service and Support), implementation guide.
- [ ] Full suite green.

## Done condition

Criteria met; #290 commented (what shipped, how to configure) and closed; `plan/` removed in the close-out commit;
PR open.
