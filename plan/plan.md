# Plan: one outbound-mail policy (#290)

Spec: `plan/feature.md`. Branched from `origin/master` at `b4d5a20`.

- [x] 0. `dotnet outdated` — nothing to update, so no `chore(deps)` commit.
- [x] 1. **Policy — tests first.** `OutboundMailPolicyTests` (11): four outcomes, several allowed domains, `$(Name)`
      as unset, no `IHostEnvironment` as non-production, case-insensitive exact match, leading `@` tolerated.
- [x] 2. **Policy — implementation.** Public `IOutboundMailPolicy`, `OutboundMailPolicy`, `OutboundMailPolicyOptions`
      (`SectionName = "Email:Override"`), `OutboundMailDecision` / `OutboundMailAction` in `Tharga.Team.Service.Email`.
- [x] 3. **Invitations.** `SmtpTeamEmailSender` gains a `(options, policy, logger)` constructor; the old one-argument
      constructor is kept and uses an environment-less policy (withholds — documented). Message building split into
      internal `CreateMessage`. `SmtpTeamEmailSenderPolicyTests` (5).
- [x] 4. **Support mail.** **Changed from the spec:** applying the policy inside `SupportMailClient` broke
      `TransportNamespaceIsolationTests` — the transport namespace must stay ignorant of Team types. So the policy is
      a decorator, `Cases/PolicyGovernedMailClient`, registered as `ISupportMailClient` around the transport.
      Withheld → failed `MailSendResult`. `SupportMailClientPolicyTests` (5).
- [x] 5. **Registration + startup log.** `AddOutboundMailPolicy()` (idempotent: `TryAdd` / `TryAddEnumerable`), binds
      `Email:Override` from the container's `IConfiguration`; `OutboundMailPolicyReport` hosted service logs the
      policy outside production (warning when withholding everything). Called from the Team email registration — SMTP
      and custom-sender paths — and from support when mail is configured. Written alongside its tests
      (`OutboundMailPolicyRegistrationTests`, 7; 3 more in `EmailRegistrationTests`) rather than strictly first.
      Full suite 3,107 green.
- [x] 6. ~~Version bump~~ — dropped (user): patch release, behaviour change called out in the PR.
- [x] 7. **Docs.** Implementation guide: new *Mail outside production* section (table, config, upgrade warning, custom-sender example) and the stale "invitations are the only mail" sentence corrected. Service and Support READMEs: short sections pointing at it.
- [x] 8. Sample booted in Development with a dummy support SMTP host and `Email__Override__AllowedDomains__0=example.com`: logs `Running in Development: mail is sent only to allowed domains (example.com); everything else is withheld.` No errors.
- [~] 9. Push for user testing.
